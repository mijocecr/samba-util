using SAMBA_Util.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SAMBA_Util.Helpers;

public static class SambaConfigWriter
{
    private static long _callCountSave = 0;

    // ---------------------------------------------------------
    // SPECIAL SHARES (preserve if present)
    // ---------------------------------------------------------
    private static bool IsSpecialShare(string name)
    {
        return name.Equals("printers", StringComparison.OrdinalIgnoreCase)
            || name.Equals("print$", StringComparison.OrdinalIgnoreCase)
            || name.Equals("IPC$", StringComparison.OrdinalIgnoreCase)
            || name.Equals("homes", StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------
    // VALIDATE SHARE NAME
    // ---------------------------------------------------------
    private static void ValidateShareName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Share name cannot be empty.");

        if (!Regex.IsMatch(name, @"^[A-Za-z0-9._\-$ ]+$"))
            throw new InvalidOperationException(
                $"Invalid share name '{name}'. Allowed characters: letters, numbers, space, dot, underscore, dash, dollar."
            );

        if (name.Equals("global", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Share name 'global' is reserved.");
    }

    // ---------------------------------------------------------
    // SAVE ALL SHARES
    // ---------------------------------------------------------
    public static void SaveAllShares(IEnumerable<Share> uiShares)
    {
        var callId = ++_callCountSave;
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[WRITE] #{callId} → SaveAllShares iniciado. Shares: {uiShares.Count()}");

        var config = ConfigManager.Load();
        string smbConf = config.SmbConfPath;
        string backup = smbConf + ".bak";
        string temp = "/tmp/samba-util-smb.conf";

        // 1) Leer smb.conf original
        var originalLines = File.Exists(smbConf)
            ? File.ReadAllLines(smbConf).ToList()
            : new List<string>();

        var globalSection = ExtractGlobalSection(originalLines);
        var includeLines = ExtractIncludeLines(originalLines);

        // Cargar originales para preservar especiales no incluidos en la UI
        var originalShares = SambaConfigReader.LoadShares();

        // 2) Generar archivo temporal
        var output = new List<string>();

        // Sección global preservada
        output.AddRange(globalSection);
        output.Add("");

        // Includes preservados
        foreach (var inc in includeLines)
            output.Add(inc);

        output.Add("");

        // Identificar qué shares vienen de la UI para evitar duplicar especiales
        var uiShareNames = uiShares.Select(x => x.Name).ToList();

        // Preservar shares especiales SOLAMENTE si no están ya en uiShares
        foreach (var s in originalShares)
        {
            if (IsSpecialShare(s.Name) && !uiShareNames.Any(u => u.Equals(s.Name, StringComparison.OrdinalIgnoreCase)))
            {
                output.AddRange(ShareToLines(s));
                output.Add("");
            }
        }

        // Shares gestionados por la UI
        foreach (var s in uiShares)
        {
            ValidateShareName(s.Name);
            output.AddRange(ShareToLines(s));
            output.Add("");
        }

        File.WriteAllLines(temp, output);

        // 3) Backup del archivo original
        if (File.Exists(smbConf))
        {
            ShellHelper.EjecutarComoRoot($"cp \"{smbConf}\" \"{backup}\"");
        }

        // 4) Validar con testparm ANTES de aplicar cambios
        var test = ShellHelper.EjecutarComoRoot($"testparm -s \"{temp}\"");
        if (test.ExitCode != 0)
        {
            Console.WriteLine($"[WRITE] #{callId} ERROR: testparm falló. smb.conf inválido.");
            Console.WriteLine(test.Stderr);
            throw new InvalidOperationException($"La configuración de Samba generada no es válida: {test.Stderr}");
        }

        // 5) Aplicar el archivo temporal al archivo de configuración real
        var copyResult = ShellHelper.EjecutarComoRoot($"cp \"{temp}\" \"{smbConf}\"");

        if (copyResult.ExitCode != 0)
        {
            Console.WriteLine($"[WRITE] #{callId} ERROR al copiar smb.conf");
            throw new InvalidOperationException("No se pudo escribir el archivo /etc/samba/smb.conf. Verifica permisos de root.");
        }

        // 6) Reiniciar Samba
        RestartSambaService();

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← SaveAllShares completado en {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------
    // AÑADIR SHARE
    // ---------------------------------------------------------
    public static void AddShare(Share newShare)
    {
        ValidateShareName(newShare.Name);

        var shares = SambaConfigReader.LoadShares();

        if (shares.Any(s => s.Name.Equals(newShare.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Ya existe un recurso compartido llamado '{newShare.Name}'.");

        shares.Add(newShare);
        SaveAllShares(shares);
    }

    // ---------------------------------------------------------
    // ELIMINAR SHARE
    // ---------------------------------------------------------
    public static void DeleteShare(string name)
    {
        var shares = SambaConfigReader.LoadShares();
        var filtered = shares.Where(s => !s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();

        SaveAllShares(filtered);
    }

    // ---------------------------------------------------------
    // ACTUALIZAR SHARE
    // ---------------------------------------------------------
    public static void UpdateShare(Share updated)
    {
        ValidateShareName(updated.Name);

        var shares = SambaConfigReader.LoadShares();
        var list = shares.Where(s => !s.Name.Equals(updated.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Add(updated);

        SaveAllShares(list);
    }

    // ---------------------------------------------------------
    // HELPER: PRESERVAR [global]
    // ---------------------------------------------------------
    private static List<string> ExtractGlobalSection(List<string> lines)
    {
        var result = new List<string>();
        bool insideGlobal = false;

        foreach (var line in lines)
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("[global]", StringComparison.OrdinalIgnoreCase))
            {
                insideGlobal = true;
                result.Add(line);
                continue;
            }

            if (insideGlobal)
            {
                if (trimmed.StartsWith("[") && !trimmed.StartsWith("[global]", StringComparison.OrdinalIgnoreCase))
                    break;

                result.Add(line);
            }
        }

        return result.Count > 0 ? result : new List<string> { "[global]" };
    }

    // ---------------------------------------------------------
    // HELPER: PRESERVAR INCLUDES
    // ---------------------------------------------------------
    private static List<string> ExtractIncludeLines(List<string> lines)
    {
        return lines
            .Where(l => l.Trim().StartsWith("include =", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ---------------------------------------------------------
    // SHARE → LÍNEAS DE smb.conf
    // ---------------------------------------------------------
    private static IEnumerable<string> ShareToLines(Share s)
    {
        yield return $"[{s.Name}]";
        yield return $"   path = {CleanPath(s.Path)}";
        yield return $"   read only = {(s.ReadOnly ? "yes" : "no")}";
        yield return $"   guest ok = {(s.AllowGuests ? "yes" : "no")}";
        yield return $"   browseable = {(s.Browseable ? "yes" : "no")}";

        if (!string.IsNullOrWhiteSpace(s.Comment))
            yield return $"   comment = {s.Comment}";

        if (!string.IsNullOrWhiteSpace(s.ValidUsers))
            yield return $"   valid users = {s.ValidUsers}";

        if (!string.IsNullOrWhiteSpace(s.WriteList))
            yield return $"   write list = {s.WriteList}";

        if (!string.IsNullOrWhiteSpace(s.ReadList))
            yield return $"   read list = {s.ReadList}";

        if (!string.IsNullOrWhiteSpace(s.ForceUser))
            yield return $"   force user = {s.ForceUser}";

        if (!string.IsNullOrWhiteSpace(s.ForceGroup))
            yield return $"   force group = {s.ForceGroup}";

        if (!string.IsNullOrWhiteSpace(s.CreateMask))
            yield return $"   create mask = {s.CreateMask}";

        if (!string.IsNullOrWhiteSpace(s.DirectoryMask))
            yield return $"   directory mask = {s.DirectoryMask}";

        // Preservar cualquier parámetro avanzado no mapeado explícitamente en la UI
        if (s.UnknownParameters != null)
        {
            foreach (var param in s.UnknownParameters)
            {
                if (!string.IsNullOrWhiteSpace(param))
                    yield return $"   {param}";
            }
        }
    }

    /// <summary>
    /// Limpia las rutas asegurando que NUNCA lleven comillas en Samba.
    /// </summary>
    private static string CleanPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return path.Trim().Trim('"');
    }

    private static void RestartSambaService()
    {
        if (File.Exists("/bin/systemctl") || File.Exists("/usr/bin/systemctl"))
        {
            var res = ShellHelper.EjecutarComoRoot("systemctl restart smbd");
            if (res.ExitCode != 0)
            {
                ShellHelper.EjecutarComoRoot("systemctl restart smb");
            }
            return;
        }

        if (File.Exists("/sbin/service") || File.Exists("/usr/sbin/service"))
        {
            var res = ShellHelper.EjecutarComoRoot("service smbd restart");
            if (res.ExitCode != 0)
            {
                ShellHelper.EjecutarComoRoot("service smb restart");
            }
            return;
        }

        if (File.Exists("/sbin/rc-service"))
        {
            ShellHelper.EjecutarComoRoot("rc-service samba restart");
            return;
        }

        Console.WriteLine("[WRITE] ADVERTENCIA: No se pudo detectar el gestor de servicios para reiniciar Samba.");
    }
}