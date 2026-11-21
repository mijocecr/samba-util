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

        // La cuenta está bloqueada: volver a pedir credenciales solo agrava
        // el bloqueo, así que se trata como un error con explicación propia.
        AccountLockedOut,

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

            // `authArgs` ya incluye las credenciales disponibles, así que no
            // tiene sentido repetir la llamada: solo generaría intentos de
            // acceso extra (y podría agravar un bloqueo de cuenta).

            // Un mensaje NT_STATUS aislado (por ejemplo al sondear IPC$) no
            // significa que fallara la enumeración: si hay recursos, se usan.
            var shares = ParseRpcShares(ip, rpcOut);
            if (shares.Count > 0)
                return Success(shares);

            return ClassifyFailure(rpcOut);
        }

        // ---------------------------------------------------------
        // NON-WINDOWS → SMBCLIENT
        // ---------------------------------------------------------
        private static async Task<ShareEnumerationResult> EnumerateSmbClientAsync(string ip, CancellationToken cancellationToken)
        {
            if (!await Task.Run(SambaEnvironment.HasSmbClient))
                return ClientMissing();

            // Si el usuario ya dio credenciales, tienen prioridad sobre el
            // acceso invitado. macOS con guest activado puede exponer un share
            // `nobody` que no debe ocultar los recursos reales de esa cuenta.
            if (HasCredentials())
            {
                var authResult = await RunAuthenticatedEnumerationAsync(ip, cancellationToken);
                if (ShouldReturnAuthenticatedResult(authResult))
                    return authResult;
            }

            // `-U %` es ya un intento anónimo (usuario y contraseña vacíos):
            // antes de pedir credenciales se prueba sin ellas. En macOS ese
            // formulario solo sirve para IPC$, así que si el servidor rechaza
            // el intento se repite con el usuario `guest`, que es como Finder
            // identifica el acceso invitado.
            string outGuest = await RunAnonymousEnumerationAsync(ip, cancellationToken);

            var guestResult = EvaluateSmbClientOutput(ip, outGuest, authenticated: false);
            if (guestResult.Outcome == ShareEnumerationOutcome.Success &&
                guestResult.Shares.Count > 0)
            {
                foreach (var share in guestResult.Shares)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    share.Access = await DetectShareAccess(ip, share.Name);
                }

                return guestResult;
            }

            Console.WriteLine("[Scanner] Enumeration failed.");
            return guestResult.Outcome == ShareEnumerationOutcome.Success
                ? guestResult
                : ClassifyFailure(outGuest);
        }

        // Un rechazo explícito (o una cuenta bloqueada) debe propagarse: caer
        // al invitado en ese caso ocultaría el motivo real del fallo.
        internal static bool ShouldReturnAuthenticatedResult(ShareEnumerationResult result) =>
            result.Outcome == ShareEnumerationOutcome.AccountLockedOut ||
            result.Outcome == ShareEnumerationOutcome.RequiresCredentials ||
            (result.Outcome == ShareEnumerationOutcome.Success && result.Shares.Count > 0);

        private static async Task<ShareEnumerationResult> RunAuthenticatedEnumerationAsync(
            string ip, CancellationToken cancellationToken)
        {
            var (authArgs, authFile) = BuildAuthArgs();
            try
            {
                string output = await ShellHelper.RunAsync(
                    $"smbclient -gL {ShellQuote("//" + ip)} {authArgs} {ConfigManager.ClientMinProtocolOption()}");

                cancellationToken.ThrowIfCancellationRequested();

                Console.WriteLine("[Scanner] Credential output:");
                Console.WriteLine(output);

                return EvaluateSmbClientOutput(ip, output, authenticated: true);
            }
            finally
            {
                ShellHelper.DeleteCredentialFile(authFile);
            }
        }

        // macOS no acepta `-U %` para el acceso invitado: solo devuelve IPC$
        // o LOGON_FAILURE. Solo repetimos con `guest` ante un rechazo claro de
        // credenciales; no reintentamos otros errores para no agravar bloqueos.
        internal static bool ShouldRetryAnonymousAsGuest(string output) =>
            output.Contains("NT_STATUS_LOGON_FAILURE", StringComparison.Ordinal);

        private static async Task<string> RunAnonymousEnumerationAsync(
            string ip, CancellationToken cancellationToken)
        {
            string commandPrefix = $"smbclient -gL {ShellQuote("//" + ip)}";
            string protocol = ConfigManager.ClientMinProtocolOption();

            string emptyUserOutput = await ShellHelper.RunAsync(
                $"{commandPrefix} -U % {protocol}");

            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine("[Scanner] Guest output (-U %):");
            Console.WriteLine(emptyUserOutput);

            if (!ShouldRetryAnonymousAsGuest(emptyUserOutput))
                return emptyUserOutput;

            string guestUserOutput = await ShellHelper.RunAsync(
                $"{commandPrefix} -U guest% {protocol}");

            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine("[Scanner] Guest output (-U guest%):");
            Console.WriteLine(guestUserOutput);

            var emptyResult = EvaluateSmbClientOutput(ip, emptyUserOutput, authenticated: false);
            var guestResult = EvaluateSmbClientOutput(ip, guestUserOutput, authenticated: false);

            if (guestResult.Shares.Count > emptyResult.Shares.Count)
                return guestUserOutput;

            // macOS puede responder solo con el pseudo-share `nobody`. No es
            // un recurso real, pero sí indica que hizo falta autenticarse.
            if (guestResult.Outcome == ShareEnumerationOutcome.RequiresCredentials &&
                emptyResult.Shares.Count == 0)
                return guestUserOutput;

            return emptyUserOutput;
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

            if (upper.Contains("NT_STATUS_ACCOUNT_LOCKED_OUT"))
                return ShareEnumerationOutcome.AccountLockedOut;

            if (upper.Contains("NT_STATUS_ACCESS_DENIED") ||
                upper.Contains("NT_STATUS_LOGON_FAILURE") ||
                upper.Contains("NT_STATUS_ACCOUNT_DISABLED") ||
                upper.Contains("NT_STATUS_ACCOUNT_EXPIRED") ||
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
        // SHARE PARSING (RPC / SMBCLIENT)
        // ---------------------------------------------------------
        internal static List<NetworkShare> ParseRpcShares(string ip, string output)
        {
            var shares = new List<NetworkShare>();

            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains("netname:", StringComparison.Ordinal)) continue;

                string name = line.Replace("netname:", "", StringComparison.Ordinal).Trim();
                if (string.IsNullOrWhiteSpace(name) || name.EndsWith("$", StringComparison.Ordinal)) continue;

                shares.Add(new NetworkShare(name)
                {
                    IP = ip,
                    Access = "Authenticated",
                    Comment = "Windows RPC"
                });
            }

            return shares;
        }

        // Evalúa la salida de smbclient dando prioridad a los recursos que se
        // hayan enumerado. Algunos servidores (Windows incluido) imprimen un
        // NT_STATUS por un sondeo secundario aunque la lista de shares sea
        // válida; en ese caso no se deben descartar los recursos obtenidos.
        internal static ShareEnumerationResult EvaluateSmbClientOutput(string ip, string output, bool authenticated)
        {
            var shares = ParseShares(ip, output);
            bool onlyMacGuestPseudoShare = false;

            if (!authenticated)
            {
                // Con SMB guest activado, macOS anuncia un share `nobody` que
                // no es accesible. No debe mostrarse como recurso de la red.
                onlyMacGuestPseudoShare = shares.Count > 0 &&
                    shares.All(s => s.Name.Equals("nobody", StringComparison.OrdinalIgnoreCase));
                shares.RemoveAll(s => s.Name.Equals("nobody", StringComparison.OrdinalIgnoreCase));
            }

            if (shares.Count > 0)
            {
                if (authenticated)
                    foreach (var share in shares)
                        share.Access = "Authenticated";

                return Success(shares);
            }

            if (onlyMacGuestPseudoShare)
                return new() { Outcome = ShareEnumerationOutcome.RequiresCredentials };

            if (string.IsNullOrWhiteSpace(output))
                return Success(shares);

            return ClassifyFailure(output);
        }

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

        private static bool IsSuccessfulListing(string output) =>
            output.Contains("blocks", StringComparison.Ordinal) ||
            output.Contains("NT_STATUS_OK", StringComparison.Ordinal);

        // ---------------------------------------------------------
        // ACCESS DETECTION (SMBCLIENT)
        // ---------------------------------------------------------
        private static async Task<string> DetectShareAccess(string ip, string share)
        {
            Console.WriteLine($"[Scanner] Testing access for share '{share}'...");

            string shareUrl = ShellQuote("//" + ip + "/" + share);
            string protocol = ConfigManager.ClientMinProtocolOption();

            string guestOut = await ShellHelper.RunAsync(
                $"smbclient {shareUrl} -U % {protocol} -c \"ls\"");

            if (IsSuccessfulListing(guestOut))
                return "Anonymous";

            // Igual que en el listado: macOS solo entra como invitado si el
            // usuario se llama explícitamente `guest`.
            if (ShouldRetryAnonymousAsGuest(guestOut))
            {
                guestOut = await ShellHelper.RunAsync(
                    $"smbclient {shareUrl} -U guest% {protocol} -c \"ls\"");

                if (IsSuccessfulListing(guestOut))
                    return "Anonymous";
            }

            string anonOut = await ShellHelper.RunAsync(
                $"smbclient {shareUrl} -N {protocol} -c \"ls\"");

            if (IsSuccessfulListing(anonOut))
                return "Anonymous";

            var (authArgs, authFile) = BuildAuthArgs();
            string credOut;
            try
            {
                credOut = await ShellHelper.RunAsync(
                    $"smbclient {shareUrl} {authArgs} {protocol} -c \"ls\"");
            }
            finally
            {
                ShellHelper.DeleteCredentialFile(authFile);
            }

            if (IsSuccessfulListing(credOut))
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