using System;
using Avalonia.Controls;
using Avalonia.Dialogs;
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
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _config = ConfigManager.Load();

        // Cargar ruta smb.conf
        TxtSmbConf.Text = _config.SmbConfPath;

        // Seleccionar permiso actual en el ComboBox
        foreach (ComboBoxItem item in PermCombo.Items)
        {
            if (item.Tag?.ToString() == _config.DefaultPermissions)
            {
                PermCombo.SelectedItem = item;
                break;
            }
        }

        // Eventos
        BtnBrowseSmbConf.Click += OnBrowseSmbConf;
    }

    private async void OnBrowseSmbConf(object? sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select smb.conf",
            AllowMultiple = false
        };

        dlg.Filters.Add(new FileDialogFilter
        {
            Name = "Samba config",
            Extensions = { "conf" }
        });

        var result = await dlg.ShowAsync(this);

        if (result != null && result.Length > 0)
            TxtSmbConf.Text = result[0];
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // Guardar ruta
        _config.SmbConfPath = TxtSmbConf.Text?.Trim() ?? "/etc/samba/smb.conf";

        // Guardar permisos desde el ComboBox
        if (PermCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string permStr &&
            !string.IsNullOrWhiteSpace(permStr))
        {
            _config.DefaultPermissions = permStr;
        }
        else
        {
            _config.DefaultPermissions = "755"; // fallback seguro
        }

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
