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

    public static List<Share> LoadShares(string? filePath = null)
    {
        var sw = Stopwatch.StartNew();
        var callId = ++_callCount;

        var config = ConfigManager.Load();
        filePath ??= config.SmbConfPath;

        Console.WriteLine($"[CONF] #{callId} → LoadShares('{filePath}') iniciado");

        var shares = new List<Share>();

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            Console.WriteLine($"[CONF] #{callId} Archivo no existe o ruta inválida. Tiempo: {sw.ElapsedMilliseconds} ms");
            return shares;
        }

        Share? current = null;

        using var reader = new StreamReader(filePath);

        string? rawLine;
        while ((rawLine = reader.ReadLine()) != null)
        {
            // Normalizar tabulaciones
            var clean = rawLine.Replace("\t", " ").Trim();

            // Ignorar líneas vacías o comentarios puros (que empiezan por # o ;)
            if (string.IsNullOrWhiteSpace(clean) || clean.StartsWith("#") || clean.StartsWith(";"))
                continue;

            // Quitar comentarios al final de la línea si no son parte de comillas
            clean = StripInlineComment(clean);
            if (string.IsNullOrWhiteSpace(clean))
                continue;

            // Multilínea: procesar la barra invertida al final
            while (clean.EndsWith("\\"))
            {
                clean = clean.TrimEnd('\\').Trim();
                var nextLine = reader.ReadLine()?.Replace("\t", " ").Trim();
                if (!string.IsNullOrEmpty(nextLine))
                {
                    clean += " " + StripInlineComment(nextLine);
                }
            }

            // Manejo de la directiva 'include ='
            if (clean.StartsWith("include =", StringComparison.OrdinalIgnoreCase))
            {
                var includePath = clean.Split('=', 2)[1].Trim().Trim('"');

                if (File.Exists(includePath))
                {
                    Console.WriteLine($"[CONF] #{callId} → Procesando include: {includePath}");

                    // Guardar el share actual si teníamos uno en progreso antes del include
                    if (current != null && !shares.Contains(current))
                    {
                        shares.Add(current);
                        current = null;
                    }

                    var includedShares = LoadShares(includePath);

                    foreach (var s in includedShares)
                    {
                        if (!shares.Any(x => x.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            shares.Add(s);
                        }
                    }
                }
                continue;
            }

            // Detección de nueva sección [NombreSeccion]
            if (clean.StartsWith("[") && clean.EndsWith("]"))
            {
                var name = clean.Substring(1, clean.Length - 2).Trim();

                // Guardar la sección anterior si existía
                if (current != null)
                {
                    shares.Add(current);
                }

                // Si es la sección global, la ignoramos para la lista de recursos compartidos
                if (name.Equals("global", StringComparison.OrdinalIgnoreCase))
                {
                    current = null;
                    continue;
                }

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

            // Si estamos fuera de una sección válida (ej: antes de la primera sección o en [global]), ignorar
            if (current == null)
                continue;

            // Parsear Clave = Valor
            var parts = clean.Split('=', 2, StringSplitOptions.TrimEntries);
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

        // Añadir el último recurso procesado al finalizar el archivo
        if (current != null && !shares.Contains(current))
        {
            shares.Add(current);
        }

        sw.Stop();
        Console.WriteLine($"[CONF] #{callId} ← LoadShares completado en {sw.ElapsedMilliseconds} ms. Shares: {shares.Count}");

        return shares;
    }

    /// <summary>
    /// Elimina comentarios al final de la línea (# o ;) respetando que no estén dentro de comillas.
    /// </summary>
    private static string StripInlineComment(string line)
    {
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && (c == '#' || c == ';'))
            {
                return line.Substring(0, i).Trim();
            }
        }

        return line.Trim();
    }
}