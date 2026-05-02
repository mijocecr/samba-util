using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAMBA_Util.Models;

namespace SAMBA_Util.Helpers;

public static class SambaConfigWriter
{
    private const string SmbConfPath = "/etc/samba/smb.conf";
    private const string TempPath = "/tmp/smb.conf";

    /// <summary>
    /// Guarda la lista completa de shares en smb.conf usando sudo.
    /// </summary>
    public static void SaveAllShares(IEnumerable<Share> shares)
    {
        // 1) Generar archivo temporal
        var lines = shares.Select(s => ShareToText(s)).ToList();
        File.WriteAllLines(TempPath, lines);

        // 2) Copiar a /etc/samba/smb.conf como root
        ShellHelper.EjecutarComoRoot($"cp \"{TempPath}\" \"{SmbConfPath}\"");

        // 3) Reiniciar Samba
        ShellHelper.EjecutarComoRoot("systemctl restart smbd");
    }

    /// <summary>
    /// Agrega un nuevo share.
    /// </summary>
    public static void AddShare(Share newShare)
    {
        var shares = SambaConfigReader.LoadShares();

        // Evitar duplicados
        if (shares.Any(s => s.Name.Equals(newShare.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A share named '{newShare.Name}' already exists.");

        shares.Add(newShare);

        SaveAllShares(shares);
    }

    /// <summary>
    /// Elimina un share por nombre.
    /// </summary>
    public static void DeleteShare(string name)
    {
        var shares = SambaConfigReader.LoadShares();
        var filtered = shares.Where(s => s.Name != name).ToList();
        SaveAllShares(filtered);
    }

    /// <summary>
    /// Actualiza un share existente.
    /// </summary>
    public static void UpdateShare(Share updated)
    {
        var shares = SambaConfigReader.LoadShares();

        var list = shares
            .Where(s => s.Name != updated.Name)
            .ToList();

        list.Add(updated);

        SaveAllShares(list);
    }

    /// <summary>
    /// Convierte un Share a texto smb.conf completo.
    /// </summary>
    private static string ShareToText(Share s)
    {
        // Construcción ordenada y limpia
        var lines = new List<string>
        {
            $"[{s.Name}]",
            $"   path = {s.Path}",
            $"   read only = {(s.ReadOnly ? "yes" : "no")}",
            $"   guest ok = {(s.AllowGuests ? "yes" : "no")}",
            $"   browseable = {(s.Browseable ? "yes" : "no")}"
        };

        // Campos opcionales (solo si tienen valor)
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

        // Línea en blanco final para separar shares
        lines.Add("");

        return string.Join(Environment.NewLine, lines);
    }
}
