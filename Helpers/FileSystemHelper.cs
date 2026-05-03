using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SAMBA_Util.Helpers;

public static class FileSystemHelper
{
    private static long _callCount = 0;

    public static (string Owner, string Group, string Mode) GetPermissions(string path)
    {
        var sw = Stopwatch.StartNew();
        var callId = ++_callCount;

        Console.WriteLine($"[PERM] #{callId} → GetPermissions('{path}') iniciado");

        try
        {
            // Detectar inexistencia ANTES de llamar a stat
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                Console.WriteLine($"[PERM] #{callId} NO EXISTE");
                return ("", "", ""); // ← diferencia clara respecto a error
            }

            var result = ShellHelper.EjecutarComoRoot($"stat -c \"%U %G %a\" \"{path}\"");

            var output = result.Stdout?.Trim() ?? "";

            sw.Stop();
            Console.WriteLine($"[PERM] #{callId} ← stat completado en {sw.ElapsedMilliseconds} ms");

            if (string.IsNullOrWhiteSpace(output) ||
                output.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("cannot stat", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[PERM] #{callId} ERROR o salida vacía");
                return ("?", "?", "?");
            }

            var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 3)
            {
                string owner = parts[0];
                string group = parts[1];
                string mode = parts[2];

                // Normalizar modo a 3 dígitos
                mode = new string(mode.Where(char.IsDigit).ToArray());

                if (mode.Length > 3)
                    mode = mode[^3..]; // últimos 3 dígitos

                Console.WriteLine($"[PERM] #{callId} OK → Owner={owner}, Group={group}, Mode={mode}");
                return (owner, group, mode);
            }
            else
            {
                Console.WriteLine($"[PERM] #{callId} SALIDA INVALIDA: '{output}'");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PERM] #{callId} EXCEPCIÓN: {ex.Message}");
        }

        sw.Stop();
        Console.WriteLine($"[PERM] #{callId} FALLBACK en {sw.ElapsedMilliseconds} ms");

        return ("?", "?", "?");
    }
}
