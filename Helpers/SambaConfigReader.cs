using SAMBA_Util.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SAMBA_Util.Helpers;

public static class SambaConfigReader
{
    private static long _callCount = 0;

    private static bool IsTrue(string v) =>
        v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("1") ||
        v.Equals("y", StringComparison.OrdinalIgnoreCase);

    public static List<Share> LoadShares(string filePath = "/etc/samba/smb.conf")
    {
        var sw = Stopwatch.StartNew();
        var callId = ++_callCount;

        Console.WriteLine($"[CONF] #{callId} → LoadShares('{filePath}') iniciado");

        var shares = new List<Share>();

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[CONF] #{callId} Archivo no existe. Tiempo: {sw.ElapsedMilliseconds} ms");
            return shares;
        }

        Share? current = null;

        using var reader = new StreamReader(filePath);

        string? rawLine;
        while ((rawLine = reader.ReadLine()) != null)
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith(";"))
                continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                var name = line.Trim('[', ']');

                if (name.Equals("global", StringComparison.OrdinalIgnoreCase))
                {
                    current = null;
                    continue;
                }

                if (current != null)
                    shares.Add(current);

                current = new Share
                {
                    Name = name,
                    ReadOnly = true,
                    AllowGuests = false,
                    Browseable = true,
                    CreateMask = "0744",
                    DirectoryMask = "0755"
                };

                continue;
            }

            if (current == null)
                continue;

            var parts = line.Split('=', 2);
            if (parts.Length != 2)
                continue;

            var key = parts[0].Trim().ToLower();
            var value = parts[1].Trim().Trim('"');

            switch (key)
            {
                case "path":
                    current.Path = value;
                    break;

                case "read only":
                case "readonly":
                    current.ReadOnly = IsTrue(value);
                    break;

                case "writeable":
                case "writable":
                    current.ReadOnly = !IsTrue(value);
                    break;

                case "guest ok":
                case "public":
                    current.AllowGuests = IsTrue(value);
                    break;

                case "browseable":
                case "browsable":
                    current.Browseable = IsTrue(value);
                    break;

                case "comment":
                    current.Comment = value;
                    break;

                case "valid users":
                    current.ValidUsers = value;
                    break;

                case "write list":
                    current.WriteList = value;
                    break;

                case "read list":
                    current.ReadList = value;
                    break;

                case "force user":
                    current.ForceUser = value;
                    break;

                case "force group":
                    current.ForceGroup = value;
                    break;

                case "create mask":
                case "create mode":
                    current.CreateMask = value;
                    break;

                case "directory mask":
                case "directory mode":
                    current.DirectoryMask = value;
                    break;
            }
        }

        if (current != null)
            shares.Add(current);

        sw.Stop();
        Console.WriteLine($"[CONF] #{callId} ← LoadShares completado en {sw.ElapsedMilliseconds} ms. Shares: {shares.Count}");

        return shares;
    }
}
