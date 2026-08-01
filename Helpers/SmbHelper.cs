using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace SAMBA_Util.Helpers
{
    public static class SmbHelper
    {
        /// <summary>
        /// Abre un recurso SMB en el explorador de archivos adecuado según la distro.
        /// Envía notificación de error si falla.
        /// </summary>
        public static void OpenShare(string ip, string shareName, string? user, string? pass)
        {
            try
            {
                // 1. Normalizar NFD → NFC (macOS usa NFD)
                string name = shareName.Normalize(NormalizationForm.FormC);

                // 2. Construir URL EXACTA sin escapar nada
                string url = !string.IsNullOrWhiteSpace(user)
                    ? $"smb://{user}:{pass}@{ip}/{name}"
                    : $"smb://{ip}/{name}";

                // 3. Detectar entorno y herramientas disponibles
                bool hasKio = File.Exists("/usr/bin/kioclient5");
                bool hasGio = File.Exists("/usr/bin/gio");
                bool hasXdg = File.Exists("/usr/bin/xdg-open");

                // 4. Detectar exploradores instalados
                bool hasDolphin = File.Exists("/usr/bin/dolphin");
                bool hasNautilus = File.Exists("/usr/bin/nautilus");
                bool hasNemo = File.Exists("/usr/bin/nemo");
                bool hasThunar = File.Exists("/usr/bin/thunar");
                bool hasPcmanfm = File.Exists("/usr/bin/pcmanfm");

                ProcessStartInfo psi;

                // 5. Prioridad absoluta: Dolphin (KDE)
                if (hasDolphin)
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "dolphin",
                        Arguments = $"\"{url}\"",
                        UseShellExecute = false
                    };
                }
                // 6. GNOME / Nautilus
                else if (hasNautilus)
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "nautilus",
                        Arguments = $"\"{url}\"",
                        UseShellExecute = false
                    };
                }
                // 7. Cinnamon / Nemo
                else if (hasNemo)
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "nemo",
                        Arguments = $"\"{url}\"",
                        UseShellExecute = false
                    };
                }
                // 8. XFCE / Thunar
                else if (hasThunar)
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "thunar",
                        Arguments = $"\"{url}\"",
                        UseShellExecute = false
                    };
                }
                // 9. LXDE / PCManFM
                else if (hasPcmanfm)
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "pcmanfm",
                        Arguments = $"\"{url}\"",
                        UseShellExecute = false
                    };
                }
                // 10. KDE sin Dolphin → usar KIO directamente
                else if (hasKio)
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "kioclient5",
                        Arguments = $"exec \"{url}\"",
                        UseShellExecute = false
                    };
                }
                // 11. GNOME sin Nautilus → usar GIO
                else if (hasGio)
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "gio",
                        Arguments = $"open \"{url}/\"",
                        UseShellExecute = false
                    };
                }
                // 12. Fallback universal → xdg-open
                else if (hasXdg)
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = $"\"{url}\"",
                        UseShellExecute = true
                    };
                }
                else
                {
                    NotifyError($"No file manager available to open SMB URL: {url}");
                    return;
                }

                // 13. Ejecutar
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                NotifyError($"Failed to open SMB share: {ex.Message}");
            }
        }

        /// <summary>
        /// Envía notificación de error (CLI + callback opcional).
        /// </summary>
        private static void NotifyError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[SmbHelper ERROR] {message}");
            Console.ResetColor();

            // Si tu GUI tiene un sistema de notificaciones:
            // MainWindow.Instance?.ShowErrorDialog("SMB Error", message);
            // MainWindow.Instance?.ShowToast(message);
        }
    }
}
