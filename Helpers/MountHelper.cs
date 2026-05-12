using System;
using System.IO;
using System.Threading.Tasks;
using SAMBA_Util.Helpers;

namespace SAMBA_Util.Helpers
{
    public static class MountHelper
    {
        public class MountResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public int ExitCode { get; set; }
            public string Stdout { get; set; } = "";
            public string Stderr { get; set; } = "";
        }

        // ---------------------------------------------------------
        // Construir opciones CIFS (guest o usuario/contraseña)
        // ---------------------------------------------------------
        private static async Task<string> BuildCifsOptionsAsync(bool isGuest, string? user, string? pass)
        {
            // uid/gid del usuario actual
            var uidRes = ShellHelper.Ejecutar("id -u");
            var gidRes = ShellHelper.Ejecutar("id -g");

            string uid = uidRes.Stdout.Trim();
            string gid = gidRes.Stdout.Trim();

            if (string.IsNullOrWhiteSpace(uid)) uid = "1000";
            if (string.IsNullOrWhiteSpace(gid)) gid = "1000";

            string common =
                $"uid={uid},gid={gid},rw,vers=3.0,file_mode=0777,dir_mode=0777";

            if (isGuest)
            {
                return $"-o guest,{common}";
            }

            user ??= "";
            pass ??= "";

            return $"-o username={user},password={pass},{common}";
        }

        // ---------------------------------------------------------
        // Montar CIFS (con fallback de versión)
        // ---------------------------------------------------------
        public static async Task<MountResult> MountAsync(
            string ip,
            string shareName,
            string mountPoint,
            bool isGuest,
            string? user,
            string? pass)
        {
            Directory.CreateDirectory(mountPoint);

            string options = await BuildCifsOptionsAsync(isGuest, user, pass);
            string baseCmd = $"mount.cifs //{ip}/{shareName} \"{mountPoint}\" {options}";

            // Primer intento
            var r1 = ShellHelper.EjecutarComoRoot(baseCmd);

            if (r1.ExitCode == 0 &&
                !r1.Stderr.Contains("mount error", StringComparison.OrdinalIgnoreCase) &&
                !r1.Stderr.Contains("nt_status", StringComparison.OrdinalIgnoreCase))
            {
                return new MountResult
                {
                    Success = true,
                    ExitCode = r1.ExitCode,
                    Stdout = r1.Stdout,
                    Stderr = r1.Stderr,
                    Message = "Mounted successfully (vers=3.0)."
                };
            }

            // Si falla por versión, probamos con vers=2.1
            if (r1.Stderr.Contains("Operation not supported", StringComparison.OrdinalIgnoreCase) ||
                r1.Stderr.Contains("mount error(95)", StringComparison.OrdinalIgnoreCase))
            {
                string optionsFallback = options.Replace("vers=3.0", "vers=2.1");
                string cmdFallback = $"mount.cifs //{ip}/{shareName} \"{mountPoint}\" {optionsFallback}";

                var r2 = ShellHelper.EjecutarComoRoot(cmdFallback);

                if (r2.ExitCode == 0 &&
                    !r2.Stderr.Contains("mount error", StringComparison.OrdinalIgnoreCase) &&
                    !r2.Stderr.Contains("nt_status", StringComparison.OrdinalIgnoreCase))
                {
                    return new MountResult
                    {
                        Success = true,
                        ExitCode = r2.ExitCode,
                        Stdout = r2.Stdout,
                        Stderr = r2.Stderr,
                        Message = "Mounted successfully (vers=2.1 fallback)."
                    };
                }

                return new MountResult
                {
                    Success = false,
                    ExitCode = r2.ExitCode,
                    Stdout = r2.Stdout,
                    Stderr = r2.Stderr,
                    Message = "Mount failed (vers=3.0 and vers=2.1)."
                };
            }

            return new MountResult
            {
                Success = false,
                ExitCode = r1.ExitCode,
                Stdout = r1.Stdout,
                Stderr = r1.Stderr,
                Message = "Mount failed."
            };
        }

        // ---------------------------------------------------------
        // Desmontar
        // ---------------------------------------------------------
        public static MountResult Unmount(string mountPoint)
        {
            var r = ShellHelper.EjecutarComoRoot($"umount \"{mountPoint}\"");

            return new MountResult
            {
                Success = r.ExitCode == 0,
                ExitCode = r.ExitCode,
                Stdout = r.Stdout,
                Stderr = r.Stderr,
                Message = r.ExitCode == 0 ? "Unmounted successfully." : "Unmount failed."
            };
        }

        // ---------------------------------------------------------
        // Comprobar si está montado
        // ---------------------------------------------------------
        public static bool IsMounted(string mountPoint)
        {
            var r = ShellHelper.Ejecutar($"mount | grep \" {mountPoint} \"");
            return !string.IsNullOrWhiteSpace(r.Stdout);
        }
    }
}
