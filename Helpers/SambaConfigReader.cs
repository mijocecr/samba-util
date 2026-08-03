using SAMBA_Util.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SAMBA_Util.Helpers;

public static class SambaConfigReader
{
    private static long _callCount = 0;

    private static bool IsTrue(string v) =>
        v.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        v.Equals("1") ||
        v.Equals("y", StringComparison.OrdinalIgnoreCase);

    private static void ValidateShareName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Share name cannot be empty.");

        // Samba permite letras, números, punto, guion, guion bajo y dólar
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9._\-$]+$"))
            throw new InvalidOperationException(
                $"Invalid share name '{name}'. Allowed characters: letters, numbers, dot, underscore, dash, dollar."
            );
    }



    public static List<Share> LoadShares(string filePath = null)
    {
        var sw = Stopwatch.StartNew();
        var callId = ++_callCount;

        var config = ConfigManager.Load();
        filePath ??= config.SmbConfPath;

        Console.WriteLine($"[CONF] #{callId} → LoadShares('{filePath}') started");

        var shares = new List<Share>();

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[CONF] #{callId} File does not exist. Time: {sw.ElapsedMilliseconds} ms");
            return shares;
        }

        Share? current = null;

        using var reader = new StreamReader(filePath);

        string? rawLine;
        while ((rawLine = reader.ReadLine()) != null)
        {
            rawLine = rawLine.Replace("\t", " ");

            // Preserve full-line comments inside sections
            if (rawLine.Trim().StartsWith("#") || rawLine.Trim().StartsWith(";"))
            {
                if (current != null)
                    current.UnknownParameters.Add(rawLine.Trim());
                continue;
            }

            // Remove trailing comments
            var clean = rawLine.Split('#')[0].Split(';')[0].Trim();

            if (string.IsNullOrWhiteSpace(clean))
                continue;

            // Includes
            if (clean.StartsWith("include =", StringComparison.OrdinalIgnoreCase))
            {
                var includePath = clean.Split('=', 2)[1].Trim().Trim('"');
                if (File.Exists(includePath))
                {
                    Console.WriteLine($"[CONF] #{callId} → Processing include: {includePath}");
                    shares.AddRange(LoadShares(includePath));
                }
                continue;
            }

            // Multiline continuation
            while (clean.EndsWith("\\"))
            {
                clean = clean.TrimEnd('\\').Trim();
                var next = reader.ReadLine()?.Trim() ?? "";
                clean += " " + next;
            }

            // Section detection
            if (clean.StartsWith("[") && clean.EndsWith("]"))
            {
                var name = clean.Substring(1, clean.Length - 2);

                if (name.Equals("global", StringComparison.OrdinalIgnoreCase))
                {
                    current = null;
                    continue;
                }

                ValidateShareName(name);

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

            // Key/value parsing
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
        Console.WriteLine($"[CONF] #{callId} ← LoadShares completed in {sw.ElapsedMilliseconds} ms. Shares: {shares.Count}");

        return shares;
    }
}
