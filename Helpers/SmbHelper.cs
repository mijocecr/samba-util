/*using System;
using System.Collections.Generic;
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
                NotifyError($"Error opening the SMB share: {ex.Message}");
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

                if (!process.WaitForExit(4000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return false;
                }

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

                foreach (var dir in Directory.GetDirectories(baseGvfsDir))
                {
                    string dirName = Path.GetFileName(dir);

                    // El directorio tiene el formato
                    // "smb-share:server=<ip>,share=<recurso>". Comparamos los
                    // campos exactos: Contains daría falsos positivos
                    // (10.0.0.1 casaría con 10.0.0.10).
                    if (!dirName.StartsWith("smb-share:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fields = ParseGvfsFields(dirName);
                    if (!fields.TryGetValue("server", out var server) ||
                        !fields.TryGetValue("share", out var share))
                        continue;

                    if (server.Equals(ip, StringComparison.OrdinalIgnoreCase) &&
                        share.Equals(shareName, StringComparison.OrdinalIgnoreCase))
                    {
                        return dir;
                    }
                }
            }
            catch { }

            return null;
        }

        // Descompone "smb-share:server=10.0.0.1,share=docs" en pares clave/valor,
        // decodificando los valores URL-encoded que genera GVfs.
        private static Dictionary<string, string> ParseGvfsFields(string dirName)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int colon = dirName.IndexOf(':');
            if (colon < 0)
                return fields;

            string rest = dirName[(colon + 1)..];
            foreach (var pair in rest.Split(','))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = pair[..eq].Trim();
                string value = Uri.UnescapeDataString(pair[(eq + 1)..].Trim());
                fields[key] = value;
            }

            return fields;
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

                if (!process.WaitForExit(3000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return false;
                }

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
}*/

