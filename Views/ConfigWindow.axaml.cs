using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select smb.conf",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Samba config")
                {
                    Patterns = new[] { "*.conf" }
                }
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                TxtSmbConf.Text = path;
            }
        }
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