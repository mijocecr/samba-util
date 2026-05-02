using SAMBA_Util.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SAMBA_Util.Helpers;

public static class SambaConfigWriter
{
    private const string SmbConfPath = "/etc/samba/smb.conf";
    private const string TempPath = "/tmp/smb.conf";

    private static long _callCountSave = 0;
    private static long _callCountAdd = 0;
    private static long _callCountDelete = 0;
    private static long _callCountUpdate = 0;

    public static void SaveAllShares(IEnumerable<Share> shares)
    {
        var callId = ++_callCountSave;
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[WRITE] #{callId} → SaveAllShares iniciado. Shares: {shares.Count()}");

        // 1) Generar archivo temporal
        var swWrite = Stopwatch.StartNew();
        var lines = shares.Select(s => ShareToText(s)).ToList();
        File.WriteAllLines(TempPath, lines);
        swWrite.Stop();
        Console.WriteLine($"[WRITE] #{callId} Archivo temporal escrito en {swWrite.ElapsedMilliseconds} ms");

        // 2) Copiar a /etc/samba/smb.conf como root
        var swCopy = Stopwatch.StartNew();
        var copyResult = ShellHelper.EjecutarComoRoot($"cp \"{TempPath}\" \"{SmbConfPath}\"");
        swCopy.Stop();
        Console.WriteLine($"[WRITE] #{callId} Copia a smb.conf en {swCopy.ElapsedMilliseconds} ms");

        // 3) Reiniciar Samba
        var swRestart = Stopwatch.StartNew();
        var restartResult = ShellHelper.EjecutarComoRoot("systemctl restart smbd");
        swRestart.Stop();
        Console.WriteLine($"[WRITE] #{callId} systemctl restart smbd en {swRestart.ElapsedMilliseconds} ms");

        sw.Stop();
        Console.WriteLine($"[WRITE] #{callId} ← SaveAllShares completado en {sw.ElapsedMilliseconds} ms");
    }

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

    private static string ShareToText(Share s)
    {
        var lines = new List<string>
        {
            $"[{s.Name}]",
            $"   path = {s.Path}",
            $"   read only = {(s.ReadOnly ? "yes" : "no")}",
            $"   guest ok = {(s.AllowGuests ? "yes" : "no")}",
            $"   browseable = {(s.Browseable ? "yes" : "no")}"
        };

        if (!string.IsNullOrWhiteSpace(s.Comment))
            lines.Add($"   comment = {s.Comment}");

        if (!string.IsNullOrWhiteSpace(s.ValidUsers))
            lines.Add($"   valid users = {s.ValidUsers}");

        if (!string.IsNullOrWhiteSpace(s.WriteList))
            lines.Add($"   write list = {s.WriteList}");

        if (!string.IsNullOrWhiteSpace(s.ReadList))
            lines.Add($"   read list = {s.ReadList}");

        if (!string.IsNullOrWhiteSpace(s.ForceUser))
            lines.Add($"   force user = {s.ForceUser}");

        if (!string.IsNullOrWhiteSpace(s.ForceGroup))
            lines.Add($"   force group = {s.ForceGroup}");

        if (!string.IsNullOrWhiteSpace(s.CreateMask))
            lines.Add($"   create mask = {s.CreateMask}");

        if (!string.IsNullOrWhiteSpace(s.DirectoryMask))
            lines.Add($"   directory mask = {s.DirectoryMask}");

        lines.Add("");

        return string.Join(Environment.NewLine, lines);
    }
}
