namespace SAMBA_Util.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;

// Gestor de la regla Samba en el cortafuegos del sistema.
//
// SAMBA-Util solo toca gestores con una interfaz de alto nivel (UFW y
// firewalld). En sistemas con nftables/iptables "puros" no modifica reglas
// crudas: se limita a avisar para no romper configuraciones ajenas.
public static class FirewallHelper
{
    public enum Backend
    {
        None,
        Ufw,
        Firewalld,
        Nftables,
        Iptables
    }

    public sealed record Probe(Backend Backend, bool IsActive, string Message);

    // Resultado de intentar asegurar la regla Samba.
    public sealed record EnsureResult(bool Changed, string Message);

    // Puertos SMB/CIFS por si no existe el perfil de aplicación "Samba" en UFW.
    public static readonly string[] UfwPortRules = { "137,138/udp", "139,445/tcp" };

    // Nombre del servicio Samba tanto en UFW (perfil de aplicación) como en
    // firewalld (servicio `samba`).
    public const string SambaServiceName = "samba";

    // ---------------------------------------------------------
    // DETECCIÓN
    // ---------------------------------------------------------

    public static Probe Detect()
    {
        var ufw = ShellHelper.EjecutarComoRoot(
            "if command -v ufw >/dev/null 2>&1; then ufw status; else echo __UFW_NOT_INSTALLED__; fi");
        var ufwInstalled = !ufw.Stdout.Contains("__UFW_NOT_INSTALLED__", StringComparison.Ordinal);
        if (ufwInstalled && IsUfwActive(ufw.Stdout))
            return new Probe(Backend.Ufw, true, "UFW is active.");

        var firewalld = ShellHelper.EjecutarComoRoot(
            "if command -v firewall-cmd >/dev/null 2>&1; then firewall-cmd --state 2>&1; else echo __FIREWALLD_NOT_INSTALLED__; fi");
        var firewalldInstalled = !firewalld.Stdout.Contains("__FIREWALLD_NOT_INSTALLED__", StringComparison.Ordinal);
        if (firewalldInstalled && IsFirewalldRunning(firewalld.Stdout))
            return new Probe(Backend.Firewalld, true, "firewalld is active.");

        if (firewalldInstalled)
            return new Probe(Backend.Firewalld, false, "firewalld is installed but not running.");
        if (ufwInstalled)
            return new Probe(Backend.Ufw, false, "UFW is installed but inactive.");

        // Sin gestor de alto nivel: comprobamos si hay filtrado "puro" activo.
        var nft = ShellHelper.EjecutarComoRoot(
            "if command -v nft >/dev/null 2>&1; then nft list ruleset 2>/dev/null; else echo __NFT_NOT_INSTALLED__; fi");
        if (!nft.Stdout.Contains("__NFT_NOT_INSTALLED__", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(nft.Stdout))
            return new Probe(Backend.Nftables, true, "nftables rules are present.");

        var iptables = ShellHelper.EjecutarComoRoot(
            "if command -v iptables >/dev/null 2>&1; then iptables -S INPUT 2>/dev/null; else echo __IPTABLES_NOT_INSTALLED__; fi");
        if (!iptables.Stdout.Contains("__IPTABLES_NOT_INSTALLED__", StringComparison.Ordinal) &&
            HasRestrictiveInputPolicy(iptables.Stdout))
            return new Probe(Backend.Iptables, true, "iptables has a restrictive INPUT policy.");

        return new Probe(Backend.None, false,
            "No supported firewall manager (UFW/firewalld) was detected.");
    }

    // ---------------------------------------------------------
    // REGLA SAMBA
    // ---------------------------------------------------------

    // Asegura que el cortafuegos activo permite Samba. Devuelve un resultado
    // legible para mostrar en pantalla o registrar en el log.
    public static EnsureResult EnsureSambaAllowed()
    {
        var probe = Detect();
        switch (probe.Backend)
        {
            case Backend.Ufw when probe.IsActive:
                return EnsureUfw();
            case Backend.Firewalld when probe.IsActive:
                return EnsureFirewalld();
            case Backend.Nftables:
            case Backend.Iptables:
                return new EnsureResult(false,
                    probe.Message + " SAMBA-Util does not modify raw nftables/iptables rules; " +
                    "allow TCP 139/445 and UDP 137/138 manually if remote access fails.");
            default:
                // Sin cortafuegos activo (o solo instalado pero apagado) no hay
                // nada que permitir.
                return new EnsureResult(false, probe.Message);
        }
    }

    private static EnsureResult EnsureUfw()
    {
        // UFW incluye un perfil de aplicación llamado "Samba".
        var profile = ShellHelper.EjecutarComoRoot($"ufw allow {SambaServiceName}");
        if (profile.ExitCode == 0)
            return new EnsureResult(true, "UFW: allowed the Samba application profile.");

        // Si el perfil no existe, permitimos los puertos SMB/CIFS directamente.
        var errors = new List<string>();
        foreach (var rule in UfwPortRules)
        {
            var result = ShellHelper.EjecutarComoRoot($"ufw allow {rule}");
            if (result.ExitCode != 0)
                errors.Add(result.Stderr.Trim());
        }

        return errors.Count == 0
            ? new EnsureResult(true, "UFW: allowed SMB ports 137/138 (UDP) and 139/445 (TCP).")
            : new EnsureResult(false, "UFW: could not add the Samba rules. " + string.Join(" ", errors));
    }

    private static EnsureResult EnsureFirewalld()
    {
        var zones = GetActiveZones();
        if (zones.Length == 0)
        {
            var def = ShellHelper.EjecutarComoRoot("firewall-cmd --get-default-zone");
            var zone = def.Stdout.Trim();
            zones = new[] { string.IsNullOrWhiteSpace(zone) ? "public" : zone };
        }

        foreach (var zone in zones)
        {
            var quoted = ShellHelper.Quote(zone);
            // Regla en caliente y persistente: ambas son idempotentes.
            ShellHelper.EjecutarComoRoot($"firewall-cmd --zone={quoted} --add-service={SambaServiceName}");
            ShellHelper.EjecutarComoRoot($"firewall-cmd --zone={quoted} --permanent --add-service={SambaServiceName}");
        }

        ShellHelper.EjecutarComoRoot("firewall-cmd --reload");

        return new EnsureResult(true,
            $"firewalld: allowed the Samba service in zone(s): {string.Join(", ", zones)}.");
    }

    private static string[] GetActiveZones()
    {
        var result = ShellHelper.EjecutarComoRoot("firewall-cmd --get-active-zones");
        return ParseActiveZones(result.Stdout);
    }

    // ---------------------------------------------------------
    // PARSEO (funciones puras, comprobables con tests)
    // ---------------------------------------------------------

    public static bool IsUfwActive(string? ufwStatus) =>
        !string.IsNullOrWhiteSpace(ufwStatus) &&
        ufwStatus.Contains("Status: active", StringComparison.OrdinalIgnoreCase);

    public static bool IsFirewalldRunning(string? state) =>
        string.Equals(state?.Trim(), "running", StringComparison.OrdinalIgnoreCase);

    // `firewall-cmd --get-active-zones` lista las zonas sin sangría y sus
    // interfaces/entradas sangradas. Solo nos interesan los nombres de zona.
    public static string[] ParseActiveZones(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<string>();

        return output
            .Split('\n')
            .Where(line => line.Length > 0 && !char.IsWhiteSpace(line[0])) // los nombres de zona no van sangrados
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static bool HasRestrictiveInputPolicy(string? iptablesInput) =>
        !string.IsNullOrWhiteSpace(iptablesInput) &&
        iptablesInput.Split('\n').Any(line =>
            line.StartsWith("-P INPUT DROP", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("-P INPUT REJECT", StringComparison.OrdinalIgnoreCase));
}
