using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using SAMBA_Util;
using SAMBA_Util.Helpers;
using System.Linq;

namespace SAMBA_Util.Views;

public partial class StatusView : UserControl
{
    private readonly List<string> SmbServices = new() { "smbd", "smb" };
    private readonly List<string> NmbServices = new() { "nmbd", "nmb" };
    private readonly List<string> WinbindServices = new() { "winbind" };

    public StatusView()
    {
        InitializeComponent();
    }

    // ---------------------------------------------------------
    // REFRESH STATUS
    // ---------------------------------------------------------
    public void RefreshStatus()
    {
        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
            return;

        // Validar si el stack está instalado globalmente en el host
        var checkSmbdBinary = ShellHelper.EjecutarComoRoot("which smbd");
        bool isStackInstalled = checkSmbdBinary.ExitCode == 0 && !string.IsNullOrWhiteSpace(checkSmbdBinary.Stdout);

        if (!isStackInstalled)
        {
            TxtSmbd.Text = "Not installed (Host missing samba package)";
            SetIcon(IconSmbd, "Inactive");
            TxtFirewall.Text = "Not checked (Samba is not installed)";
            IconFirewall.Fill = Brushes.Gray;
            TxtTestparm.Text = "❌ Stack de Samba no encontrado en el sistema operativo.";
            UpdateTestparmStyle("❌");
            return;
        }

        // Servicios
        TxtSmbd.Text = DetectAndCheckService(SmbServices);
        TxtNmbd.Text = DetectAndCheckService(NmbServices);
        TxtWinbind.Text = DetectAndCheckService(WinbindServices);

        SetIcon(IconSmbd, TxtSmbd.Text);
        SetIcon(IconNmbd, TxtNmbd.Text);
        SetIcon(IconWinbind, TxtWinbind.Text);

        // testparm (Validación estricta anti-falsos positivos)
        TxtTestparm.Text = RunTestparm();
        UpdateTestparmStyle(TxtTestparm.Text);

        // Puertos
        CheckPort("445", IconPort445, TxtPort445);
        CheckPort("139", IconPort139, TxtPort139);
        CheckFirewall();

        // Shares
        CheckShares();

        // Consistencia de usuarios
        CheckUserConsistency();

        // Interfaces
        UpdateIpList();

        // Timestamp
        TxtLastUpdate.Text = $"Last update: {DateTime.Now:HH:mm:ss}";

        if (App.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is MainWindow mw)
        {
            mw.UpdateStatus("Status refreshed successfully");
        }
    }

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
        var result = ShellHelper.EjecutarComoRoot("testparm -s 2>&1");
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
    private void CheckPort(string port, Ellipse icon, TextBlock txt)
    {
        var (exit, outp, err) = ShellHelper.EjecutarComoRoot(
            $"bash -c \"ss -tulpn | grep :{port} || true\""
        );

        if (string.IsNullOrWhiteSpace(outp))
        {
            txt.Text = "Closed";
            icon.Fill = Brushes.Red;
        }
        else
        {
            txt.Text = "Open";
            icon.Fill = Brushes.LimeGreen;
        }
    }

