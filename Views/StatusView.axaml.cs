using Avalonia.Controls;
using Avalonia.Interactivity;
using SAMBA_Util.Helpers;
using System;
using System.Collections.Generic;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using System.Net.NetworkInformation;
using System.Net;

namespace SAMBA_Util.Views;

public partial class StatusView : UserControl
{
    // Servicios posibles según la distro
    private readonly List<string> SmbServices = new() { "smbd", "smb" };
    private readonly List<string> NmbServices = new() { "nmbd", "nmb" };
    private readonly List<string> WinbindServices = new() { "winbind" };

    public StatusView()
    {
        InitializeComponent();
        RefreshStatus();
    }

    public void RefreshStatus()
    {
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

    // Detecta qué servicio existe y devuelve su estado
    private string DetectAndCheckService(List<string> candidates)
    {
        foreach (var svc in candidates)
        {
            var check = ShellHelper.EjecutarComoRoot($"systemctl status {svc}.service");

            // Tu lógica original: funciona, no se toca
            if (check.ExitCode == 0 || check.Stdout.Contains("Loaded:", StringComparison.OrdinalIgnoreCase))
            {
                return GetServiceStatus(svc);
            }
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

        // 1. Si testparm dice explícitamente que está OK → está OK
        if (output.Contains("Loaded services file OK", StringComparison.OrdinalIgnoreCase))
            return "✔ Configuration OK";

        // 2. Errores reales de Samba
        if (output.Contains("ERROR:", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Unknown parameter", StringComparison.OrdinalIgnoreCase))
        {
            return "❌ Configuration errors detected:\n" + output;
        }

        // 3. Cualquier otra cosa (warnings, avisos, etc.) → OK
        return "✔ Configuration OK";
    }

    public void OnRefresh(object? sender, RoutedEventArgs e)
    {
        RefreshStatus();
    }

    private void OnReload(object? sender, RoutedEventArgs e)
    {
        // Recarga TODOS los servicios posibles en TODAS las distros
        ShellHelper.EjecutarComoRoot("systemctl reload smb.service");
        ShellHelper.EjecutarComoRoot("systemctl reload smbd.service");
        ShellHelper.EjecutarComoRoot("systemctl reload nmb.service");
        ShellHelper.EjecutarComoRoot("systemctl reload nmbd.service");
        ShellHelper.EjecutarComoRoot("systemctl reload winbind.service");

        RefreshStatus();
    }

    private void OnRestart(object? sender, RoutedEventArgs e)
    {
        // Reinicia TODOS los servicios posibles en TODAS las distros
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
                {
                    list.Add((ni.Name, addr.Address.ToString()));
                }
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

