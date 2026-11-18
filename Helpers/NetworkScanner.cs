using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
            return Overrides.TryGetValue(ip, out os!);
        }

        public static void SetOverride(string ip, string os)
        {
            Overrides[ip] = os;
            Save();
        }
    }

    // ---------------------------------------------------------
    // SHARE ENUMERATION RESULT
    // ---------------------------------------------------------
    // Distingue el motivo por el que una enumeración no devolvió recursos, para
    // que la interfaz solo pida credenciales cuando el servidor las exige de
    // verdad y explique el problema en cualquier otro caso.
    public enum ShareEnumerationOutcome
    {
        // El comando se ejecutó y su salida se pudo interpretar (con o sin recursos).
        Success,

        // El servidor rechazó el acceso anónimo: tiene sentido pedir credenciales.
        RequiresCredentials,

        // Faltan las herramientas de cliente SMB (smbclient/rpcclient).
        ClientMissing,

        // Error de red u otro error ajeno a la autenticación.
        Failed
    }

    public sealed class ShareEnumerationResult
    {
        public ShareEnumerationOutcome Outcome { get; init; }
        public List<NetworkShare> Shares { get; init; } = new();
    }

    // ---------------------------------------------------------
    // NETWORK SCANNER
    // ---------------------------------------------------------
    public static class NetworkScanner
    {
        // ConcurrentDictionary: DetectOSAsync se ejecuta en paralelo desde
        // Task.WhenAll, así que la caché debe ser segura para hilos.
        private static readonly ConcurrentDictionary<string, string> OsCache = new();

        // ---------------------------------------------------------
        // DISCOVER DEVICES
        // ---------------------------------------------------------
        public static async Task<List<NetworkDevice>> DiscoverAsync(string ifaceName, CancellationToken cancellationToken = default)
        {
            var devices = new List<NetworkDevice>();

            string? subnet = GetSubnetFromInterface(ifaceName);
            if (subnet == null)
                return devices;

            var alive = await PingSweepAsync(subnet, cancellationToken);

            foreach (var ip in alive)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
        // AUTH ARGS (Windows / macOS / Linux)
        // ---------------------------------------------------------
        // Devuelve los argumentos de autenticación para smbclient/rpcclient.
        // Cuando hay contraseña se usa un fichero temporal (-A) para no
        // exponerla en la línea de comandos (visible con `ps`) ni en los logs.
        // El llamador debe borrar AuthFile con ShellHelper.DeleteCredentialFile.
        private static (string Args, string? AuthFile) BuildAuthArgs()
        {
            string user = CredStore.User;
            string pass = CredStore.Password;

            if (string.IsNullOrWhiteSpace(user) || user.Equals("guest", StringComparison.OrdinalIgnoreCase))
                return ("-U %", null);

            if (string.IsNullOrEmpty(pass))
                return ($"-U {ShellQuote(user)}%", null);

            string file = ShellHelper.CreateCredentialFile(user, pass);
            return ($"-A {ShellQuote(file)}", file);
        }

        // ---------------------------------------------------------
        // SHARE ENUMERATION (RPC for Windows, SMBCLIENT for others)
        // ---------------------------------------------------------
        // Envoltorio que solo devuelve los recursos, para los llamadores que no
        // necesitan saber el motivo de un fallo.
        public static async Task<List<NetworkShare>> GetSharesAsync(string ip, CancellationToken cancellationToken = default)
        {
            var result = await GetSharesResultAsync(ip, cancellationToken);
            return result.Shares;
        }

        // Enumeración con el motivo del fallo incluido. La interfaz usa esto
        // para pedir credenciales solo cuando el servidor las exige.
        public static async Task<ShareEnumerationResult> GetSharesResultAsync(string ip, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"[Scanner] === GET SHARES FOR {ip} ===");

            string os = await DetectOSAsync(ip, ip);

            cancellationToken.ThrowIfCancellationRequested();

            if (os == "Windows")
                return await EnumerateWindowsAsync(ip, cancellationToken);

            return await EnumerateSmbClientAsync(ip, cancellationToken);
        }

        // ---------------------------------------------------------
        // WINDOWS → RPC netshareenumall
        // ---------------------------------------------------------
        private static async Task<ShareEnumerationResult> EnumerateWindowsAsync(string ip, CancellationToken cancellationToken)
        {
            if (!await Task.Run(SambaEnvironment.HasRpcClient))
                return ClientMissing();

            string rpcOut;
            var (authArgs, authFile) = BuildAuthArgs();
            try
            {
                rpcOut = await ShellHelper.RunAsync(
                    $"rpcclient {authArgs} {ShellHelper.Quote(ip)} -c \"netshareenumall\""
                );
            }
            finally
            {
                ShellHelper.DeleteCredentialFile(authFile);
            }

            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine("[Scanner] RPC output:");
            Console.WriteLine(rpcOut);

            if (rpcOut.Contains("NT_STATUS") && HasCredentials())
            {
                Console.WriteLine("[Scanner] Anonymous attempt rejected. Retrying RPC with credentials...");
                var (retryArgs, retryFile) = BuildAuthArgs();
                try
                {
                    rpcOut = await ShellHelper.RunAsync(
                        $"rpcclient {retryArgs} {ShellHelper.Quote(ip)} -c \"netshareenumall\""
                    );
                }
                finally
                {
                    ShellHelper.DeleteCredentialFile(retryFile);
                }

                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine("[Scanner] RPC Credential output:");
                Console.WriteLine(rpcOut);
            }

            if (rpcOut.Contains("NT_STATUS"))
                return ClassifyFailure(rpcOut);

            var shares = new List<NetworkShare>();
            foreach (var line in rpcOut.Split('\n'))
            {
                if (!line.Contains("netname:")) continue;

                string name = line.Replace("netname:", "").Trim();
                if (string.IsNullOrWhiteSpace(name) || name.EndsWith("$")) continue;

                shares.Add(new NetworkShare(name)
                {
                    IP = ip,
                    Access = "Authenticated",
                    Comment = "Windows RPC"
                });
            }

            return Success(shares);
        }

        // ---------------------------------------------------------
        // NON-WINDOWS → SMBCLIENT
        // ---------------------------------------------------------
        private static async Task<ShareEnumerationResult> EnumerateSmbClientAsync(string ip, CancellationToken cancellationToken)
        {
            if (!await Task.Run(SambaEnvironment.HasSmbClient))
                return ClientMissing();

            // `-U %` es ya un intento anónimo (usuario y contraseña vacíos):
            // antes de pedir credenciales se prueba sin ellas.
            string outGuest = await ShellHelper.RunAsync(
                $"smbclient -gL {ShellQuote("//" + ip)} -U % {ConfigManager.ClientMinProtocolOption()}"
            );

            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine("[Scanner] Guest output:");
            Console.WriteLine(outGuest);

            if (!outGuest.Contains("NT_STATUS"))
            {
                var parsed = ParseShares(ip, outGuest);

                foreach (var share in parsed)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    share.Access = await DetectShareAccess(ip, share.Name);
                }

                return Success(parsed);
            }

            if (HasCredentials())
            {
                var (authArgs, authFile) = BuildAuthArgs();
                string outCred;
                try
                {
                    outCred = await ShellHelper.RunAsync(
                        $"smbclient -gL {ShellQuote("//" + ip)} {authArgs} {ConfigManager.ClientMinProtocolOption()}"
                    );
                }
                finally
                {
                    ShellHelper.DeleteCredentialFile(authFile);
                }

                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine("[Scanner] Credential output:");
                Console.WriteLine(outCred);

                if (!outCred.Contains("NT_STATUS"))
                {
                    var parsed = ParseShares(ip, outCred);

                    foreach (var share in parsed)
                        share.Access = "Authenticated";

                    return Success(parsed);
                }

                return ClassifyFailure(outCred);
            }

            Console.WriteLine("[Scanner] Enumeration failed.");
            return ClassifyFailure(outGuest);
        }

        private static bool HasCredentials() =>
            !string.IsNullOrWhiteSpace(CredStore.User) &&
            !CredStore.User.Equals("guest", StringComparison.OrdinalIgnoreCase);

        // Traduce la salida de smbclient/rpcclient a un motivo. Solo se piden
        // credenciales cuando el servidor las rechaza explícitamente; un error
        // de red u otro fallo no debe abrir el diálogo de credenciales.
        internal static ShareEnumerationOutcome ClassifyEnumerationFailure(string output)
        {
            if (string.IsNullOrEmpty(output))
                return ShareEnumerationOutcome.Failed;

            string upper = output.ToUpperInvariant();

            if (upper.Contains("NT_STATUS_ACCESS_DENIED") ||
                upper.Contains("NT_STATUS_LOGON_FAILURE") ||
                upper.Contains("NT_STATUS_ACCOUNT_DISABLED") ||
                upper.Contains("NT_STATUS_ACCOUNT_EXPIRED") ||
                upper.Contains("NT_STATUS_ACCOUNT_LOCKED_OUT") ||
                upper.Contains("NT_STATUS_PASSWORD_MUST_CHANGE") ||
                upper.Contains("NT_STATUS_PASSWORD_EXPIRED") ||
                upper.Contains("NT_STATUS_NO_LOGON_SERVERS") ||
                upper.Contains("NT_STATUS_MORE_PROCESSING_REQUIRED"))
            {
                return ShareEnumerationOutcome.RequiresCredentials;
            }

            return ShareEnumerationOutcome.Failed;
        }

        private static ShareEnumerationResult ClassifyFailure(string output) =>
            new() { Outcome = ClassifyEnumerationFailure(output) };

        private static ShareEnumerationResult Success(List<NetworkShare> shares) =>
            new() { Outcome = ShareEnumerationOutcome.Success, Shares = shares };

        private static ShareEnumerationResult ClientMissing() =>
            new() { Outcome = ShareEnumerationOutcome.ClientMissing };

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

        private static string ShellQuote(string value) =>
            "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

        // ---------------------------------------------------------
        // ACCESS DETECTION (SMBCLIENT)
        // ---------------------------------------------------------
        private static async Task<string> DetectShareAccess(string ip, string share)
        {
            Console.WriteLine($"[Scanner] Testing access for share '{share}'...");

            string guestCmd =
                $"smbclient {ShellQuote("//" + ip + "/" + share)} -U % {ConfigManager.ClientMinProtocolOption()} -c \"ls\"";

            string guestOut = await ShellHelper.RunAsync(guestCmd);

            if (guestOut.Contains("blocks") || guestOut.Contains("NT_STATUS_OK"))
                return "Anonymous";

            string anonCmd =
                $"smbclient {ShellQuote("//" + ip + "/" + share)} -N {ConfigManager.ClientMinProtocolOption()} -c \"ls\"";

            string anonOut = await ShellHelper.RunAsync(anonCmd);

            if (anonOut.Contains("blocks") || anonOut.Contains("NT_STATUS_OK"))
                return "Anonymous";

            var (authArgs, authFile) = BuildAuthArgs();
            string credOut;
            try
            {
                credOut = await ShellHelper.RunAsync(
                    $"smbclient {ShellQuote("//" + ip + "/" + share)} {authArgs} {ConfigManager.ClientMinProtocolOption()} -c \"ls\"");
            }
            finally
            {
                ShellHelper.DeleteCredentialFile(authFile);
            }

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
        private static async Task<List<string>> PingSweepAsync(string subnet, CancellationToken cancellationToken = default)
        {
            var list = new List<string>();
            var tasks = new List<Task>();
            var limiter = new SemaphoreSlim(64);

            for (int i = 1; i <= 254; i++)
            {
                string ip = $"{subnet}.{i}";

                tasks.Add(Task.Run(async () =>
                {
                    await limiter.WaitAsync(cancellationToken);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var ping = new Ping();
                        var reply = await ping.SendPingAsync(ip, 200);

                        if (reply.Status == IPStatus.Success)
                            lock (list) list.Add(ip);
                    }
                    finally
                    {
                        limiter.Release();
                    }
                }, cancellationToken));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) { }

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
        public string IP { get; set; } = "";
        public string RemoteOS { get; set; } = "";

        public NetworkShare(string name)
        {
            Name = name;
        }
    }
}