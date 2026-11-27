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

        DebugConsole.WriteLine($"[PERM] #{callId} -> GetPermissions('{path}') started");

        try
        {
            // Verificar existencia antes de ejecutar stat
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                DebugConsole.WriteLine($"[PERM] #{callId} DOES NOT EXIST");
                return ("", "", "");
            }

            // Comando universal:
            // 1. GNU stat (Linux)
            // 2. BSD stat (FreeBSD/macOS)
            // 3. BusyBox stat (Alpine)
            // La ruta se cita como argumento literal para evitar inyección.
            string quotedPath = ShellHelper.Quote(path);
            string cmd =
                $"stat -c '%U %G %a' {quotedPath} 2>/dev/null || " +
                $"stat -f '%Su %Sg %Op' {quotedPath} 2>/dev/null";

            var result = ShellHelper.Ejecutar(cmd);
            var output = result.Stdout?.Trim() ?? "";

            sw.Stop();
            DebugConsole.WriteLine($"[PERM] #{callId} <- stat completed in {sw.ElapsedMilliseconds} ms");

            if (string.IsNullOrWhiteSpace(output))
            {
                DebugConsole.WriteLine($"[PERM] #{callId} ERROR: empty output");
                return ("?", "?", "?");
            }

            // SELinux, ACLs y atributos extendidos pueden añadir más campos
            var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3)
            {
                DebugConsole.WriteLine($"[PERM] #{callId} INVALID OUTPUT: '{output}'");
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

            DebugConsole.WriteLine($"[PERM] #{callId} OK → Owner={owner}, Group={group}, Mode={mode}");
            return (owner, group, mode);
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[PERM] #{callId} EXCEPTION: {ex.Message}");
        }

        sw.Stop();
        DebugConsole.WriteLine($"[PERM] #{callId} FALLBACK in {sw.ElapsedMilliseconds} ms");

        return ("?", "?", "?");
    }
}
