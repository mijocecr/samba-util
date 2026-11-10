using System;
using System.Collections.Generic;

namespace SAMBA_Util.Helpers;

// Secciones de smb.conf que SAMBA-Util no gestiona: se conservan verbatim al
// guardar y no se muestran como shares editables.
public static class SambaSections
{
    private static readonly HashSet<string> Special = new(StringComparer.OrdinalIgnoreCase)
    {
        "homes",
        "printers",
        "print$",
        "IPC$"
    };

    public static bool IsSpecial(string name) => Special.Contains(name);
}
