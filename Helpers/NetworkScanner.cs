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
using System.Threading;

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
    // NETWORK SCANNER (UNIVERSAL)
    // ---------------------------------------------------------
    public static class NetworkScanner
    {
        // Cache interna para OS
        private static readonly Dictionary<string, string> OsCache = new();

        // ---------------------------------------------------------
        // DISCOVER DEVICES (con OS detection paralela)
        // ---------------------------------------------------------
        public static async Task<List<NetworkDevice>> DiscoverAsync(string ifaceName)
        {
            var devices = new List<NetworkDevice>();

            string subnet = GetSubnetFromInterface(ifaceName);
            if (subnet == null)
                return devices;

            var alive = await PingSweepAsync(subnet);

            var uniqueHosts = new HashSet<string>();

            // Crear lista de dispositivos sin OS
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
                    OS = "Detecting...",
                    Source = ifaceName
                });
            }

            // ---------------------------------------------------------
            // Detectar OS en paralelo (rápido y eficiente)
            // ---------------------------------------------------------
            var tasks = devices.Select(async dev =>
            {
                dev.OS = await DetectOSAsync(dev.IP, dev.Name);
            });

            await Task.WhenAll(tasks);

            return devices;
        }

        // ---------------------------------------------------------
        // GET MAC ADDRESS (MODERNO: ip neigh)
        // ---------------------------------------------------------
        private static async Task<string?> GetMacAsync(string ip)
        {
            string output = await ShellHelper.RunAsync($"ip neigh show {ip}");

            var match = Regex.Match(output, @"([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}");

            if (match.Success)
                return match.Value.ToLowerInvariant();

            return null;
        }

        // ---------------------------------------------------------
        // OS DETECTION (OsDetector rápido + Cache + Overrides)
        // ---------------------------------------------------------
        public static async Task<string> DetectOSAsync(string ip, string name)
        {
            // 1) Override manual del usuario
            if (OsOverrideManager.TryGetOverride(ip, out var forcedOs))
                return forcedOs;

            // 2) Cache interna
            if (OsCache.TryGetValue(ip, out var cached))
                return cached;

            // 3) Detección real (rápida)
            string detected = await OsDetector.DetectOsAsync(ip);

            // 4) Guardar en caché
            OsCache[ip] = detected;

            return detected;
        }

        // ---------------------------------------------------------
        // GET SHARES (UNIVERSAL SMB2/SMB3)
        // ---------------------------------------------------------
        public static async Task<List<NetworkShare>> GetSharesAsync(string ip)
        {
            var shares = new List<NetworkShare>();

            try
            {
                string cmd = $"smbclient -L //{ip} -N --option='client min protocol=SMB2'";
                string output = await ShellHelper.RunAsync(cmd);

                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                bool inShareSection = false;

                foreach (var raw in lines)
                {
                    string line = raw.Trim();

                    if (line.StartsWith("Sharename", StringComparison.OrdinalIgnoreCase))
                    {
                        inShareSection = true;
                        continue;
                    }

                    if (line.StartsWith("SMB1 disabled", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (line.StartsWith("----"))
                        continue;

                    if (!inShareSection)
                        continue;

                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 1)
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
        // SHARE ACCESS DETECTION (MEJORADO)
        // ---------------------------------------------------------
        private static async Task<string> DetectShareAccess(string ip, string share)
        {
            string readTest = await ShellHelper.RunAsync(
                $"smbclient //{ip}/{share} -N --option='client min protocol=SMB2' -c \"ls\""
            );

            if (readTest.Contains("NT_STATUS_ACCESS_DENIED"))
                return "Requires credentials";

            if (readTest.Contains("NT_STATUS_LOGON_FAILURE"))
                return "Requires credentials";

            if (readTest.Contains("NT_STATUS_BAD_NETWORK_NAME"))
                return "No Access";

            if (readTest.Contains("blocks"))
            {
                string writeTest = await ShellHelper.RunAsync(
                    $"smbclient //{ip}/{share} -N --option='client min protocol=SMB2' -c \"put /dev/null __test\""
                );

                if (!writeTest.Contains("NT_STATUS"))
                {
                    await ShellHelper.RunAsync(
                        $"smbclient //{ip}/{share} -N --option='client min protocol=SMB2' -c \"del __test\""
                    );

                    return "Read/Write (Anonymous)";
                }

                return "Read Only (Anonymous)";
            }

            return "Read Only";
        }

        // ---------------------------------------------------------
        // IS MOUNTED (ROBUSTO)
        // ---------------------------------------------------------
        public static bool IsMounted(string mountPoint)
        {
            try
            {
                string mounts = File.ReadAllText("/proc/mounts");
                return mounts.Split('\n').Any(l => l.Contains($" {mountPoint} "));
            }
            catch
            {
                return false;
            }
        }

        // ---------------------------------------------------------
        // PING SWEEP (OPTIMIZADO)
        // ---------------------------------------------------------
        private static async Task<List<string>> PingSweepAsync(string subnet)
        {
            var list = new List<string>();
            var tasks = new List<Task>();

            SemaphoreSlim limiter = new SemaphoreSlim(64);

            for (int i = 1; i <= 254; i++)
            {
                string ip = $"{subnet}.{i}";

                tasks.Add(Task.Run(async () =>
                {
                    await limiter.WaitAsync();
                    try
                    {
                        var ping = new Ping();
                        var reply = await ping.SendPingAsync(ip, 200);

                        if (reply.Status == IPStatus.Success)
                            lock (list) list.Add(ip);
                    }
                    finally
                    {
                        limiter.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);
            return list;
        }

        // ---------------------------------------------------------
        // SUBNET FROM INTERFACE (ROBUSTO)
        // ---------------------------------------------------------
        private static string? GetSubnetFromInterface(string ifaceName)
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!ni.Name.Equals(ifaceName, StringComparison.OrdinalIgnoreCase))
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
