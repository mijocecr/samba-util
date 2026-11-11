using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SAMBA_Util.Helpers
{
    public static class OsDetector
    {
        public static async Task<string> DetectOsAsync(string ip)
        {
            // 1) SMB (Windows / macOS / Linux / NAS)
            if (await IsPortOpen(ip, 445))
            {
                string smb = await DetectFromSmbAsync(ip);
                if (smb != null)
                    return smb;
            }

            // 2) SSH (Linux / BSD / macOS / Unix)
            if (await IsPortOpen(ip, 22))
            {
                string ssh = await DetectFromSshAsync(ip);
                if (ssh != null)
                    return ssh;
            }

            // 3) TTL fallback
            string ttl = await DetectFromTtlAsync(ip);
            if (ttl != null)
                return ttl;

            return "Other";
        }

        private static async Task<string?> DetectFromSmbAsync(string ip)
        {
            string user = CredStore.User ?? "guest";
            string pass = CredStore.Password ?? "";

            var result = ShellHelper.Ejecutar(
                $"smbclient -L //{ip} -U {user}%{pass} {ConfigManager.ClientMinProtocolOption()} 2>/dev/null",
                ShellHelper.NetworkTimeoutMs
            );

            string output = (result.Stdout + result.Stderr).ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(output))
                return null;

            if (output.Contains("microsoft") || output.Contains("windows"))
                return "Windows";

            if (output.Contains("mac os") || output.Contains("darwin") || output.Contains("smbx") || output.Contains("aapl"))
                return "macOS";

            if (output.Contains("synology") || output.Contains("qnap") || output.Contains("freenas") || output.Contains("truenas") || output.Contains("wd"))
                return "NAS";

            if (output.Contains("samba") || output.Contains("unix"))
                return "Linux";

            return null;
        }

        private static async Task<string?> DetectFromSshAsync(string ip)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, 22);

                if (await Task.WhenAny(connectTask, Task.Delay(250)) != connectTask)
                    return null;

                using var stream = client.GetStream();
                byte[] buffer = new byte[200];
                int read = await stream.ReadAsync(buffer, 0, buffer.Length);

                if (read <= 0)
                    return null;

                string banner = Encoding.ASCII.GetString(buffer, 0, read).ToLowerInvariant();

                if (banner.Contains("windows"))
                    return "Windows";

                if (banner.Contains("darwin"))
                    return "macOS";

                if (banner.Contains("freebsd") || banner.Contains("openbsd") || banner.Contains("netbsd"))
                    return "BSD";

                // Si menciona explícitamente una distro o es un OpenSSH estándar (que en redes locales suele ser Linux)
                if (banner.Contains("ubuntu") || banner.Contains("debian") || banner.Contains("fedora") || 
                    banner.Contains("arch") || banner.Contains("manjaro") || banner.Contains("opensuse") || 
                    banner.Contains("linux") || banner.Contains("openssh"))
                {
                    return "Linux";
                }

                return "Unix";
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string?> DetectFromTtlAsync(string ip)
        {
            try
            {
                var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 200);

                if (reply.Status != IPStatus.Success)
                    return null;

                int ttl = reply.Options.Ttl;

                if (ttl >= 120 && ttl <= 135)
                    return "Windows";

                // TTL ~64 es el estándar absoluto para Linux y Unix en redes locales
                if (ttl >= 60 && ttl <= 70)
                    return "Linux"; 

                if (ttl >= 200)
                    return "Router";

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<bool> IsPortOpen(string ip, int port)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);

                if (await Task.WhenAny(connectTask, Task.Delay(150)) != connectTask)
                    return false;

                return client.Connected;
            }
            catch
            {
                return false;
            }
        }
    }
}