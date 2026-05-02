using System;
using System.Diagnostics;
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
                Console.WriteLine($"[PERM] #{callId} OK → Owner={parts[0]}, Group={parts[1]}, Mode={parts[2]}");
                return (parts[0], parts[1], parts[2]);
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