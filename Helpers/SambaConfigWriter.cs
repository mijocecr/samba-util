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
    private static long _callCountAdd = 0;
    private static long _callCountDelete = 0;
    private static long _callCountUpdate = 0;

    // ---------------------------------------------------------
    // SPECIAL SHARES (must always be preserved)
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

        if (!Regex.IsMatch(name, @"^[A-Za-z0-9._\-$]+$"))
            throw new InvalidOperationException(
                $"Invalid share name '{name}'. Allowed characters: letters, numbers, dot, underscore, dash, dollar."
            );

        var reserved = new[] { "global", "homes" };
        if (reserved.Any(r => r.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Share name '{name}' is reserved by Samba.");
    }

    // ---------------------------------------------------------
    // SAVE ALL SHARES (non-destructive)
    // ---------------------------------------------------------
    public static void SaveAllShares(IEnumerable<Share> uiShares)
    {
        var callId = ++_callCountSave;
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[WRITE] #{callId} → SaveAllShares started. UI Shares: {uiShares.Count()}");

        var config = ConfigManager.Load();
        string smbConf = config.SmbConfPath;
        string backup = smbConf + ".bak";
        string temp = "/tmp/samba-util-smb.conf";

        // Read original file
        var originalLines = File.Exists(smbConf)
            ? File.ReadAllLines(smbConf).ToList()
            : new List<string>();

        var globalSection = ExtractGlobalSectionWithComments(originalLines);
        var includeLines = ExtractIncludeLines(originalLines);

        // Load shares from includes
        var includeShares = new List<Share>();
        foreach (var inc in includeLines)
        {
            var includePath = inc.Split('=', 2)[1].Trim().Trim('"');
            if (File.Exists(includePath))
                includeShares.AddRange(SambaConfigReader.LoadShares(includePath));
        }

        // Load original shares
        var originalShares = SambaConfigReader.LoadShares();

        // Build output
        var output = new List<string>();

        // Global section
        output.AddRange(globalSection);
        output.Add("");

        // Includes
        foreach (var inc in includeLines)
            output.Add(inc);

        output.Add("");

        // 1) Write special shares from original file
        foreach (var s in originalShares)
        {
            if (IsSpecialShare(s.Name))
            {
                output.AddRange(ShareToLines(s));
                output.Add("");
            }
        }

        // 2) Write shares from includes
        foreach (var s in includeShares)
        {
            output.AddRange(ShareToLines(s));
            output.Add("");
        }

        // 3) Write UI-managed shares
        foreach (var s in uiShares)
        {
            ValidateShareName(s.Name);
            output.AddRange(ShareToLines(s));
            output.Add("");
        }

        File.WriteAllLines(temp, output);

        // Backup
        ShellHelper.EjecutarComoRoot($"cp \"{smbConf}\" \"{backup}\"");

        // Validate
        var test = ShellHelper.EjecutarComoRoot($"testparm -s \"{temp}\"");
        if (test.ExitCode != 0)
        {
            Console.WriteLine($"[WRITE] #{callId} ERROR: testparm failed.");
            Console.WriteLine(test.Stderr);
            return;
        }

        // Copy
        var copyResult = ShellHelper.EjecutarComoRoot($"cp \"{temp}\" \"{smbConf}\"");
        if (copyResult.ExitCode != 0)
        {
            Console.WriteLine($"[WRITE] #{callId} ERROR: failed to copy smb.conf");
            return;
        }

        RestartSambaService();

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← SaveAllShares completed in {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------
    // ADD SHARE
    // ---------------------------------------------------------
    public static void AddShare(Share newShare)
    {
        var callId = ++_callCountAdd;
        var sw = Stopwatch.StartNew();

        ValidateShareName(newShare.Name);

        var shares = SambaConfigReader.LoadShares();

        if (shares.Any(s => s.Name.Equals(newShare.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A share named '{newShare.Name}' already exists.");

        shares.Add(newShare);

        SaveAllShares(shares);

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← AddShare completed in {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------
    // DELETE SHARE
    // ---------------------------------------------------------
    public static void DeleteShare(string name)
    {
        var callId = ++_callCountDelete;
        var sw = Stopwatch.StartNew();

        var shares = SambaConfigReader.LoadShares();
        var filtered = shares.Where(s => s.Name != name).ToList();

        SaveAllShares(filtered);

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← DeleteShare completed in {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------
    // UPDATE SHARE
    // ---------------------------------------------------------
    public static void UpdateShare(Share updated)
    {
        var callId = ++_callCountUpdate;
        var sw = Stopwatch.StartNew();

        ValidateShareName(updated.Name);

        var shares = SambaConfigReader.LoadShares();
        var list = shares.Where(s => s.Name != updated.Name).ToList();
        list.Add(updated);

        SaveAllShares(list);

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← UpdateShare completed in {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------
    // PRESERVE [global]
    // ---------------------------------------------------------
    private static List<string> ExtractGlobalSectionWithComments(List<string> lines)
    {
        var result = new List<string>();
        bool insideGlobal = false;
        bool globalFound = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (!globalFound && trimmed.StartsWith("#"))
            {
                result.Add(line);
                continue;
            }

            if (trimmed.StartsWith("[global]", StringComparison.OrdinalIgnoreCase))
            {
                insideGlobal = true;
                globalFound = true;
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

        if (result.Count == 0)
            return new List<string> { "[global]" };

        return result;
    }

    // ---------------------------------------------------------
    // PRESERVE includes
    // ---------------------------------------------------------
    private static List<string> ExtractIncludeLines(List<string> lines)
    {
        return lines
            .Where(l => l.Trim().StartsWith("include =", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ---------------------------------------------------------
    // SHARE → smb.conf LINES
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
    // ESCAPE PATHS
    // ---------------------------------------------------------
    private static string EscapePath(string path)
    {
        return path.Contains(' ') ? $"\"{path}\"" : path;
    }

    // ---------------------------------------------------------
    // RESTART SAMBA
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

        if (File.Exists("/sbin/rc-service"))
        {
            ShellHelper.EjecutarComoRoot("rc-service samba restart");
            return;
        }

        Console.WriteLine("[WRITE] WARNING: Could not detect service manager.");
    }
}
