using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SAMBA_Util.Helpers;

namespace SAMBA_Util.Views;

public partial class ConfigWindow : Window
{
    private ConfigManager.AppConfig _config;

    public event Action? ConfigSaved;

    public ConfigWindow()
    {
        InitializeComponent();
        base.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _config = ConfigManager.Load();

        TxtSmbConf.Text = _config.SmbConfPath;
        TxtDefaultPerms.Text = _config.DefaultPermissions;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _config.SmbConfPath = TxtSmbConf.Text?.Trim() ?? "/etc/samba/smb.conf";

        // Normalizar permisos
        string perms = TxtDefaultPerms.Text?.Trim() ?? "755";
        perms = perms.TrimStart('0');
        if (perms.Length > 3)
            perms = perms[^3..];
        perms = perms.PadLeft(3, '0');

        _config.DefaultPermissions = perms;

        ConfigManager.Save(_config);

        // 🔥 Notificar a MainWindow
        ConfigSaved?.Invoke();

        Close();
    }

 
    
    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}