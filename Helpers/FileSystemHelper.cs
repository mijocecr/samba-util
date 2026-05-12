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
            // Verificar existencia antes de ejecutar stat
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                Console.WriteLine($"[PERM] #{callId} NO EXISTE");
                return ("", "", "");
            }

            // Comando universal:
            // 1. GNU stat (Linux)
            // 2. BSD stat (FreeBSD/macOS)
            // 3. BusyBox stat (Alpine)
            string cmd =
                $"stat -c \"%U %G %a\" \"{path}\" 2>/dev/null || " +
                $"stat -f \"%Su %Sg %Op\" \"{path}\" 2>/dev/null";

            var result = ShellHelper.Ejecutar(cmd);
            var output = result.Stdout?.Trim() ?? "";

            sw.Stop();
            Console.WriteLine($"[PERM] #{callId} ← stat completado en {sw.ElapsedMilliseconds} ms");

            if (string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine($"[PERM] #{callId} ERROR: salida vacía");
                return ("?", "?", "?");
            }

            // SELinux, ACLs y atributos extendidos pueden añadir más campos
            var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3)
            {
                Console.WriteLine($"[PERM] #{callId} SALIDA INVALIDA: '{output}'");
                return ("?", "?", "?");
            }

            string owner = parts[0];
            string group = parts[1];
            string mode = parts[2];

            // Normalizar permisos (solo dígitos)
            mode = new string(mode.Where(char.IsDigit).ToArray());

            // Mantener solo los últimos 3 dígitos
            if (mode.Length > 3)
                mode = mode[^3..];

            Console.WriteLine($"[PERM] #{callId} OK → Owner={owner}, Group={group}, Mode={mode}");
            return (owner, group, mode);
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
