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

        string perms = TxtDefaultPerms.Text?.Trim() ?? "755";

        // 🔥 Validación: solo números y longitud 3
        if (!int.TryParse(perms, out _) || perms.Length < 3 || perms.Length > 4)
        {
            var msg = new Window
            {
                Width = 350,
                Height = 150,
                Title = "Invalid Permissions",
                Content = new TextBlock
                {
                    Text = "Invalid permission format. Use values like 755 or 644.",
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                },
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            msg.ShowDialog(this);
            return;
        }

        // 🔥 Normalizar (quitar ceros iniciales)
        perms = perms.TrimStart('0');
        perms = perms.PadLeft(3, '0');

        _config.DefaultPermissions = perms;

        ConfigManager.Save(_config);

        // Notificar a MainWindow
        ConfigSaved?.Invoke();

        Close();
    }

 
    
    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}