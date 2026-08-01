using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace SAMBA_Util.Helpers
{
    // ---------------------------------------------------------
    // CREDENCIALES
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
        private static readonly Dictionary<string, string> OsCache = new();

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

            foreach (var ip in alive)
            {
                string hostname = ip;

                try
                {
                    var entry = await System.Net.Dns.GetHostEntryAsync(ip);
                    if (!string.IsNullOrWhiteSpace(entry.HostName))
                        hostname = entry.HostName;
                }
                catch { }

                devices.Add(new NetworkDevice
                {
                    Name = hostname,
                    Hostname = hostname,
                    IP = ip,
                    OS = "Detecting...",
                    Source = ifaceName
                });
            }

            var tasks = devices.Select(async dev =>
            {
                dev.OS = await DetectOSAsync(dev.IP, dev.Name);
            });

            await Task.WhenAll(tasks);

            return devices;
        }

        // ---------------------------------------------------------
        // OS DETECTION
        // ---------------------------------------------------------
        public static async Task<string> DetectOSAsync(string ip, string name)
        {
            if (OsOverrideManager.TryGetOverride(ip, out var forcedOs))
                return forcedOs;

            if (OsCache.TryGetValue(ip, out var cached))
                return cached;

            string detected = await OsDetector.DetectOsAsync(ip);
            OsCache[ip] = detected;

            return detected;
        }

        // ---------------------------------------------------------
        // UNIVERSAL USER SPEC (Windows / macOS / Linux)
        // ---------------------------------------------------------
        private static string BuildUserSpec(string ip)
        {
            string user = CredStore.User;
            string pass = CredStore.Password;

            if (user == "guest")
                return "guest%";

            if (OsCache.TryGetValue(ip, out var os) &&
                os.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            {
                return $"WORKGROUP\\{user}%{pass}";
            }

            return $"{user}%{pass}";
        }

        // ---------------------------------------------------------
        // SHARE ENUMERATION (CON FALLBACK WINDOWS)
        // ---------------------------------------------------------
        public static async Task<List<NetworkShare>> GetSharesAsync(string ip)
        {
            Console.Clear();
            Console.WriteLine($"[Scanner] === GET SHARES FOR {ip} ===");

            var shares = new List<NetworkShare>();

            // ---------------------------------------------------------
            // 1) Intentar guest
            // ---------------------------------------------------------
            string outGuest = await ShellHelper.RunAsync(
                $"smbclient -gL //{ip} -U guest% --option='client min protocol=SMB2'"
            );

            Console.WriteLine("[Scanner] Guest output:");
            Console.WriteLine(outGuest);

            if (!outGuest.Contains("NT_STATUS"))
            {
                var parsed = ParseShares(ip, outGuest);

                foreach (var share in parsed)
                    share.Access = await DetectShareAccess(ip, share.Name);

                return parsed;
            }

            // ---------------------------------------------------------
            // 2) Intentar credenciales reales
            // ---------------------------------------------------------
            if (!string.IsNullOrWhiteSpace(CredStore.User) &&
                CredStore.User != "guest")
            {
                string userSpec = BuildUserSpec(ip);

                string outCred = await ShellHelper.RunAsync(
                    $"smbclient -gL //{ip} -U {userSpec} --option='client min protocol=SMB2'"
                );

                Console.WriteLine("[Scanner] CredStore output:");
                Console.WriteLine(outCred);

                if (!outCred.Contains("NT_STATUS"))
                {
                    var parsed = ParseShares(ip, outCred);

                    foreach (var share in parsed)
                        share.Access = "Authenticated";

                    return parsed;
                }
            }

            // ---------------------------------------------------------
            // 3) FALLBACK WINDOWS
            // ---------------------------------------------------------
            Console.WriteLine("[Scanner] Enumeration failed → Windows fallback");

            string[] commonShares =
            {
                "Users",
                "Public",
                "Documents",
                "Downloads",
                "Desktop",
                "Shared",
                "Share",
                "Data",
                "Miguel",
                "MacOs",
                "Carpeta pública de “Miguel Cerrato”"
            };

            string winUserSpec = BuildUserSpec(ip);

            foreach (var shareName in commonShares)
            {
                string cmd =
                    $"smbclient //{ip}/{shareName} -U {winUserSpec} --option='client min protocol=SMB2' -c \"ls\"";

                Console.WriteLine($"[Scanner] Testing fallback share: {shareName}");
                string output = await ShellHelper.RunAsync(cmd);

                if (!output.Contains("NT_STATUS"))
                {
                    shares.Add(new NetworkShare(shareName)
                    {
                        IP = ip,
                        Access = "Authenticated",
                        Comment = "Detected via Windows fallback"
                    });
                }
            }

            if (shares.Count > 0)
                return shares;

            Console.WriteLine("[Scanner] Unable to enumerate shares (guest + credstore + fallback failed).");
            return shares;
        }

        private static List<NetworkShare> ParseShares(string ip, string output)
        {
            var shares = new List<NetworkShare>();

            foreach (var line in output.Split('\n'))
            {
                if (!line.StartsWith("Disk|")) continue;

                var parts = line.Split('|');
                if (parts.Length < 2) continue;

                string name = parts[1];
                if (string.IsNullOrWhiteSpace(name)) continue;

                shares.Add(new NetworkShare(name)
                {
                    IP = ip,
                    Comment = parts.Length > 2 ? parts[2] : ""
                });
            }

            return shares;
        }
        // ---------------------------------------------------------
        // ACCESS DETECTION
        // ---------------------------------------------------------
        private static async Task<string> DetectShareAccess(string ip, string share)
        {
            Console.WriteLine($"[Scanner] Testing access for share '{share}'...");

            // 1) Guest
            string guestCmd =
                $"smbclient //{ip}/{share} -U guest% --option='client min protocol=SMB2' -c \"ls\"";

            string guestOut = await ShellHelper.RunAsync(guestCmd);

            if (guestOut.Contains("blocks") || guestOut.Contains("NT_STATUS_OK"))
                return "Anonymous";

            // 2) Anonymous (sin usuario)
            string anonCmd =
                $"smbclient //{ip}/{share} -N --option='client min protocol=SMB2' -c \"ls\"";

            string anonOut = await ShellHelper.RunAsync(anonCmd);

            if (anonOut.Contains("blocks") || anonOut.Contains("NT_STATUS_OK"))
                return "Anonymous";

            // 3) Credenciales reales
            string credCmd =
                $"smbclient //{ip}/{share} -U {BuildUserSpec(ip)} --option='client min protocol=SMB2' -c \"ls\"";

            string credOut = await ShellHelper.RunAsync(credCmd);

            if (credOut.Contains("blocks") || credOut.Contains("NT_STATUS_OK"))
                return "Authenticated";

            if (credOut.Contains("NT_STATUS_ACCESS_DENIED") ||
                credOut.Contains("NT_STATUS_LOGON_FAILURE"))
                return "Requires credentials";

            if (credOut.Contains("NT_STATUS_BAD_NETWORK_NAME"))
                return "No Access";

            return "Requires credentials";
        }


        // ---------------------------------------------------------
        // PING SWEEP
        // ---------------------------------------------------------
        private static async Task<List<string>> PingSweepAsync(string subnet)
        {
            var list = new List<string>();
            var tasks = new List<Task>();
            var limiter = new SemaphoreSlim(64);

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
        // SUBNET FROM INTERFACE
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
