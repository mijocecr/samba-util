using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SAMBA_Util.Helpers
{
    // ---------------------------------------------------------
    // CREDENCIALES (para Mount/Open)
    // ---------------------------------------------------------
    public static class CredStore
    {
        public static string User { get; set; } = "guest";
        public static string Password { get; set; } = "";
    }

    // ---------------------------------------------------------
    // OS OVERRIDE MANAGER
    // ---------------------------------------------------------
    public static class OsOverrideManager
    {
        private static readonly string ConfigDir;
        private static readonly string FilePath;
        private static Dictionary<string, string> Overrides = new();

        static OsOverrideManager()
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(xdg))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                xdg = Path.Combine(home, ".config");
            }

            ConfigDir = Path.Combine(xdg, "samba-util");
            FilePath = Path.Combine(ConfigDir, "os_overrides.json");

            Load();
        }

        public static void Load()
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                    Directory.CreateDirectory(ConfigDir);

                if (!File.Exists(FilePath))
                {
                    Overrides = new Dictionary<string, string>();
                    return;
                }

                string json = File.ReadAllText(FilePath);
                Overrides = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                            ?? new Dictionary<string, string>();
            }
            catch
            {
                Overrides = new Dictionary<string, string>();
            }
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                    Directory.CreateDirectory(ConfigDir);

                string json = JsonSerializer.Serialize(Overrides, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }

        public static bool TryGetOverride(string ip, out string os)
        {
            return Overrides.TryGetValue(ip, out os);
        }

        public static void SetOverride(string ip, string os)
        {
            Overrides[ip] = os;
            Save();
        }
    }

    // ---------------------------------------------------------
    // NETWORK SCANNER
    // ---------------------------------------------------------
    public static class NetworkScanner
    {
        // ---------------------------------------------------------
        // DISCOVER DEVICES
        // ---------------------------------------------------------
        public static async Task<List<NetworkDevice>> DiscoverAsync(string ifaceName)
        {
            var devices = new List<NetworkDevice>();

            string subnet = GetSubnetFromInterface(ifaceName);
            if (subnet == null)
                return devices;

            var alive = await PingSweepAsync(subnet);

            var uniqueHosts = new HashSet<string>();

            foreach (var ip in alive)
            {
                string? mac = await GetMacAsync(ip);

                string hostname = ip;
                try
                {
                    var entry = await System.Net.Dns.GetHostEntryAsync(ip);
                    if (!string.IsNullOrWhiteSpace(entry.HostName))
                        hostname = entry.HostName;
                }
                catch { }

                string key = mac ?? hostname ?? ip;

                if (!uniqueHosts.Add(key))
                    continue;

                devices.Add(new NetworkDevice
                {
                    Name = hostname,
                    Hostname = hostname,
                    IP = ip,
                    OS = "Unknown",
                    Source = ifaceName
                });
            }

            return devices;
        }

        // ---------------------------------------------------------
        // GET MAC ADDRESS
        // ---------------------------------------------------------
        private static async Task<string?> GetMacAsync(string ip)
        {
            string output = await ShellHelper.RunAsync($"arp -n {ip}");

            var match = Regex.Match(output, @"([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}");

            if (match.Success)
                return match.Value.ToLowerInvariant();

            return null;
        }

        // ---------------------------------------------------------
        // OS DETECTION
        // ---------------------------------------------------------
        public static async Task<string> DetectOSAsync(string ip, string name)
        {
            if (OsOverrideManager.TryGetOverride(ip, out var forcedOs))
                return forcedOs;

            if (IsRouter(ip, name))
                return "Router";

            if (await IsPortOpen(ip, 445))
            {
                string smb = await DetectFromSMB(ip);
                if (smb != null)
                    return smb;
            }

            if (await IsPortOpen(ip, 22))
            {
                string ssh = await DetectFromSSH(ip);
                if (ssh != null)
                    return ssh;
            }

            if (await IsPortOpen(ip, 80) || await IsPortOpen(ip, 443))
            {
                string http = await DetectFromHTTP(ip);
                if (http != null)
                    return http;
            }

            string ttl = await DetectFromTTL(ip);
            if (ttl != null)
                return ttl;

            return "Other";
        }

        private static bool IsRouter(string ip, string hostname)
        {
            if (hostname == "_gateway")
                return true;

            if (ip.EndsWith(".1") || ip.EndsWith(".254"))
                return true;

            try
            {
                var ping = new Ping();
                var reply = ping.Send(ip, 200);

                if (reply.Status == IPStatus.Success)
                {
                    int ttl = reply.Options.Ttl;
                    if (ttl == 255 || ttl == 254 || ttl == 253)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static async Task<string> DetectFromSMB(string ip)
        {
            string output = "";
            string lower = output.ToLowerInvariant();

            if (lower.Contains("windows"))
                return "Windows";

            if (lower.Contains("samba"))
            {
                if (lower.Contains("freebsd") || lower.Contains("openbsd") || lower.Contains("netbsd"))
                    return "BSD";

                if (lower.Contains("darwin"))
                    return "macOS";

                return "Linux";
            }

            return null;
        }

        private static async Task<string> DetectFromSSH(string ip)
        {
            try
            {
                using var client = new TcpClient();
                var result = client.ConnectAsync(ip, 22);
                if (await Task.WhenAny(result, Task.Delay(300)) != result)
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

                if (banner.Contains("linux"))
                    return "Linux";

                if (banner.Contains("openssh"))
                    return "Unix";

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string> DetectFromHTTP(string ip)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMilliseconds(600);

                var response = await client.GetAsync($"http://{ip}");
                var headers = response.Headers;

                if (headers.Contains("Server"))
                {
                    string server = string.Join(" ", headers.GetValues("Server")).ToLowerInvariant();

                    if (server.Contains("microsoft"))
                        return "Windows";

                    if (server.Contains("darwin"))
                        return "macOS";

                    if (server.Contains("freebsd"))
                        return "BSD";

                    if (server.Contains("nginx") || server.Contains("apache"))
                        return "Linux";

                    return "Unix";
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string> DetectFromTTL(string ip)
        {
            try
            {
                var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 300);

                if (reply.Status != IPStatus.Success)
                    return null;

                int ttl = reply.Options.Ttl;

                if (ttl >= 120 && ttl <= 135)
                    return "Windows";

                if (ttl >= 60 && ttl <= 70)
                    return "Unix";

                return "Other";
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
        // SHARE ACCESS DETECTION
        // ---------------------------------------------------------
        private static async Task<string> DetectShareAccess(string ip, string share)
        {
            // 1. Probar lectura
            string readTest = await ShellHelper.RunAsync(
                $"smbclient //{ip}/{share} -N -c \"ls\""
            );

            if (readTest.Contains("NT_STATUS_ACCESS_DENIED"))
                return "Requires credentials";

            if (!readTest.Contains("NT_STATUS") && readTest.Contains("blocks"))
            {
                // 2. Probar escritura
                string writeTest = await ShellHelper.RunAsync(
                    $"smbclient //{ip}/{share} -N -c \"put /dev/null __test\""
                );

                if (!writeTest.Contains("NT_STATUS"))
                {
                    await ShellHelper.RunAsync(
                        $"smbclient //{ip}/{share} -N -c \"del __test\""
                    );

                    return "Read/Write (Anonymous)";
                }

                return "Read Only (Anonymous)";
            }

            return "No Access";
        }

        // ---------------------------------------------------------
        // GET SHARES (REMOTE)
        // ---------------------------------------------------------
        public static async Task<List<NetworkShare>> GetSharesAsync(string ip)
        {
            var shares = new List<NetworkShare>();

            try
            {
                string cmd = $"smbclient -L //{ip} -N";
                string output = await ShellHelper.RunAsync(cmd);

                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                bool inShareSection = false;

                foreach (var raw in lines)
                {
                    string line = raw.Trim();

                    if (line.StartsWith("Sharename", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("Server", StringComparison.OrdinalIgnoreCase))
                    {
                        inShareSection = true;
                        continue;
                    }

                    if (line.StartsWith("----") ||
                        line.StartsWith("Anonymous") ||
                        line.StartsWith("Reconnecting", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("SMB1 disabled", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("Domain", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!inShareSection)
                        continue;

                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                        continue;

                    string name = parts[0];

                    if (name is "IPC$" or "print$" or "ADMIN$")
                        continue;

                    var share = new NetworkShare(name)
                    {
                        IP = ip
                    };

                    if (parts.Length > 2)
                    {
                        string comment = string.Join(" ", parts.Skip(2));
                        if (!string.IsNullOrWhiteSpace(comment))
                            share.Comment = comment;
                    }

                    share.Access = await DetectShareAccess(ip, name);

                    if (!shares.Any(s => s.Name == share.Name))
                        shares.Add(share);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SMB] Error leyendo shares de {ip}: {ex.Message}");
            }

            return shares;
        }

        // ---------------------------------------------------------
        // IS MOUNTED
        // ---------------------------------------------------------
        public static bool IsMounted(string mountPoint)
        {
            try
            {
                string mounts = File.ReadAllText("/proc/mounts");
                return mounts.Contains(mountPoint);
            }
            catch
            {
                return false;
            }
        }

        // ---------------------------------------------------------
        // PING SWEEP
        // ---------------------------------------------------------
        private static async Task<List<string>> PingSweepAsync(string subnet)
        {
            var list = new List<string>();
            var tasks = new List<Task>();

            for (int i = 1; i <= 254; i++)
            {
                string ip = $"{subnet}.{i}";

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var ping = new Ping();
                        var reply = await ping.SendPingAsync(ip, 200);

                        if (reply.Status == IPStatus.Success)
                            lock (list) list.Add(ip);
                    }
                    catch { }
                }));
            }

            await Task.WhenAll(tasks);
            return list;
        }

        // ---------------------------------------------------------
        // SUBNET FROM INTERFACE
        // ---------------------------------------------------------
        private static string? GetSubnetFromInterface(string ifaceName)
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.Name != ifaceName)
                    continue;

                var props = ni.GetIPProperties();

                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ip = ua.Address.ToString();
                        var parts = ip.Split('.');
                        return $"{parts[0]}.{parts[1]}.{parts[2]}";
                    }
                }
            }

            return null;
        }
    }

    // ---------------------------------------------------------
    // DEVICE MODEL
    // ---------------------------------------------------------
    public class NetworkDevice
    {
        public string Name { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string IP { get; set; } = "";
        public string OS { get; set; } = "";
        public string Source { get; set; } = "";
    }

    // ---------------------------------------------------------
    // SHARE MODEL
    // ---------------------------------------------------------
    public class NetworkShare
    {
        public string Name { get; set; }
        public string? Comment { get; set; }
        public string Access { get; set; } = "Unknown";
        public string IP { get; set; }

        public NetworkShare(string name)
        {
            Name = name;
        }
    }
}
