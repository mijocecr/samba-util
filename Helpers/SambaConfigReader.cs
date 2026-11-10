using SAMBA_Util.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SAMBA_Util.Helpers;

public static class SambaConfigReader
{
    private static long _callCount;

    private static bool IsTrue(string value) =>
        value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("1") ||
        value.Equals("y", StringComparison.OrdinalIgnoreCase);

    public static List<Share> LoadShares(string? filePath = null)
    {
        var sw = Stopwatch.StartNew();
        var callId = ++_callCount;

        filePath ??= ConfigManager.Load().SmbConfPath;
        Console.WriteLine($"[CONF] #{callId} → LoadShares('{filePath}') iniciado");

        var shares = new List<Share>();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            Console.WriteLine($"[CONF] #{callId} Archivo no existe o ruta inválida. Tiempo: {sw.ElapsedMilliseconds} ms");
            return shares;
        }

        Share? current = null;
        var includeStack = new HashSet<string>(StringComparer.Ordinal);
        ParseFile(Path.GetFullPath(filePath), shares, ref current, includeStack);

        sw.Stop();
        Console.WriteLine($"[CONF] #{callId} ← LoadShares completado en {sw.ElapsedMilliseconds} ms. Shares: {shares.Count}");
        return shares;
    }

    private static void ParseFile(
        string filePath,
        List<Share> shares,
        ref Share? current,
        HashSet<string> includeStack)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        if (!includeStack.Add(normalizedPath))
        {
            throw new InvalidOperationException($"Se detectó una inclusión circular en la configuración Samba: {normalizedPath}");
        }

        try
        {
            using var reader = new StreamReader(normalizedPath);
            string? rawLine;
            while ((rawLine = reader.ReadLine()) != null)
            {
                var clean = rawLine.Replace("\t", " ").Trim();
                if (string.IsNullOrWhiteSpace(clean) || clean.StartsWith("#") || clean.StartsWith(";"))
                    continue;

                clean = StripInlineComment(clean);
                if (string.IsNullOrWhiteSpace(clean))
                    continue;

                while (clean.EndsWith("\\", StringComparison.Ordinal))
                {
                    clean = clean[..^1].TrimEnd();
                    var nextLine = reader.ReadLine();
                    if (nextLine == null)
                        break;

                    var continuation = StripInlineComment(nextLine.Replace("\t", " ").Trim());
                    if (!string.IsNullOrEmpty(continuation))
                        clean += " " + continuation;
                }

                var parts = clean.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && parts[0].Equals("include", StringComparison.OrdinalIgnoreCase))
                {
                    var includeValue = parts[1].Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(includeValue))
                        throw new FormatException($"La directiva include no contiene una ruta en '{normalizedPath}'.");

                    if (includeValue.Contains('%'))
                        throw new NotSupportedException($"No se pueden resolver inclusiones Samba con variables (%): '{includeValue}'.");

                    var includePath = Path.IsPathRooted(includeValue)
                        ? includeValue
                        : Path.Combine(Path.GetDirectoryName(normalizedPath)!, includeValue);

                    if (!File.Exists(includePath))
                        throw new FileNotFoundException($"No se encontró el archivo incluido '{includePath}'.", includePath);

                    Console.WriteLine($"[CONF] Procesando include: {includePath}");
                    var includeCurrent = current;
                    ParseFile(includePath, shares, ref includeCurrent, includeStack);
                    continue;
                }

                if (clean.StartsWith("[") && clean.EndsWith("]"))
                {
                    var name = clean[1..^1].Trim();
                    if (name.Equals("global", StringComparison.OrdinalIgnoreCase))
                    {
                        current = null;
                    }
                    else if (name.Equals("homes", StringComparison.OrdinalIgnoreCase))
                    {
                        current = null;
                    }
                    else
                    {
                        current = FindOrCreateShare(shares, name);
                    }

                    continue;
                }

                if (current == null)
                    continue;

                if (parts.Length != 2)
                {
                    current.UnknownParameters.Add(clean);
                    continue;
                }

                var key = parts[0].Trim().ToLowerInvariant();
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
                    case "follow symlinks":
                        current.FollowSymlinks = IsTrue(value);
                        break;
                    case "wide links":
                        current.WideLinks = IsTrue(value);
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
        }
        finally
        {
            includeStack.Remove(normalizedPath);
        }
    }

    private static Share FindOrCreateShare(List<Share> shares, string name)
    {
        foreach (var share in shares)
        {
            if (share.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return share;
        }

        var created = new Share
        {
            Name = name,
            ReadOnly = true,
            AllowGuests = false,
            Browseable = true,
            CreateMask = "0744",
            DirectoryMask = "0755",
            UnknownParameters = new List<string>()
        };
        shares.Add(created);
        return created;
    }

    private static string StripInlineComment(string line)
    {
        var inQuotes = false;
        var escaped = false;
        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '"' && !escaped)
                inQuotes = !inQuotes;
            else if (!inQuotes && (character == '#' || character == ';'))
                return line[..i].Trim();

            escaped = character == '\\' && !escaped;
            if (character != '\\')
                escaped = false;
        }

        return line.Trim();
    }
}