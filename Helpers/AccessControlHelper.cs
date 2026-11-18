namespace SAMBA_Util.Helpers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAMBA_Util.Models;

// Corrige los permisos de un recurso compartido teniendo en cuenta no solo
// los bits del sistema de archivos, sino también los controles de acceso
// obligatorios del sistema:
//   - SELinux (contexto samba_share_t)
//   - AppArmor (regla local del perfil de smbd)
//   - Propietario/grupo (usuario efectivo de Samba)
public static class AccessControlHelper
{
    public sealed record Step(string Name, bool Success, string Message);

    public sealed record Result(IReadOnlyList<Step> Steps)
    {
        public bool Success => Steps.All(step => step.Success);

        public string Summary => string.Join(" ", Steps.Select(step => step.Message));
    }

    // Rutas de AppArmor del perfil de smbd.
    public const string SmbdProfilePath = "/etc/apparmor.d/usr.sbin.smbd";
    public const string LocalProfilePath = "/etc/apparmor.d/local/usr.sbin.smbd";
    public const string LocalIncludeDirective = "#include <local/usr.sbin.smbd>";

    // Marcadores del bloque que gestiona SAMBA-Util dentro del fichero local.
    private const string MarkerBegin = "# BEGIN SAMBA-Util";
    private const string MarkerEnd = "# END SAMBA-Util";

    // ---------------------------------------------------------
    // PUNTO DE ENTRADA
    // ---------------------------------------------------------

    public static Result FixPermissions(Share share, string mode)
    {
        ArgumentNullException.ThrowIfNull(share);

        var steps = new List<Step>();
        var path = share.Path ?? "";

        if (string.IsNullOrWhiteSpace(path))
        {
            steps.Add(new Step("Filesystem permissions", false, "The share has no path."));
            return new Result(steps);
        }

        // 1) Bits del sistema de archivos (obligatorio: si falla, se detiene).
        var chmod = ShellHelper.EjecutarComoRoot(
            $"chmod -R {mode} -- {ShellHelper.Quote(path)}", ShellHelper.LongTimeoutMs);
        if (chmod.ExitCode != 0)
        {
            steps.Add(new Step("Filesystem permissions", false,
                $"Could not apply mode {mode}: {chmod.Stderr.Trim()}"));
            return new Result(steps);
        }
        steps.Add(new Step("Filesystem permissions", true, $"Applied mode {mode} recursively."));

        // 2) SELinux, 3) AppArmor y 4) propietario (mejor esfuerzo, se informa).
        steps.Add(FixSelinux(path));
        steps.Add(FixAppArmor(path));
        steps.Add(FixOwner(share));

        return new Result(steps);
    }

    // ---------------------------------------------------------
    // SELINUX
    // ---------------------------------------------------------