using System;
using System.Collections.Generic;
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
                    throw new PlatformNotSupportedException("Local operating system not supported.");
                }
            }
            catch (Exception ex)
            {
                NotifyError($"Error opening the SMB share: {ex.Message}");
                throw; // Relanzamos para que NetworkView pueda mostrar el ShowErrorDialog
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

            // 1. Intentar montar mediante GVfs (útil en GNOME/XFCE/Cinnamon)
            MountGvfsInteractive(cleanUrl, user, pass, domain);

            // Dar un margen breve para que gvfsd-fuse cree el directorio en /run/user/<uid>/gvfs
            string? localGvfsPath = null;
            for (int i = 0; i < 6; i++)
            {
                localGvfsPath = FindActiveGvfsMount(ip, shareName);
                if (!string.IsNullOrEmpty(localGvfsPath) && Directory.Exists(localGvfsPath))
                    break;

                Thread.Sleep(250);
            }

            // 2. Si GVfs montó la carpeta física, abrirla directamente en el gestor de archivos (desacoplado)
            if (!string.IsNullOrEmpty(localGvfsPath) && Directory.Exists(localGvfsPath))
            {
                if (LaunchDetached("gio", $"open \"{localGvfsPath}\"") ||
                    LaunchDetached("xdg-open", $"\"{localGvfsPath}\"") ||
                    LaunchDetached("nautilus", $"\"{localGvfsPath}\""))
                {
                    return;
                }
            }

            // 3. Fallback KDE (Dolphin / KIO)
            if (LaunchDetached("kioclient6", $"exec \"{fullUrl}\"") ||
                LaunchDetached("kioclient5", $"exec \"{fullUrl}\"") ||
                LaunchDetached("kioclient", $"exec \"{fullUrl}\""))
            {
                return;
            }

            // 4. Fallback URI SMB directa en GNOME / Otros escritorios
            if (LaunchDetached("gio", $"open \"{cleanUrl}\"") ||
                LaunchDetached("xdg-open", $"\"{cleanUrl}\""))
            {
                return;
            }

            throw new Exception($"Could not open '{cleanUrl}' in the file manager.");
        }

        #endregion

        #region Helpers para Linux

        private static bool MountGvfsInteractive(string url, string? user, string? pass, string domain)
        {
            try
            {
                bool isAnonymous = string.IsNullOrWhiteSpace(user) ||
                                   user.Equals("guest", StringComparison.OrdinalIgnoreCase);

                // Si es anónimo/guest, usamos -a (--anonymous) para montar directo sin prompts interactivos
                string args = isAnonymous ? $"mount -a \"{url}\"" : $"mount \"{url}\"";

                var psi = CreateCleanProcessStartInfo("gio", args);
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                if (!isAnonymous)
                {
                    psi.RedirectStandardInput = true;
                }

                using var process = Process.Start(psi);
                if (process == null) return false;

                if (!isAnonymous)
                {
                    using StreamWriter writer = process.StandardInput;
                    if (!string.IsNullOrEmpty(pass))
                    {
                        writer.WriteLine(pass);
                    }
                    writer.Flush();
                }

                if (!process.WaitForExit(4000))
                {
                    try { process.Kill(); } catch { }
                    return false;
                }

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// Lanza procesos gráficos (Nautilus, Dolphin, gio open, xdg-open) sin matarlos por timeout
        /// y limpiando las variables de entorno del AppImage para evitar conflictos con GTK/GLib.
        /// </summary>
        private static bool LaunchDetached(string command, string arguments)
        {
            try
            {
                var psi = CreateCleanProcessStartInfo(command, arguments);
                using var process = Process.Start(psi);
                if (process == null) return false;

                // Esperamos solo 400ms para detectar si el binario NO existe o falló al arrancar de inmediato.
                // Si sigue vivo (ej. Nautilus abriendo ventana), NO lo matamos: lo dejamos correr libre.
                if (process.WaitForExit(400))
                {
                    return process.ExitCode == 0;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static ProcessStartInfo CreateCleanProcessStartInfo(string command, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Evita que gio / nautilus (GTK4/GLib) choquen con las librerías empaquetadas en el AppImage
            psi.Environment.Remove("LD_LIBRARY_PATH");
            psi.Environment.Remove("APPDIR");
            psi.Environment.Remove("APPIMAGE");
            psi.Environment.Remove("XDG_DATA_DIRS");

            return psi;
        }

        private static string? FindActiveGvfsMount(string ip, string shareName)
        {
            try
            {
                int uid = GetCurrentUnixUid();
                string baseGvfsDir = $"/run/user/{uid}/gvfs";

                if (!Directory.Exists(baseGvfsDir))
                    return null;

                foreach (var dir in Directory.GetDirectories(baseGvfsDir))
                {
                    string dirName = Path.GetFileName(dir);

                    // El directorio tiene el formato
                    // "smb-share:server=<ip>,share=<recurso>". Comparamos los
                    // campos exactos: Contains daría falsos positivos
                    // (10.0.0.1 casaría con 10.0.0.10).
                    if (!dirName.StartsWith("smb-share:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fields = ParseGvfsFields(dirName);
                    if (!fields.TryGetValue("server", out var server) ||
                        !fields.TryGetValue("share", out var share))
                        continue;

                    if (server.Equals(ip, StringComparison.OrdinalIgnoreCase) &&
                        share.Equals(shareName, StringComparison.OrdinalIgnoreCase))
                    {
                        return dir;
                    }
                }
            }
            catch { }

            return null;
        }

        // Descompone "smb-share:server=10.0.0.1,share=docs" en pares clave/valor,
        // decodificando los valores URL-encoded que genera GVfs.
        private static Dictionary<string, string> ParseGvfsFields(string dirName)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int colon = dirName.IndexOf(':');
            if (colon < 0)
                return fields;

            string rest = dirName[(colon + 1)..];
            foreach (var pair in rest.Split(','))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = pair[..eq].Trim();
                string value = Uri.UnescapeDataString(pair[(eq + 1)..].Trim());
                fields[key] = value;
            }

            return fields;
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

        private static void NotifyError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[SmbHelper ERROR] {message}");
            Console.ResetColor();
        }

        #endregion
    }
}