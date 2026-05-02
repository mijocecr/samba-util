using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SAMBA_Util.Views;

public partial class ConfigWindow : Window
{
    public ConfigWindow()
    {
        InitializeComponent();

        // Cargar valores actuales
        TxtSmbConf.Text = "/etc/samba/smb.conf";
        TxtDefaultPerms.Text = "0755";
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // Aquí guardarías en tu Settings.json o similar
        Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}