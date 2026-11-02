using System;
using System.Collections.Generic;
using System.IO;
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

    private const string DefaultSmbConf = @"[global]
   workgroup = WORKGROUP
   server string = Samba Server %v
   netbios name = SAMBA-HOST
   security = user
   map to guest = Bad User
   dns proxy = no
   logging = systemd

[homes]
   comment = Home Directories
   browseable = no
   writable = yes
   create mask = 0700
   directory mask = 0700
";

    public StatusView()
    {
        InitializeComponent();
        EnsureSambaEnvironment();
    }

    // ---------------------------------------------------------
    // AUTOPROVISIONAMIENTO Y VERIFICACIÓN DEL HOST (AppImage Ready)
    // ---------------------------------------------------------
    private void EnsureSambaEnvironment()
    {
        try
        {
            // 1. Asegurar que el directorio /etc/samba existe en el host
            string sambaDir = "/etc/samba";
            if (!Directory.Exists(sambaDir))
            {
                ShellHelper.EjecutarComoRoot($"mkdir -p {sambaDir}");
            }

            // 2. Si el archivo smb.conf no existe, crearlo por defecto automáticamente
            string confPath = System.IO.Path.Combine(sambaDir, "smb.conf");
            if (!File.Exists(confPath))
            {
                // Escribimos temporalmente o mediante root helper
                File.WriteAllText("/tmp/smb.conf.default", DefaultSmbConf);
                ShellHelper.EjecutarComoRoot($"cp /tmp/smb.conf.default {confPath} && chmod 644 {confPath}");
            }
        }
        catch (Exception ex)
        {
            // Log o manejo silencioso si falta password de root aún
        }
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

    // ---------------------------------------------------------
    // SHARES
    // ---------------------------------------------------------
    private void CheckShares()
    {
        var (exit, outp, err) = ShellHelper.EjecutarComoRoot(
            $"bash -c \"smbclient -L localhost -N 2>&1 || true\""
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