using System;
using System.IO;

namespace SAMBA_Util.Helpers;

/// <summary>
/// Salida de diagnóstico de helpers y vistas.
/// Históricamente estos mensajes se escribían en Console y aparecían en medio
/// de los menús de la CLI. Ahora van a un archivo de log con rotación simple;
/// la CLI solo muestra su propia salida (menús y resultados).
/// </summary>
public static class DebugConsole
{
    private static readonly object Lock = new();

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config",
        "samba-util",
        "samba-util.log");

    public static void WriteLine(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            var path = LogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024)
            {
                var backup = path + ".1";
                if (File.Exists(backup))
                    File.Delete(backup);
                File.Move(path, backup);
            }

            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message.TrimEnd()}{Environment.NewLine}";
            lock (Lock)
            {
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // El diagnóstico nunca debe romper la aplicación ni la CLI.
        }
    }

    public static void WriteLine(object? value) => WriteLine(value?.ToString());
}
