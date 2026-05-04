using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SAMBA_Util.Helpers
{
    public static class OsDetector
    {
        public static async Task<string> DetectOsAsync(string ip)
        {
            // 1) Intentar SMB OS-Level (si hay 445 abierto)
            if (await IsPortOpen(ip, 445))
            {
                string smbOs = await DetectFromSmbAsync(ip);
                if (smbOs != null)
                    return smbOs;
            }

            // 2) Intentar SSH banner (si hay 22 abierto)
            if (await IsPortOpen(ip, 22))
            {
                string sshOs = await DetectFromSshAsync(ip);
                if (sshOs != null)
                    return sshOs;
            }

            // 3) Intentar HTTP headers (si hay 80/443)
            if (await IsPortOpen(ip, 80) || await IsPortOpen(ip, 443))
            {
                string httpOs = await DetectFromHttpAsync(ip);
                if (httpOs != null)
                    return httpOs;
            }

            // 4) TTL + puertos básicos → heurística
            string ttlOs = await DetectFromTtlAsync(ip);
            if (ttlOs != null)
                return ttlOs;

            return "Other";
        }

        // ---------------------------------------------------------
        // SMB OS-LEVEL (Windows / Samba / BSD / Unix)
        // ---------------------------------------------------------
        private static async Task<string> DetectFromSmbAsync(string ip)
        {
            // Aquí podrías invocar "smbclient -L" vía proceso externo
            // y parsear la salida. Te dejo un stub para que lo conectes
            // con tu sistema de SHELL ya existente.

            string output = await RunShellAsync($"smbclient -L //{ip} -N 2>/dev/null");

            string lower = output.ToLowerInvariant();

            if (lower.Contains("windows"))
                return "Windows";

            if (lower.Contains("samba"))
            {
                if (lower.Contains("freebsd") || lower.Contains("openbsd") || lower.Contains("netbsd"))
                    return "BSD";

                if (lower.Contains("darwin"))
                    return "macOS";

                // Samba genérico → Linux o Unix
                if (lower.Contains("truenas") || lower.Contains("ixsystems"))
                    return "Unix";

                return "Linux";
            }

            return null;
        }

        // ---------------------------------------------------------
        // SSH BANNER (Linux / BSD / macOS / Unix)
        // ---------------------------------------------------------
        private static async Task<string> DetectFromSshAsync(string ip)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, 22);
                if (await Task.WhenAny(connectTask, Task.Delay(400)) != connectTask)
                    return null;

                using var stream = client.GetStream();
                byte[] buffer = new byte[256];
                int read = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0) return null;

                string banner = Encoding.ASCII.GetString(buffer, 0, read).ToLowerInvariant();

                if (banner.Contains("windows"))
                    return "Windows";

                if (banner.Contains("darwin"))
                    return "macOS";

                if (banner.Contains("freebsd") || banner.Contains("openbsd") || banner.Contains("netbsd"))
                    return "BSD";

                if (banner.Contains("linux") || banner.Contains("ubuntu") || banner.Contains("debian") ||
                    banner.Contains("arch") || banner.Contains("fedora") || banner.Contains("centos"))
                    return "Linux";

                // OpenSSH sin pista clara → Unix genérico
                if (banner.Contains("openssh"))
                    return "Unix";

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ---------------------------------------------------------
        // HTTP HEADERS (pistas de Unix / macOS / Linux / appliances)
        // ---------------------------------------------------------
        private static async Task<string> DetectFromHttpAsync(string ip)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMilliseconds(600);

                var response = await client.GetAsync($"http://{ip}");
                var headers = response.Headers;

                string server = "";
                if (headers.Contains("Server"))
                    server = string.Join(" ", headers.GetValues("Server")).ToLowerInvariant();

                if (server.Contains("microsoft"))
                    return "Windows";

                if (server.Contains("darwin"))
                    return "macOS";

                if (server.Contains("nginx") || server.Contains("apache"))
                    return "Linux";

                if (server.Contains("freebsd") || server.Contains("openbsd") || server.Contains("netbsd"))
                    return "BSD";

                if (!string.IsNullOrEmpty(server))
                    return "Unix"; // servidor HTTP genérico en Unix-like

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ---------------------------------------------------------
        // TTL + PUERTOS → HEURÍSTICA FINAL
        // ---------------------------------------------------------
        private static async Task<string> DetectFromTtlAsync(string ip)
        {
            try
            {
                var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 300);

                if (reply.Status != IPStatus.Success)
                    return null;

                int ttl = reply.Options.Ttl;

                // Windows suele 128
                if (ttl >= 120 && ttl <= 135)
                    return "Windows";

                // 64 → Linux / macOS / BSD / Unix
                if (ttl >= 60 && ttl <= 70)
                {
                    bool hasSSH = await IsPortOpen(ip, 22);
                    bool hasSMB = await IsPortOpen(ip, 445);

                    if (hasSSH && !hasSMB)
                        return "Unix"; // sin más pistas, Unix genérico

                    return "Unix";
                }

                // TTL alto → routers / appliances → Other
                if (ttl >= 200)
                    return "Other";

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ---------------------------------------------------------
        // PORT CHECK
        // ---------------------------------------------------------
        private static async Task<bool> IsPortOpen(string ip, int port)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(250);

                var completed = await Task.WhenAny(connectTask, timeoutTask);
                if (completed == timeoutTask)
                    return false;

                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        // ---------------------------------------------------------
        // SHELL HELPER (ADÁPTALO A TU SISTEMA EXISTENTE)
        // ---------------------------------------------------------
        private static async Task<string> RunShellAsync(string cmd)
        {
            // Aquí deberías usar tu propio wrapper de shell (EjecutarComoRoot, etc.)
            // De momento, stub vacío:
            await Task.CompletedTask;
            return "";
        }
    }
}
