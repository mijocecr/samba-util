using SAMBA_Util.Models;
using System.Collections.Generic;
using System.IO;

namespace SAMBA_Util.Helpers;

public static class SambaConfigReader
{
    public static List<Share> LoadShares(string filePath = "/etc/samba/smb.conf")
    {
        var shares = new List<Share>();

        if (!File.Exists(filePath))
            return shares;

        Share? current = null;

        foreach (var rawLine in File.ReadAllLines(filePath))
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith(";"))
                continue;

            // Detectar sección
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                var name = line.Trim('[', ']');

                if (name.Equals("global", System.StringComparison.OrdinalIgnoreCase))
                {
                    current = null;
                    continue;
                }

                if (current != null)
                    shares.Add(current);

                current = new Share { Name = name };
                continue;
            }

            if (current == null)
                continue;

            // PATH
            if (line.StartsWith("path", System.StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                    current.Path = parts[1].Trim();
            }

            // READ ONLY
            if (line.StartsWith("read only", System.StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                    current.ReadOnly = parts[1].Trim().Equals("yes", System.StringComparison.OrdinalIgnoreCase);
            }

            // GUEST OK
            if (line.StartsWith("guest ok", System.StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                    current.AllowGuests = parts[1].Trim().Equals("yes", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        if (current != null)
            shares.Add(current);

        return shares;
    }
}
