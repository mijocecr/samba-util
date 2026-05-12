using SAMBA_Util.Models;
using System.IO;
using System.Linq;

namespace SAMBA_Util.Helpers;

public static class ShareValidator
{
    // Normaliza permisos tipo "0755" → "755"
    private static string Normalize(string p)
    {
        p = new string(p.Where(char.IsDigit).ToArray());
        if (p.Length >= 3)
            return p[^3..];
        return p.PadLeft(3, '0');
    }

    // Determina el usuario efectivo que Samba usará
    private static string GetEffectiveUser(Share s)
    {
        // 1) Si hay force user → Samba siempre usa ese usuario
        if (!string.IsNullOrWhiteSpace(s.ForceUser))
            return s.ForceUser;

        // 2) Si el share permite invitados → Samba usa nobody
        if (s.AllowGuests)
            return "nobody";

        // 3) Si requiere autenticación → usa el usuario SMB introducido
        // (si en el futuro guardas el usuario SMB, cámbialo aquí)
        return "smbuser";
    }

    public static string? ValidateShare(Share s)
    {
        if (string.IsNullOrWhiteSpace(s.Path))
            return "The share has no assigned path.";

        if (!Directory.Exists(s.Path))
            return "The path exists but is not a directory.";

        // Obtener permisos reales del FS
        var (owner, group, mode) = FileSystemHelper.GetPermissions(s.Path);

        if (owner == "" && group == "" && mode == "")
            return "Directory does not exist.";

        if (mode.Contains('?'))
            return "File system permissions could not be read.";

        if (string.IsNullOrWhiteSpace(mode) || mode.Length < 3)
            return "File system permissions could not be read.";

        mode = Normalize(mode);

        // Bits de escritura
        bool ownerWrite = mode[0] == '7' || mode[0] == '6' || mode[0] == '2';
        bool groupWrite = mode[1] == '7' || mode[1] == '6' || mode[1] == '2';
        bool otherWrite = mode[2] == '7' || mode[2] == '6' || mode[2] == '2';

        // Determinar usuario efectivo de Samba
        string effectiveUser = GetEffectiveUser(s);

        // Determinar si ese usuario puede escribir
        bool canWrite = false;

        if (effectiveUser == owner)
            canWrite = ownerWrite;
        else if (effectiveUser == group)
            canWrite = groupWrite;
        else
            canWrite = otherWrite;

        // Si Samba permite escribir pero el FS no → error real
        if (!s.ReadOnly && !canWrite)
        {
            return $"User '{effectiveUser}' cannot write to this folder. " +
                   $"Filesystem permissions: owner={owner}, group={group}, mode={mode}.";
        }

        // Si es guest y no hay escritura para others → error real
        if (s.AllowGuests && effectiveUser == "nobody" && !otherWrite)
        {
            return "Guests allowed, but filesystem blocks write for others.";
        }

        // Si hay force user pero no coincide con el owner → advertencia real
        if (!string.IsNullOrWhiteSpace(s.ForceUser) && s.ForceUser != owner)
        {
            return $"Samba forces user '{s.ForceUser}', but the directory belongs to '{owner}'.";
        }

        // NO COMPARAR PERMISOS EXACTOS CON ConfigWindow (esto causaba falsos positivos)
        // Eliminado a propósito.

        return null; // Share válido
    }
}
