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

namespace SAMBA_Util.Views;

public partial class StatusView : UserControl
{
    private readonly List<string> SmbServices = new() { "smbd", "smb" };
    private readonly List<string> NmbServices = new() { "nmbd", "nmb" };
    private readonly List<string> WinbindServices = new() { "winbind" };

    public StatusView()
    {
        InitializeComponent();
        // ❌ Nada de RefreshStatus aquí
    }

    public void RefreshStatus()
    {
        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
            return;

        TxtSmbd.Text = DetectAndCheckService(SmbServices);
        TxtNmbd.Text = DetectAndCheckService(NmbServices);
        TxtWinbind.Text = DetectAndCheckService(WinbindServices);

        TxtTestparm.Text = RunTestparm();
        TxtLastUpdate.Text = $"Last update: {DateTime.Now:HH:mm:ss}";

        if (App.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is MainWindow mw)
        {
            mw.UpdateStatus("Status refreshed");
        }

        SetIcon(IconSmbd, TxtSmbd.Text);
        SetIcon(IconNmbd, TxtNmbd.Text);
        SetIcon(IconWinbind, TxtWinbind.Text);

        UpdateTestparmStyle(TxtTestparm.Text);
        UpdateIpList();
    }

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

    private string RunTestparm()
    {
        var result = ShellHelper.EjecutarComoRoot("testparm -s 2>&1");
        var output = (result.Stdout + result.Stderr).Trim();

        if (output.Contains("Loaded services file OK", StringComparison.OrdinalIgnoreCase))
            return "✔ Configuration OK";

        if (output.Contains("ERROR:", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Unknown parameter", StringComparison.OrdinalIgnoreCase))
            return "❌ Configuration errors detected:\n" + output;

        return "✔ Configuration OK";
    }

    public void OnRefresh(object? sender, RoutedEventArgs e)
    {
        RefreshStatus();
    }

    private void OnReload(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
            return;

        ShellHelper.EjecutarComoRoot("systemctl reload smb.service");
        ShellHelper.EjecutarComoRoot("systemctl reload smbd.service");
        ShellHelper.EjecutarComoRoot("systemctl reload nmb.service");
        ShellHelper.EjecutarComoRoot("systemctl reload nmbd.service");
        ShellHelper.EjecutarComoRoot("systemctl reload winbind.service");

        RefreshStatus();
    }

    private void OnRestart(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
            return;

        ShellHelper.EjecutarComoRoot("systemctl restart smb.service");
        ShellHelper.EjecutarComoRoot("systemctl restart smbd.service");
        ShellHelper.EjecutarComoRoot("systemctl restart nmb.service");
        ShellHelper.EjecutarComoRoot("systemctl restart nmbd.service");
        ShellHelper.EjecutarComoRoot("systemctl restart winbind.service");

        RefreshStatus();
    }

    private void SetIcon(Ellipse icon, string status)
    {
        if (status.StartsWith("Active"))
            icon.Fill = Brushes.LimeGreen;
        else if (status.StartsWith("Inactive"))
            icon.Fill = Brushes.Red;
        else
            icon.Fill = Brushes.Gray;
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
