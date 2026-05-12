using SAMBA_Util.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SAMBA_Util.Helpers;

public static class SambaConfigWriter
{
    private static long _callCountSave = 0;
    private static long _callCountAdd = 0;
    private static long _callCountDelete = 0;
    private static long _callCountUpdate = 0;

    // ---------------------------------------------------------
    //  GUARDAR TODOS LOS SHARES
    // ---------------------------------------------------------
    public static void SaveAllShares(IEnumerable<Share> shares)
    {
        var callId = ++_callCountSave;
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[WRITE] #{callId} → SaveAllShares iniciado. Shares: {shares.Count()}");

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

        // 2) Generar archivo temporal
        var output = new List<string>();

        // Sección global preservada
        output.AddRange(globalSection);
        output.Add("");

        // Includes preservados
        foreach (var inc in includeLines)
            output.Add(inc);

        output.Add("");

        // Shares
        foreach (var s in shares)
        {
            output.AddRange(ShareToLines(s));
            output.Add("");
        }

        File.WriteAllLines(temp, output);

        // 3) Backup
        ShellHelper.EjecutarComoRoot($"cp \"{smbConf}\" \"{backup}\"");

        // 4) Validar con testparm ANTES de copiar
        var test = ShellHelper.EjecutarComoRoot($"testparm -s \"{temp}\"");
        if (test.ExitCode != 0)
        {
            Console.WriteLine($"[WRITE] #{callId} ERROR: testparm falló. smb.conf inválido.");
            Console.WriteLine(test.Stderr);
            return;
        }

        // 5) Copiar archivo temporal
        var copyResult = ShellHelper.EjecutarComoRoot($"cp \"{temp}\" \"{smbConf}\"");

        if (copyResult.ExitCode != 0)
        {
            Console.WriteLine($"[WRITE] #{callId} ERROR al copiar smb.conf");
            return;
        }

        // 6) Reiniciar Samba (autodetección)
        RestartSambaService();

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← SaveAllShares completado en {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------
    //  AÑADIR SHARE
    // ---------------------------------------------------------
    public static void AddShare(Share newShare)
    {
        var callId = ++_callCountAdd;
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[WRITE] #{callId} → AddShare('{newShare.Name}')");

        var shares = SambaConfigReader.LoadShares();

        if (shares.Any(s => s.Name.Equals(newShare.Name, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"[WRITE] #{callId} ERROR: duplicado");
            throw new InvalidOperationException($"A share named '{newShare.Name}' already exists.");
        }

        shares.Add(newShare);

        SaveAllShares(shares);

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← AddShare completado en {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------
    //  ELIMINAR SHARE
    // ---------------------------------------------------------
    public static void DeleteShare(string name)
    {
        var callId = ++_callCountDelete;
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[WRITE] #{callId} → DeleteShare('{name}')");

        var shares = SambaConfigReader.LoadShares();
        var filtered = shares.Where(s => s.Name != name).ToList();

        SaveAllShares(filtered);

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← DeleteShare completado en {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------
    //  ACTUALIZAR SHARE
    // ---------------------------------------------------------
    public static void UpdateShare(Share updated)
    {
        var callId = ++_callCountUpdate;
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[WRITE] #{callId} → UpdateShare('{updated.Name}')");

        var shares = SambaConfigReader.LoadShares();
        var list = shares.Where(s => s.Name != updated.Name).ToList();
        list.Add(updated);

        SaveAllShares(list);

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← UpdateShare completado en {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------
    //  PRESERVAR [global]
    // ---------------------------------------------------------
    private static List<string> ExtractGlobalSection(List<string> lines)
    {
        var result = new List<string>();
        bool insideGlobal = false;

        foreach (var line in lines)
        {
            if (line.Trim().StartsWith("[global]", StringComparison.OrdinalIgnoreCase))
            {
                insideGlobal = true;
                result.Add(line);
                continue;
            }

            if (insideGlobal)
            {
                if (line.Trim().StartsWith("[") && !line.Trim().StartsWith("[global]"))
                    break;

                result.Add(line);
            }
        }

        return result.Count > 0 ? result : new List<string> { "[global]" };
    }

    // ---------------------------------------------------------
    //  PRESERVAR includes
    // ---------------------------------------------------------
    private static List<string> ExtractIncludeLines(List<string> lines)
    {
        return lines
            .Where(l => l.Trim().StartsWith("include =", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ---------------------------------------------------------
    //  SHARE → LÍNEAS DE smb.conf
    // ---------------------------------------------------------
    private static IEnumerable<string> ShareToLines(Share s)
    {
        yield return $"[{s.Name}]";
        yield return $"   path = {EscapePath(s.Path)}";
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
    }

    // ---------------------------------------------------------
    //  ESCAPAR RUTAS CON ESPACIOS
    // ---------------------------------------------------------
    private static string EscapePath(string path)
    {
        return path.Contains(' ') ? $"\"{path}\"" : path;
    }

    // ---------------------------------------------------------
    //  REINICIAR SAMBA (AUTODETECCIÓN)
    // ---------------------------------------------------------
    private static void RestartSambaService()
    {
        if (File.Exists("/bin/systemctl"))
        {
            ShellHelper.EjecutarComoRoot("systemctl restart smbd || systemctl restart smb");
            return;
        }

        if (File.Exists("/sbin/service"))
        {
            ShellHelper.EjecutarComoRoot("service smbd restart || service smb restart");
            return;
        }

        // OpenRC
        if (File.Exists("/sbin/rc-service"))
        {
            ShellHelper.EjecutarComoRoot("rc-service samba restart");
            return;
        }

        Console.WriteLine("[WRITE] ADVERTENCIA: No se pudo detectar el gestor de servicios.");
    }
}
