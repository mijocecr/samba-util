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
    //  VALIDATE SHARE NAME (user-facing: English)
    // ---------------------------------------------------------
    
    private static void ValidateShareName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Share name cannot be empty.");

        // Samba-friendly: letters, numbers, dot, underscore, dash, dollar
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9._\-$]+$"))
            throw new InvalidOperationException(
                $"Invalid share name '{name}'. Allowed characters: letters, numbers, dot, underscore, dash, dollar."
            );

        // Reserved names
        var reserved = new[] { "global", "homes", "printers" };
        if (reserved.Any(r => r.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Share name '{name}' is reserved by Samba.");
    }


    // ---------------------------------------------------------
    //  SAVE ALL SHARES
    // ---------------------------------------------------------
    public static void SaveAllShares(IEnumerable<Share> shares)
    {
        var callId = ++_callCountSave;
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[WRITE] #{callId} → SaveAllShares started. Shares: {shares.Count()}");

        var config = ConfigManager.Load();
        string smbConf = config.SmbConfPath;
        string backup = smbConf + ".bak";
        string temp = "/tmp/samba-util-smb.conf";

        // 1) Read original smb.conf
        var originalLines = File.Exists(smbConf)
            ? File.ReadAllLines(smbConf).ToList()
            : new List<string>();

        var globalSection = ExtractGlobalSectionWithComments(originalLines);
        var includeLines = ExtractIncludeLines(originalLines);

        // 2) Generate temporary file
        var output = new List<string>();

        // Global section preserved (with comments)
        output.AddRange(globalSection);
        output.Add("");

        // Includes preserved
        foreach (var inc in includeLines)
            output.Add(inc);

        output.Add("");

        // Shares
        foreach (var s in shares)
        {
            ValidateShareName(s.Name);
            output.AddRange(ShareToLines(s));
            output.Add("");
        }

        File.WriteAllLines(temp, output);

        // 3) Backup
        ShellHelper.EjecutarComoRoot($"cp \"{smbConf}\" \"{backup}\"");

        // 4) Validate with testparm BEFORE copying
        var test = ShellHelper.EjecutarComoRoot($"testparm -s \"{temp}\"");
        if (test.ExitCode != 0)
        {
            Console.WriteLine($"[WRITE] #{callId} ERROR: testparm failed. smb.conf is invalid.");
            Console.WriteLine(test.Stderr);
            return;
        }

        // 5) Copy temporary file
        var copyResult = ShellHelper.EjecutarComoRoot($"cp \"{temp}\" \"{smbConf}\"");

        if (copyResult.ExitCode != 0)
        {
            Console.WriteLine($"[WRITE] #{callId} ERROR: failed to copy smb.conf");
            return;
        }

        // 6) Restart Samba (auto-detection)
        RestartSambaService();

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← SaveAllShares completed in {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------
    //  ADD SHARE
    // ---------------------------------------------------------
    public static void AddShare(Share newShare)
    {
        var callId = ++_callCountAdd;
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[WRITE] #{callId} → AddShare('{newShare.Name}')");

        ValidateShareName(newShare.Name);

        var shares = SambaConfigReader.LoadShares();

        if (shares.Any(s => s.Name.Equals(newShare.Name, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"[WRITE] #{callId} ERROR: duplicate share name.");
            throw new InvalidOperationException($"A share named '{newShare.Name}' already exists.");
        }

        shares.Add(newShare);

        SaveAllShares(shares);

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← AddShare completed in {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------
    //  DELETE SHARE
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
        Console.WriteLine($"[WRITE] #{callId} ← DeleteShare completed in {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------
    //  UPDATE SHARE
    // ---------------------------------------------------------
    public static void UpdateShare(Share updated)
    {
        var callId = ++_callCountUpdate;
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[WRITE] #{callId} → UpdateShare('{updated.Name}')");

        ValidateShareName(updated.Name);

        var shares = SambaConfigReader.LoadShares();
        var list = shares.Where(s => s.Name != updated.Name).ToList();
        list.Add(updated);

        SaveAllShares(list);

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← UpdateShare completed in {sw.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------
    //  PRESERVE [global] WITH COMMENTS
    // ---------------------------------------------------------
    private static List<string> ExtractGlobalSectionWithComments(List<string> lines)
    {
        var result = new List<string>();
        bool insideGlobal = false;
        bool globalFound = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Preserve comments before [global]
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
    //  PRESERVE includes
    // ---------------------------------------------------------
    private static List<string> ExtractIncludeLines(List<string> lines)
    {
        return lines
            .Where(l => l.Trim().StartsWith("include =", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ---------------------------------------------------------
    //  SHARE → smb.conf LINES
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
    //  ESCAPE PATHS WITH SPACES
    // ---------------------------------------------------------
    private static string EscapePath(string path)
    {
        return path.Contains(' ') ? $"\"{path}\"" : path;
    }

    // ---------------------------------------------------------
    //  RESTART SAMBA (AUTO-DETECTION)
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

        Console.WriteLine("[WRITE] WARNING: Could not detect service manager. Samba was not restarted automatically.");
    }
}
