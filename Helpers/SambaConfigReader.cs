using SAMBA_Util.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SAMBA_Util.Helpers;

public static class SambaConfigReader
{
    private static long _callCount = 0;

    private static bool IsTrue(string v) =>
        v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("1") ||
        v.Equals("y", StringComparison.OrdinalIgnoreCase);

    public static List<Share> LoadShares(string filePath = null)
    {
        var sw = Stopwatch.StartNew();
        var callId = ++_callCount;

        var config = ConfigManager.Load();
        filePath ??= config.SmbConfPath;

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
            rawLine = rawLine.Replace("\t", " ");

            // Quitar comentarios al final de línea
            var clean = rawLine.Split('#')[0].Split(';')[0].Trim();

            if (string.IsNullOrWhiteSpace(clean))
                continue;

            // Includes
            if (clean.StartsWith("include =", StringComparison.OrdinalIgnoreCase))
            {
                var includePath = clean.Split('=', 2)[1].Trim().Trim('"');

                if (File.Exists(includePath))
                {
                    Console.WriteLine($"[CONF] #{callId} → Procesando include: {includePath}");

                    var included = LoadShares(includePath);

                    // evitar duplicados
                    foreach (var s in included)
                    {
                        if (!shares.Any(x => x.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase)))
                            shares.Add(s);
                    }
                }
                continue;
            }

            // Multiline
            while (clean.EndsWith("\\"))
            {
                clean = clean.TrimEnd('\\').Trim();
                var next = reader.ReadLine()?.Trim() ?? "";
                clean += " " + next;
            }

            // Nueva sección
            if (clean.StartsWith("[") && clean.EndsWith("]"))
            {
                var name = clean.Substring(1, clean.Length - 2);

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
                    DirectoryMask = "0755",
                    UnknownParameters = new List<string>()
                };

                continue;
            }

            if (current == null)
                continue;

            // clave=valor
            var parts = clean.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                current.UnknownParameters.Add(clean);
                continue;
            }

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

                default:
                    current.UnknownParameters.Add(clean);
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