    private void CheckFirewall()
    {
        if (TxtPort445.Text != "Open")
        {
            TxtFirewall.Text = "TCP 445 is not listening; remote SMB access will fail regardless of firewall.";
            IconFirewall.Fill = Brushes.Red;
            return;
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
                SetFirewallStatus(
                    allowsSmb
                        ? "UFW is active; an inbound SMB/TCP 445 allow rule was found. Test from another PC."
                        : "UFW is active; no inbound SMB/TCP 445 allow rule was found. It may block remote access.",
                    allowsSmb ? Brushes.LimeGreen : Brushes.OrangeRed);
                return;
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

            SetFirewallStatus(
                allowsSmb
                    ? "firewalld is active; an SMB/TCP 445 allow rule was found in an active zone. Test from another PC."
                    : "firewalld is active; no SMB/TCP 445 allow rule was found in active zones. It may block remote access.",
                allowsSmb ? Brushes.LimeGreen : Brushes.OrangeRed);
            return;
        }

        var nftables = ShellHelper.EjecutarComoRoot(
            "if command -v nft >/dev/null 2>&1; then nft list ruleset | awk '/^[[:space:]]*chain / { chains++ } /hook input/ { input=1 } /policy (drop|reject)/ { blocked=1 } END { if (chains == 0) print \"EMPTY\"; else if (input && blocked) print \"INPUT_FILTER\"; else print \"RULESET\" }'; else echo __NOT_INSTALLED__; fi");
        if (!nftables.Stdout.Contains("__NOT_INSTALLED__", StringComparison.Ordinal) &&
            !nftables.Stdout.Contains("EMPTY", StringComparison.Ordinal))
        {
            SetFirewallStatus(
                nftables.Stdout.Contains("INPUT_FILTER", StringComparison.Ordinal)
                    ? "nftables has an input filtering policy; local inspection cannot confirm TCP 445 is allowed. Test from another PC."
                    : "An nftables ruleset exists; remote TCP 445 access cannot be confirmed locally. Test from another PC.",
                Brushes.Orange);
            return;
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
                SetFirewallStatus(
                    allowsSmb
                        ? "iptables has an inbound TCP 445 allow rule. Test from another PC."
                        : "iptables has a DROP/REJECT input policy and no TCP 445 allow rule; it may block remote access.",
                    allowsSmb ? Brushes.LimeGreen : Brushes.OrangeRed);
                return;
            }
        }

        SetFirewallStatus(
            "No active UFW/firewalld filtering or blocking rules were detected. Network-level filtering is not checked.",
            Brushes.LimeGreen);
    }

    private void SetFirewallStatus(string text, IBrush color)
    {
        TxtFirewall.Text = text;
        IconFirewall.Fill = color;
    }

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    // ---------------------------------------------------------
    // SHARES
    // ---------------------------------------------------------
    private void CheckShares()
    {
        var (exit, outp, err) = ShellHelper.EjecutarComoRoot(
            $"bash -c \"smbclient -L localhost -N {ConfigManager.ClientMinProtocolOption()} 2>&1 || true\""
        );

        if (outp.Contains("Sharename", StringComparison.OrdinalIgnoreCase))
            TxtShares.Text = "✔ Shares detected";
        else
            TxtShares.Text = "❌ No shares detected or Samba not responding";
    }

    // ---------------------------------------------------------
    // CONSISTENCIA DE USUARIOS
    // ---------------------------------------------------------
    private void CheckUserConsistency()
    {
        var (exit1, smbUsers, _) = ShellHelper.EjecutarComoRoot(
            "bash -c \"pdbedit -L | cut -d: -f1 || true\""
        );

        var (exit2, unixUsers, _) = ShellHelper.EjecutarComoRoot(
            "bash -c \"getent passwd | cut -d: -f1 || true\""
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

        TxtUserCheck.Text =
            $"Samba users without UNIX account: {(missingUnix.Count == 0 ? "None" : string.Join(", ", missingUnix))}\n" +
            $"UNIX users without Samba account: {(missingSmb.Count == 0 ? "None" : string.Join(", ", missingSmb))}";
    }

    // ---------------------------------------------------------
    // BOTONES
    // ---------------------------------------------------------
    public void OnRefresh(object? sender, RoutedEventArgs e)
    {
        RefreshStatus();
    }

    private void OnReload(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
            return;

        ShellHelper.EjecutarComoRoot("systemctl reload smb.service smbd.service nmb.service nmbd.service winbind.service || true");
        RefreshStatus();
    }

    private void OnRestart(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
            return;

        ShellHelper.EjecutarComoRoot("systemctl restart smb.service smbd.service nmb.service nmbd.service winbind.service || true");
        RefreshStatus();
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

    private void UpdateIpList()
    {
        IpListPanel.Children.Clear();

        var ips = GetActiveIPs();

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