    public static bool IsSelinuxActive(string? getenforceOutput)
    {
        var value = getenforceOutput?.Trim();
        return string.Equals(value, "Enforcing", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Permissive", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSelinuxEnabled()
    {
        // `getenforce` no está en el PATH de todos los entornos; si falta,
        // /sys/fs/selinux/enforce indica que SELinux está compilado y activo.
        var result = ShellHelper.EjecutarComoRoot(
            "if getenforce >/dev/null 2>&1; then getenforce; " +
            "elif [ -e /sys/fs/selinux/enforce ]; then echo Enforcing; fi");
        return IsSelinuxActive(result.Stdout);
    }

    private static Step FixSelinux(string path)
    {
        if (!IsSelinuxEnabled())
            return new Step("SELinux", true, "SELinux is not enabled; skipped.");

        // `semanage fcontext` deja la regla persistente (sobrevive a relabel).
        var pattern = $"{path}(/.*)?";
        var add = ShellHelper.EjecutarComoRoot(
            $"semanage fcontext -a -t samba_share_t {ShellHelper.Quote(pattern)}");
        var persisted = add.ExitCode == 0;
        if (!persisted)
        {
            var modify = ShellHelper.EjecutarComoRoot(
                $"semanage fcontext -m -t samba_share_t {ShellHelper.Quote(pattern)}");
            persisted = modify.ExitCode == 0;
        }

        if (persisted)
        {
            var restore = ShellHelper.EjecutarComoRoot(
                $"restorecon -R {ShellHelper.Quote(path)}", ShellHelper.LongTimeoutMs);
            return restore.ExitCode == 0
                ? new Step("SELinux", true, "Applied the samba_share_t context with semanage + restorecon.")
                : new Step("SELinux", false, $"restorecon failed: {restore.Stderr.Trim()}");
        }

        // Alternativa no persistente cuando semanage no está instalado.
        var chcon = ShellHelper.EjecutarComoRoot(
            $"chcon -R -t samba_share_t -- {ShellHelper.Quote(path)}", ShellHelper.LongTimeoutMs);
        return chcon.ExitCode == 0
            ? new Step("SELinux", true,
                "Applied the samba_share_t context with chcon (not persistent; install policycoreutils-python-utils for semanage).")
            : new Step("SELinux", false, $"Could not set the SELinux context: {chcon.Stderr.Trim()}");
    }

    // ---------------------------------------------------------
    // APPARMOR
    // ---------------------------------------------------------

    public static bool IsAppArmorActive(string? probeOutput) =>
        string.Equals(probeOutput?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);

    public static bool HasLocalInclude(string? profileContent) =>
        !string.IsNullOrWhiteSpace(profileContent) &&
        profileContent.Contains(LocalIncludeDirective, StringComparison.Ordinal);

    private static bool IsAppArmorEnabled()
    {
        var result = ShellHelper.EjecutarComoRoot(
            "if [ -d /sys/kernel/security/apparmor ] || aa-status --enabled >/dev/null 2>&1; then echo yes; fi");
        return IsAppArmorActive(result.Stdout);
    }

    private static Step FixAppArmor(string path)
    {
        if (!IsAppArmorEnabled())
            return new Step("AppArmor", true, "AppArmor is not enabled; skipped.");

        // El perfil principal debe incluir el fichero local para poder ampliarlo.
        var profile = ShellHelper.EjecutarComoRoot(
            $"cat {ShellHelper.Quote(SmbdProfilePath)} 2>/dev/null");
        if (profile.ExitCode != 0 || string.IsNullOrWhiteSpace(profile.Stdout))
            return new Step("AppArmor", true,
                $"No '{SmbdProfilePath}' profile was found; AppArmor rules were not changed.");

        if (!HasLocalInclude(profile.Stdout))
            return new Step("AppArmor", false,
                $"The '{SmbdProfilePath}' profile does not include '{LocalIncludeDirective}'. " +
                "Add that include manually so SAMBA-Util can grant access to the share path.");

        var existing = ShellHelper.EjecutarComoRoot(
            $"cat {ShellHelper.Quote(LocalProfilePath)} 2>/dev/null");
        var currentContent = existing.ExitCode == 0 ? existing.Stdout : "";

        var updated = BuildLocalProfile(currentContent, path);

        var localDir = Path.GetDirectoryName(LocalProfilePath) ?? "/etc/apparmor.d/local";
        ShellHelper.EjecutarComoRoot($"mkdir -p {ShellHelper.Quote(localDir)}");

        var write = ShellHelper.EjecutarComoRootConEntrada(
            $"cat > {ShellHelper.Quote(LocalProfilePath)}", updated);
        if (write.ExitCode != 0)
            return new Step("AppArmor", false,
                $"Could not update {LocalProfilePath}: {write.Stderr.Trim()}");

        var reload = ShellHelper.EjecutarComoRoot(
            $"apparmor_parser -r {ShellHelper.Quote(SmbdProfilePath)}", ShellHelper.LongTimeoutMs);
        return reload.ExitCode == 0
            ? new Step("AppArmor", true, $"Granted access to '{path}' in the local smbd profile.")
            : new Step("AppArmor", false, $"Reloading the smbd profile failed: {reload.Stderr.Trim()}");
    }

    // Construye el contenido del fichero local de AppArmor: conserva lo que ya
    // hubiera y sustituye el bloque gestionado por SAMBA-Util. Es idempotente.
    public static string BuildLocalProfile(string? existingContent, string path)
    {
        var normalized = (existingContent ?? "").Replace("\r\n", "\n");
        var lines = normalized.Split('\n').ToList();

        var kept = new List<string>();
        var insideManagedBlock = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed == MarkerBegin)
            {
                insideManagedBlock = true;
                continue;
            }
            if (trimmed == MarkerEnd)
            {
                insideManagedBlock = false;
                continue;
            }
            if (!insideManagedBlock)
                kept.Add(line);
        }

        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1]))
            kept.RemoveAt(kept.Count - 1);

        if (kept.Count > 0)
            kept.Add("");

        var escaped = EscapeAppArmorPath(path);
        kept.Add(MarkerBegin);
        kept.Add($"# Rules for: {path}");
        kept.Add($"\"{escaped}/\" r,");
        kept.Add($"\"{escaped}/**\" rwk,");
        kept.Add(MarkerEnd);
        kept.Add("");

        return string.Join("\n", kept);
    }

    // AppArmor usa comillas dobles para rutas con caracteres especiales;
    // hay que escapar la barra invertida y las propias comillas.
    public static string EscapeAppArmorPath(string path) =>
        path.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    // ---------------------------------------------------------
    // PROPIETARIO / GRUPO
    // ---------------------------------------------------------

    private static Step FixOwner(Share share)
    {
        var path = share.Path ?? "";
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return new Step("Ownership", false,
                "The share path does not exist on disk; ownership was not changed.");

        var effectiveUser = ShareValidator.GetEffectiveUser(share);

        // "smbuser" es un marcador genérico: sin un usuario real no hay destino.
        if (effectiveUser == ShareValidator.GenericSmbUser)
            return new Step("Ownership", true,
                "No concrete owner target (no force user and no guest access); ownership was left unchanged.");

        var group = string.IsNullOrWhiteSpace(share.ForceGroup) ? null : share.ForceGroup;
        var spec = group is null ? $"{effectiveUser}:" : $"{effectiveUser}:{group}";

        var result = ShellHelper.EjecutarComoRoot(
            $"chown -R {ShellHelper.Quote(spec)} -- {ShellHelper.Quote(path)}", ShellHelper.LongTimeoutMs);

        return result.ExitCode == 0
            ? new Step("Ownership", true, $"Ownership set to '{spec.TrimEnd(':')}'.")
            : new Step("Ownership", false, $"Could not change ownership: {result.Stderr.Trim()}");
    }
}
