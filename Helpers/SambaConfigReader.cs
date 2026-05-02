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

            // Saltar líneas vacías o comentarios
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith(";"))
                continue;

            // Detectar sección [share]
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                var name = line.Trim('[', ']');

                // Ignorar [global]
                if (name.Equals("global", System.StringComparison.OrdinalIgnoreCase))
                {
                    current = null;
                    continue;
                }

                // Guardar el share anterior
                if (current != null)
                    shares.Add(current);

                // Crear nuevo share con defaults reales de Samba
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

            // Parseo clave = valor
            var parts = line.Split('=', 2);
            if (parts.Length != 2)
                continue;

            var key = parts[0].Trim().ToLower();
            var value = parts[1].Trim();

            switch (key)
            {
                case "path":
                    current.Path = value;
                    break;

                case "read only":
                case "readonly":
                    current.ReadOnly = value.Equals("yes", System.StringComparison.OrdinalIgnoreCase) ||
                                       value.Equals("true", System.StringComparison.OrdinalIgnoreCase);
                    break;

                case "guest ok":
                case "public": // alias
                    current.AllowGuests = value.Equals("yes", System.StringComparison.OrdinalIgnoreCase) ||
                                          value.Equals("true", System.StringComparison.OrdinalIgnoreCase);
                    break;

                case "browseable":
                case "browsable":
                    current.Browseable = value.Equals("yes", System.StringComparison.OrdinalIgnoreCase) ||
                                         value.Equals("true", System.StringComparison.OrdinalIgnoreCase);
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

        // Añadir el último share
        if (current != null)
            shares.Add(current);

        // ⭐ VALIDAR PERMISOS DEL SISTEMA DE ARCHIVOS
        foreach (var s in shares)
            s.Warning = ShareValidator.ValidateShare(s);

        return shares;
    }
}
