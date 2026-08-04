using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SAMBA_Util.Helpers
{
    public static class SmbHelper
    {
        public static void OpenShare(string ip, string shareName, string? user, string? pass, string domain = "WORKGROUP")
        {
            try
            {
                // Limpiar el nombre por si el escáner incluye comentarios ("Youtube-Showcase|Prueba")
                string cleanShareName = shareName.Split('|')[0].Trim();
                string name = cleanShareName.Normalize(NormalizationForm.FormC);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    OpenInWindows(ip, name, user, pass, domain);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    OpenInMac(ip, name, user, pass, domain);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    OpenInLinux(ip, name, user, pass, domain);
                }
                else
                {
                    NotifyError("Sistema operativo local no soportado.");
                }
            }
            catch (Exception ex)
            {
                NotifyError($"Error al abrir el recurso SMB: {ex.Message}");
            }
        }

        #region Implementaciones por Plataforma

        private static void OpenInWindows(string ip, string shareName, string? user, string? pass, string domain)
        {
            string uncPath = $@"\\{ip}\{shareName}";

            if (!string.IsNullOrWhiteSpace(user))
            {
                string passwordArg = string.IsNullOrEmpty(pass) ? "\"\"" : $"\"{pass}\"";
                string fullUser = string.IsNullOrEmpty(domain) ? user : $"{domain}\\{user}";

                var netUseInfo = new ProcessStartInfo
                {
                    FileName = "net",
                    Arguments = $"use \"{uncPath}\" {passwordArg} /user:\"{fullUser}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using var netProcess = Process.Start(netUseInfo);
                netProcess?.WaitForExit(3000);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = uncPath,
                UseShellExecute = true
            });
        }

        private static void OpenInMac(string ip, string shareName, string? user, string? pass, string domain)
        {
            string url = BuildSmbUrl(ip, shareName, user, pass, domain, includePassword: true);

            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"\"{url}\"",
                UseShellExecute = false
            });
        }

        private static void OpenInLinux(string ip, string shareName, string? user, string? pass, string domain)
        {
            string cleanUrl = BuildSmbUrl(ip, shareName, user, pass, domain, includePassword: false);
            string fullUrl = BuildSmbUrl(ip, shareName, user, pass, domain, includePassword: true);

            // 1. ESTRATEGIA A: Verificar si ya existe el punto de montaje local en /run/user/<UID>/gvfs/
            string localGvfsPath = GetGvfsLocalPath(ip, shareName);

            if (Directory.Exists(localGvfsPath))
            {
                if (TryExecuteProcess("gio", $"open \"{localGvfsPath}\"") ||
                    TryExecuteProcess("xdg-open", $"\"{localGvfsPath}\""))
                {
                    return;
                }
            }

            // 2. ESTRATEGIA B: Montar mediante gio mount
            MountGvfsInteractive(cleanUrl, pass, domain);

            // Pausa táctica para dar tiempo al demonio gvfsd-smb a registrar el volumen en D-Bus
            Thread.Sleep(600);

            // 3. Verificar nuevamente si la carpeta física ya se creó tras el montaje
            if (Directory.Exists(localGvfsPath))
            {
                if (TryExecuteProcess("gio", $"open \"{localGvfsPath}\"") ||
                    TryExecuteProcess("xdg-open", $"\"{localGvfsPath}\""))
                {
                    return;
                }
            }

            // 4. Intentar abrir con 'gio open' usando la URL SMB
            if (TryExecuteProcess("gio", $"open \"{cleanUrl}\"")) return;

            // 5. Fallback para KDE (Dolphin / KIO)
            if (TryExecuteProcess("kioclient", $"exec \"{fullUrl}\"") ||
                TryExecuteProcess("kioclient5", $"exec \"{fullUrl}\"")) return;

            // 6. Fallback universal con xdg-open
            if (TryExecuteProcess("xdg-open", $"\"{cleanUrl}\"")) return;

            NotifyError($"No se pudo abrir '{cleanUrl}'.");
        }

        #endregion

        #region Helpers para Linux

        private static bool MountGvfsInteractive(string url, string? pass, string domain)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "gio",
                    Arguments = $"mount \"{url}\"",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                using (StreamWriter writer = process.StandardInput)
                {
                    // Responder al prompt de Domain
                    writer.WriteLine(string.IsNullOrWhiteSpace(domain) ? "WORKGROUP" : domain);
                    writer.Flush();

                    // Responder al prompt de Password
                    if (!string.IsNullOrEmpty(pass))
                    {
                        writer.WriteLine(pass);
                        writer.Flush();
                    }
                }

                process.WaitForExit(4000);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string GetGvfsLocalPath(string ip, string shareName)
        {
            int uid = GetCurrentUnixUid();
            
            // Buscar carpetas que coincidan con la IP y el recurso en /run/user/UID/gvfs/
            string baseGvfsDir = $"/run/user/{uid}/gvfs";
            
            if (Directory.Exists(baseGvfsDir))
            {
                string targetShareLower = shareName.ToLowerInvariant();
                foreach (var dir in Directory.GetDirectories(baseGvfsDir))
                {
                    string dirLower = dir.ToLowerInvariant();
                    if (dirLower.Contains($"server={ip.ToLowerInvariant()}") && dirLower.Contains($"share={targetShareLower}"))
                    {
                        return dir;
                    }
                }
            }

            // Ruta por defecto estandarizada de GVfs
            return $"/run/user/{uid}/gvfs/smb-share:server={ip},share={shareName.ToLowerInvariant()}";
        }

        private static int GetCurrentUnixUid()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "id",
                    Arguments = "-u",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    if (int.TryParse(output, out int uid)) return uid;
                }
            }
            catch { }

            return 1000;
        }

        private static string BuildSmbUrl(string ip, string shareName, string? user, string? pass, string domain, bool includePassword)
        {
            if (string.IsNullOrWhiteSpace(user))
            {
                return $"smb://{ip}/{shareName}";
            }

            string userWithDomain = string.IsNullOrWhiteSpace(domain) ? user : $"{domain};{user}";

            if (string.IsNullOrEmpty(pass) || !includePassword)
            {
                return $"smb://{userWithDomain}@{ip}/{shareName}";
            }

            return $"smb://{userWithDomain}:{pass}@{ip}/{shareName}";
        }

        private static bool TryExecuteProcess(string command, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                return process != null;
            }
            catch
            {
                return false;
            }
        }

        private static void NotifyError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[SmbHelper ERROR] {message}");
            Console.ResetColor();
        }

        #endregion
    }
}