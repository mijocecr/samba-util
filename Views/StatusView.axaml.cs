using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using SAMBA_Util;
using SAMBA_Util.Helpers;
using System.Linq;

namespace SAMBA_Util.Views;

public partial class StatusView : UserControl
{
    private readonly List<string> SmbServices = new() { "smbd", "smb" };
    private readonly List<string> NmbServices = new() { "nmbd", "nmb" };
    private readonly List<string> WinbindServices = new() { "winbind" };

    private bool _isRefreshing;
    private bool _isBusy;

    public StatusView()
    {
        InitializeComponent();
    }

    // ---------------------------------------------------------
    // REFRESH STATUS
    // ---------------------------------------------------------
    public async Task RefreshStatusAsync()
    {
        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
            return;

        if (_isRefreshing)
            return;

        _isRefreshing = true;
        try
        {
            //  El trabajo con sudo se hace fuera del hilo de UI.
            var snapshot = await Task.Run(CollectStatus);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplySnapshot(snapshot);

                if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow is MainWindow mw)
                {
                    mw.UpdateStatus("Status refreshed successfully");
                }
            });
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    // Recoge el estado sin tocar controles: se ejecuta en segundo plano.
    private StatusSnapshot CollectStatus()
    {
        // Validar si el stack está instalado globalmente en el host
        var checkSmbdBinary = ShellHelper.EjecutarComoRoot("which smbd");
        bool isStackInstalled = checkSmbdBinary.ExitCode == 0 && !string.IsNullOrWhiteSpace(checkSmbdBinary.Stdout);

        if (!isStackInstalled)
        {
            return new StatusSnapshot(
                IsStackInstalled: false,
                SmbdText: "Not installed (Host missing samba package)",
                NmbdText: "",
                WinbindText: "",
                TestparmText: "❌ Stack de Samba no encontrado en el sistema operativo.",
                Port445Text: "",
                Port139Text: "",
                FirewallText: "Not checked (Samba is not installed)",
                FirewallColor: Brushes.Gray,
                SharesText: "",
                UserCheckText: "",
                Ips: new List<(string iface, string ip)>());
        }

        // Servicios
        var smbd = DetectAndCheckService(SmbServices);
        var nmbd = DetectAndCheckService(NmbServices);
        var winbind = DetectAndCheckService(WinbindServices);

        // testparm (Validación estricta anti-falsos positivos)
        var testparm = RunTestparm();

        // Puertos y firewall
        var port445 = CheckPortText("445");
        var port139 = CheckPortText("139");
        var (firewallText, firewallColor) = CheckFirewallStatus(port445);

        // Shares y consistencia de usuarios
        var shares = CheckSharesText();
        var userCheck = CheckUserConsistencyText();

        // Interfaces
        var ips = GetActiveIPs();

        return new StatusSnapshot(
            IsStackInstalled: true,
            SmbdText: smbd,
            NmbdText: nmbd,
            WinbindText: winbind,
            TestparmText: testparm,
            Port445Text: port445,
            Port139Text: port139,
            FirewallText: firewallText,
            FirewallColor: firewallColor,
            SharesText: shares,
            UserCheckText: userCheck,
            Ips: ips);
    }

    // Aplica el snapshot en el hilo de UI.
    private void ApplySnapshot(StatusSnapshot snapshot)
    {
        if (!snapshot.IsStackInstalled)
        {
            TxtSmbd.Text = snapshot.SmbdText;
            SetIcon(IconSmbd, "Inactive");
            TxtFirewall.Text = snapshot.FirewallText;
            IconFirewall.Fill = snapshot.FirewallColor;
            TxtTestparm.Text = snapshot.TestparmText;
            UpdateTestparmStyle(snapshot.TestparmText);
            return;
        }

        TxtSmbd.Text = snapshot.SmbdText;
        TxtNmbd.Text = snapshot.NmbdText;
        TxtWinbind.Text = snapshot.WinbindText;

        SetIcon(IconSmbd, snapshot.SmbdText);
        SetIcon(IconNmbd, snapshot.NmbdText);
        SetIcon(IconWinbind, snapshot.WinbindText);

        TxtTestparm.Text = snapshot.TestparmText;
        UpdateTestparmStyle(snapshot.TestparmText);

        TxtPort445.Text = snapshot.Port445Text;
        IconPort445.Fill = snapshot.Port445Text == "Open" ? Brushes.LimeGreen : Brushes.Red;

        TxtPort139.Text = snapshot.Port139Text;
        IconPort139.Fill = snapshot.Port139Text == "Open" ? Brushes.LimeGreen : Brushes.Red;

        TxtFirewall.Text = snapshot.FirewallText;
        IconFirewall.Fill = snapshot.FirewallColor;

        TxtShares.Text = snapshot.SharesText;
        TxtUserCheck.Text = snapshot.UserCheckText;

        ApplyIpList(snapshot.Ips);
        TxtLastUpdate.Text = $"Last update: {DateTime.Now:HH:mm:ss}";
    }

    private sealed record StatusSnapshot(
        bool IsStackInstalled,
        string SmbdText,
        string NmbdText,
        string WinbindText,
        string TestparmText,
        string Port445Text,
        string Port139Text,
        string FirewallText,
        IBrush FirewallColor,
        string SharesText,
        string UserCheckText,
        List<(string iface, string ip)> Ips);

    // ---------------------------------------------------------
    // SET ICON
    // ---------------------------------------------------------
    private void SetIcon(Ellipse icon, string status)
    {
        if (status.StartsWith("Active"))
            icon.Fill = Brushes.LimeGreen;
        else if (status.StartsWith("Inactive") || status.StartsWith("Not installed"))
            icon.Fill = Brushes.Red;
        else
            icon.Fill = Brushes.Gray;
    }

    // ---------------------------------------------------------
    // DETECCIÓN DE SERVICIOS
    // ---------------------------------------------------------
    private string DetectAndCheckService(List<string> candidates)
    {
        foreach (var svc in candidates)
        {
            var check = ShellHelper.EjecutarComoRoot($"systemctl status {svc}.service");

            if (check.ExitCode == 0 || check.Stdout.Contains("Loaded:", StringComparison.OrdinalIgnoreCase))
                return GetServiceStatus(svc);
        }

        return "Not installed";
    }

    private string GetServiceStatus(string service)
    {
        var result = ShellHelper.EjecutarComoRoot($"systemctl is-active {service}.service");

        return result.Stdout.Trim() == "active"
            ? "Active (running)"
            : "Inactive";
    }

    // ---------------------------------------------------------
    // TESTPARM (Blindado contra falsos positivos)
    // ---------------------------------------------------------
    private string RunTestparm()
    {
        var result = ShellHelper.EjecutarComoRoot("testparm -s 2>&1", ShellHelper.LongTimeoutMs);
        var output = (result.Stdout + result.Stderr).Trim();

        // Verificación rigurosa de la salida real de testparm
        if (output.Contains("Loaded services file OK", StringComparison.OrdinalIgnoreCase))
            return "✔ Configuration OK";

        if (result.ExitCode != 0 || output.Contains("ERROR:", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Unknown parameter", StringComparison.OrdinalIgnoreCase))
        {
            return "❌ Configuration errors detected:\n" + output;
        }

        return "✔ Configuration OK";
    }

    private void UpdateTestparmStyle(string text)
    {
        if (text.StartsWith("✔"))
        {
            TestparmBorder.Background = Brushes.DarkGreen;
            TestparmBorder.BorderBrush = Brushes.LimeGreen;
        }
        else
        {
            TestparmBorder.Background = Brushes.DarkRed;
            TestparmBorder.BorderBrush = Brushes.OrangeRed;
        }
    }

    // ---------------------------------------------------------
    // PUERTOS
    // ---------------------------------------------------------
    private string CheckPortText(string port)
    {
        var (_, outp, _) = ShellHelper.EjecutarComoRoot(
            $"bash -c \"ss -tulpn | grep :{port} || true\""
        );

        return string.IsNullOrWhiteSpace(outp) ? "Closed" : "Open";
    }

    private (string Text, IBrush Color) CheckFirewallStatus(string port445Text)
    {
        if (port445Text != "Open")
        {
            return ("TCP 445 is not listening; remote SMB access will fail regardless of firewall.", Brushes.Red);
        }

        var ufw = ShellHelper.EjecutarComoRoot(
            "if command -v ufw >/dev/null 2>&1; then ufw status; else echo __NOT_INSTALLED__; fi");
        if (!ufw.Stdout.Contains("__NOT_INSTALLED__", StringComparison.Ordinal))
        {
            if (ufw.Stdout.Contains("Status: active", StringComparison.OrdinalIgnoreCase))
            {
                var allowsSmb = ufw.Stdout.Split('\n').Any(line =>
                    line.Contains("ALLOW IN", StringComparison.OrdinalIgnoreCase) &&
                    (line.Contains("445/tcp", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("Samba", StringComparison.OrdinalIgnoreCase)));
                return (
                    allowsSmb
                        ? "UFW is active; an inbound SMB/TCP 445 allow rule was found. Test from another PC."
                        : "UFW is active; no inbound SMB/TCP 445 allow rule was found. It may block remote access.",
                    allowsSmb ? Brushes.LimeGreen : Brushes.OrangeRed);
            }
        }

        var firewalldState = ShellHelper.EjecutarComoRoot(
            "if command -v firewall-cmd >/dev/null 2>&1; then firewall-cmd --state; else echo __NOT_INSTALLED__; fi");
        if (firewalldState.Stdout.Trim().Equals("running", StringComparison.OrdinalIgnoreCase))
        {
            var activeZones = ShellHelper.EjecutarComoRoot("firewall-cmd --get-active-zones");
            var zones = activeZones.Stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !char.IsWhiteSpace(line[0]))
                .Select(line => line.Trim())
                .ToList();

            var allowsSmb = zones.Any(zone =>
            {
                var zoneStatus = ShellHelper.EjecutarComoRoot(
                    $"firewall-cmd --zone={ShellQuote(zone)} --list-all");
                var output = zoneStatus.Stdout;
                var services = output.Split("services:", StringSplitOptions.None).LastOrDefault() ?? "";
                var ports = output.Split("ports:", StringSplitOptions.None).LastOrDefault() ?? "";
                return services.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                           .Contains("samba", StringComparer.OrdinalIgnoreCase) ||
                       ports.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                           .Contains("445/tcp", StringComparer.OrdinalIgnoreCase);
            });

            return (
                allowsSmb
                    ? "firewalld is active; an SMB/TCP 445 allow rule was found in an active zone. Test from another PC."
                    : "firewalld is active; no SMB/TCP 445 allow rule was found in active zones. It may block remote access.",
                allowsSmb ? Brushes.LimeGreen : Brushes.OrangeRed);
        }

        var nftables = ShellHelper.EjecutarComoRoot(
            "if command -v nft >/dev/null 2>&1; then nft list ruleset | awk '/^[[:space:]]*chain / { chains++ } /hook input/ { input=1 } /policy (drop|reject)/ { blocked=1 } END { if (chains == 0) print \"EMPTY\"; else if (input && blocked) print \"INPUT_FILTER\"; else print \"RULESET\" }'; else echo __NOT_INSTALLED__; fi");
        if (!nftables.Stdout.Contains("__NOT_INSTALLED__", StringComparison.Ordinal) &&
            !nftables.Stdout.Contains("EMPTY", StringComparison.Ordinal))
        {
            return (
                nftables.Stdout.Contains("INPUT_FILTER", StringComparison.Ordinal)
                    ? "nftables has an input filtering policy; local inspection cannot confirm TCP 445 is allowed. Test from another PC."
                    : "An nftables ruleset exists; remote TCP 445 access cannot be confirmed locally. Test from another PC.",
                Brushes.Orange);
        }

        var iptables = ShellHelper.EjecutarComoRoot(
            "if command -v iptables >/dev/null 2>&1; then iptables -S INPUT; else echo __NOT_INSTALLED__; fi");
        if (!iptables.Stdout.Contains("__NOT_INSTALLED__", StringComparison.Ordinal))
        {
            var lines = iptables.Stdout.Split('\n');
            var hasDropPolicy = lines.Any(line =>
                line.StartsWith("-P INPUT DROP", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("-P INPUT REJECT", StringComparison.OrdinalIgnoreCase));
            var allowsSmb = lines.Any(line =>
                line.Contains("--dport 445", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("-j ACCEPT", StringComparison.OrdinalIgnoreCase));
            if (hasDropPolicy || allowsSmb)
            {
                return (
                    allowsSmb
                        ? "iptables has an inbound TCP 445 allow rule. Test from another PC."
                        : "iptables has a DROP/REJECT input policy and no TCP 445 allow rule; it may block remote access.",
                    allowsSmb ? Brushes.LimeGreen : Brushes.OrangeRed);
            }
        }

        return (
            "No active UFW/firewalld filtering or blocking rules were detected. Network-level filtering is not checked.",
            Brushes.LimeGreen);
    }

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    // ---------------------------------------------------------
    // SHARES
    // ---------------------------------------------------------
    private string CheckSharesText()
    {
        var (_, outp, _) = ShellHelper.EjecutarComoRoot(
            $"bash -c \"smbclient -L localhost -N {ConfigManager.ClientMinProtocolOption()} 2>&1 || true\"",
            ShellHelper.NetworkTimeoutMs
        );

        return outp.Contains("Sharename", StringComparison.OrdinalIgnoreCase)
            ? "✔ Shares detected"
            : "❌ No shares detected or Samba not responding";
    }

    // ---------------------------------------------------------
    // CONSISTENCIA DE USUARIOS
    // ---------------------------------------------------------
    private string CheckUserConsistencyText()
    {
        var (_, smbUsers, _) = ShellHelper.EjecutarComoRoot(
            "bash -c \"pdbedit -L | cut -d: -f1 || true\"",
            ShellHelper.LongTimeoutMs
        );

        var (_, unixUsers, _) = ShellHelper.EjecutarComoRoot(
            "bash -c \"getent passwd | cut -d: -f1 || true\"",
            ShellHelper.LongTimeoutMs
        );

        var smb = smbUsers.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var unix = unixUsers.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var missingUnix = smb.Where(u => !unix.Contains(u)).ToList();
        var missingSmb = unix.Where(u =>
            !smb.Contains(u) &&
            !u.StartsWith("systemd") &&
            !u.StartsWith("root") &&
            !u.StartsWith("daemon") &&
            !u.StartsWith("nobody")
        ).ToList();

        return
            $"Samba users without UNIX account: {(missingUnix.Count == 0 ? "None" : string.Join(", ", missingUnix))}\n" +
            $"UNIX users without Samba account: {(missingSmb.Count == 0 ? "None" : string.Join(", ", missingSmb))}";
    }

    // ---------------------------------------------------------
    // BOTONES
    // ---------------------------------------------------------
    public async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STATUS ERROR] {ex}");
        }
    }

    private async void OnReload(object? sender, RoutedEventArgs e)
    {
        try
        {
            await RunServiceActionAsync(
                "systemctl reload smb.service smbd.service nmb.service nmbd.service winbind.service || true");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STATUS ERROR] {ex}");
        }
    }

    private async void OnRestart(object? sender, RoutedEventArgs e)
    {
        try
        {
            await RunServiceActionAsync(
                "systemctl restart smb.service smbd.service nmb.service nmbd.service winbind.service || true");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STATUS ERROR] {ex}");
        }
    }

    private async Task RunServiceActionAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
            return;

        if (_isBusy)
            return;

        _isBusy = true;
        try
        {
            await Task.Run(() => ShellHelper.EjecutarComoRoot(command, ShellHelper.LongTimeoutMs));
        }
        finally
        {
            _isBusy = false;
        }

        await RefreshStatusAsync();
    }

    // ---------------------------------------------------------
    // INTERFACES
    // ---------------------------------------------------------
    private List<(string iface, string ip)> GetActiveIPs()
    {
        var list = new List<(string iface, string ip)>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;

            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var props = ni.GetIPProperties();

            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    list.Add((ni.Name, addr.Address.ToString()));
            }
        }

        return list;
    }

    private void ApplyIpList(List<(string iface, string ip)> ips)
    {
        IpListPanel.Children.Clear();

        if (ips.Count == 0)
        {
            IpListPanel.Children.Add(new TextBlock { Text = "No active interfaces detected." });
            return;
        }

        foreach (var (iface, ip) in ips)
        {
            IpListPanel.Children.Add(
                new TextBlock
                {
                    Text = $"{iface}: {ip}",
                    FontFamily = "Consolas",
                    FontSize = 14
                }
            );
        }
    }
}