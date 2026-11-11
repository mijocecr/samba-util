using System;
using System.Collections.Generic;
using System.Linq;

namespace SAMBA_Util.Helpers;

// Utilidades compartidas para interpretar la salida de pdbedit.
public static class SambaUsers
{
    // "pdbedit -L" devuelve una línea por usuario con formato:
    //   usuario:uid:nombre completo
    // Se queda solo con el nombre, sin duplicados y ordenado.
    public static List<string> ParseList(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return new List<string>();

        return stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2)[0].Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
