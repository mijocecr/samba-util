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

            // 1. Forzar el montaje interactivo / automático primero para asegurar que GVfs registre el volumen
            MountGvfsInteractive(cleanUrl, pass, domain);

            // Breve pausa para dar tiempo a que el demonio cree el punto de montaje físico
            Thread.Sleep(800);

            // 2. Buscar dinámicamente la ruta real montada en gvfs
            string? localGvfsPath = FindActiveGvfsMount(ip, shareName);

            if (!string.IsNullOrEmpty(localGvfsPath) && Directory.Exists(localGvfsPath))
            {
                if (TryExecuteProcess("gio", $"open \"{localGvfsPath}\"") ||
                    TryExecuteProcess("xdg-open", $"\"{localGvfsPath}\""))
                {
                    return;
                }
            }

            // 3. Fallback usando la URL SMB directamente con gio u otros clientes
            if (TryExecuteProcess("gio", $"open \"{cleanUrl}\"")) return;

            // Fallback para KDE (Dolphin / KIO)
            if (TryExecuteProcess("kioclient6", $"exec \"{fullUrl}\"") ||
                TryExecuteProcess("kioclient5", $"exec \"{fullUrl}\"") ||
                TryExecuteProcess("kioclient", $"exec \"{fullUrl}\"")) return;

            // Fallback universal con xdg-open
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

        private static string? FindActiveGvfsMount(string ip, string shareName)
        {
            try
            {
                int uid = GetCurrentUnixUid();
                string baseGvfsDir = $"/run/user/{uid}/gvfs";

                if (!Directory.Exists(baseGvfsDir))
                    return null;

                string targetIp = ip.ToLowerInvariant();
                string targetShare = shareName.ToLowerInvariant();

                foreach (var dir in Directory.GetDirectories(baseGvfsDir))
                {
                    string dirName = Path.GetFileName(dir).ToLowerInvariant();
                    
                    // Comprobar que pertenece al protocolo smb y coincide la IP y el recurso compartido
                    if (dirName.StartsWith("smb-share:") && dirName.Contains(targetIp) && dirName.Contains(targetShare))
                    {
                        return dir;
                    }
                }
            }
            catch { }

            return null;
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
                if (process == null) return false;

                process.WaitForExit(3000);
                return process.ExitCode == 0;
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