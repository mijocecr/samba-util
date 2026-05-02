using Avalonia.Controls;
using Avalonia.Interactivity;
using SAMBA_Util.Helpers;
using System;
using System.Collections.Generic;
using Avalonia.Controls.ApplicationLifetimes;

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

    private void RefreshStatus()
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

    private void OnRefresh(object? sender, RoutedEventArgs e)
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
}
