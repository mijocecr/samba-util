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
    // CREDENTIALS
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
        // OS DETECTION (cached + override)
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
        public static string BuildUserSpec(string ip, string os)
        {
            string user = CredStore.User;
            string pass = CredStore.Password;

            if (string.IsNullOrWhiteSpace(user))
                return "guest%";

            return $"{user}%{pass}";
        }

        // ---------------------------------------------------------
        // SHARE ENUMERATION (RPC for Windows, SMBCLIENT for others)
        // ---------------------------------------------------------
        public static async Task<List<NetworkShare>> GetSharesAsync(string ip)
        {
            Console.Clear();
            Console.WriteLine($"[Scanner] === GET SHARES FOR {ip} ===");

            var shares = new List<NetworkShare>();

            string os = await DetectOSAsync(ip, ip);

            // ---------------------------------------------------------
            // WINDOWS → RPC netshareenumall
            // ---------------------------------------------------------
            if (os == "Windows")
            {
                string userSpec = BuildUserSpec(ip, os);

                string rpcOut = await ShellHelper.RunAsync(
                    $"rpcclient -U {userSpec} {ip} -c \"netshareenumall\""
                );

                Console.WriteLine("[Scanner] RPC output:");
                Console.WriteLine(rpcOut);

                foreach (var line in rpcOut.Split('\n'))
                {
                    if (!line.Contains("netname:")) continue;

                    string name = line.Replace("netname:", "").Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    shares.Add(new NetworkShare(name)
                    {
                        IP = ip,
                        Access = "Authenticated",
                        Comment = "Windows RPC"
                    });
                }

                return shares;
            }

            // ---------------------------------------------------------
            // NON-WINDOWS → SMBCLIENT
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
                    share.Access = await DetectShareAccess(ip, share.Name, os);

                return parsed;
            }

            if (!string.IsNullOrWhiteSpace(CredStore.User) &&
                CredStore.User != "guest")
            {
                string userSpec = BuildUserSpec(ip, os);

                string outCred = await ShellHelper.RunAsync(
                    $"smbclient -gL //{ip} -U {userSpec} --option='client min protocol=SMB2'"
                );

                Console.WriteLine("[Scanner] Credential output:");
                Console.WriteLine(outCred);

                if (!outCred.Contains("NT_STATUS"))
                {
                    var parsed = ParseShares(ip, outCred);

                    foreach (var share in parsed)
                        share.Access = "Authenticated";

                    return parsed;
                }
            }

            Console.WriteLine("[Scanner] Enumeration failed.");
            return shares;
        }

        // ---------------------------------------------------------
        // SHARE PARSING (SMBCLIENT)
        // ---------------------------------------------------------
        private static List<NetworkShare> ParseShares(string ip, string output)
        {
            var shares = new List<NetworkShare>();

            foreach (var line in output.Split('\n'))
            {
                if (!line.StartsWith("Disk|")) continue;

                var parts = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                string name = parts[1].Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                shares.Add(new NetworkShare(name)
                {
                    IP = ip,
                    Comment = parts.Length > 2 ? parts[2].Trim() : ""
                });
            }

            return shares;
        }

        // ---------------------------------------------------------
        // ACCESS DETECTION (SMBCLIENT)
        // ---------------------------------------------------------
        private static async Task<string> DetectShareAccess(string ip, string share, string os)
        {
            Console.WriteLine($"[Scanner] Testing access for share '{share}'...");

            string userSpec = BuildUserSpec(ip, os);

            string guestCmd =
                $"smbclient //{ip}/{share} -U guest% --option='client min protocol=SMB2' -c \"ls\"";

            string guestOut = await ShellHelper.RunAsync(guestCmd);

            if (guestOut.Contains("blocks") || guestOut.Contains("NT_STATUS_OK"))
                return "Anonymous";

            string anonCmd =
                $"smbclient //{ip}/{share} -N --option='client min protocol=SMB2' -c \"ls\"";

            string anonOut = await ShellHelper.RunAsync(anonCmd);

            if (anonOut.Contains("blocks") || anonOut.Contains("NT_STATUS_OK"))
                return "Anonymous";

            string credCmd =
                $"smbclient //{ip}/{share} -U {userSpec} --option='client min protocol=SMB2' -c \"ls\"";

            string credOut = await ShellHelper.RunAsync(credCmd);

            if (credOut.Contains("blocks") || credOut.Contains("NT_STATUS_OK"))
                return "Authenticated";

            if (credOut.Contains("NT_STATUS_ACCESS_DENIED") ||
                credOut.Contains("NT_STATUS_LOGON_FAILURE"))
                return "Requires credentials";

            if (credOut.Contains("NT_STATUS_BAD_NETWORK_NAME") ||
                credOut.Contains("NT_STATUS_BAD_NETWORK_PATH"))
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
