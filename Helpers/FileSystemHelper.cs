using System;
using System.Linq;

namespace SAMBA_Util.Helpers;

public static class FileSystemHelper
{
    public static (string Owner, string Group, string Mode) GetPermissions(string path)
    {
        try
        {
            var result = ShellHelper.EjecutarComoRoot($"stat -c \"%U %G %a\" \"{path}\"");

            // ⭐ USAR result.Stdout (NO result)
            var output = result.Stdout?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(output) ||
                output.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("cannot stat", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                return ("?", "?", "?");
            }

            var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 3)
                return (parts[0], parts[1], parts[2]);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error leyendo permisos: {ex.Message}");
        }

        return ("?", "?", "?");
    }
}