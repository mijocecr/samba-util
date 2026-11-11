using Avalonia.Controls;
using Avalonia.Interactivity;
using SAMBA_Util.Helpers;
using System;
using System.Text;

namespace SAMBA_Util.Views;

public partial class EditUserWindow : Window
{
    private readonly string _username;

    public EditUserWindow(string username)
    {
        InitializeComponent();
        _username = username;
        base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UsernameText.Text = username;
        LoadUserInfo(username);
    }

    private void LoadUserInfo(string username)
    {
        // Obtener info Samba
        var (_, sambaInfo, _) = ShellHelper.EjecutarComoRoot(
            $"bash -c \"pdbedit -L -v {username}\"", ShellHelper.LongTimeoutMs
        );

        SambaUidText.Text = ExtraerUidSamba(sambaInfo);
        EnableCheck.IsChecked = ExtraerEstadoSamba(sambaInfo) == "Enabled";

        // Obtener UID UNIX
        var (exitUnix, unixUid, _) = ShellHelper.EjecutarComoRoot(
            $"bash -c \"id -u {username} 2>/dev/null\""
        );

        UnixUidText.Text = exitUnix == 0 ? unixUid.Trim() : "No existe";
    }

    private string ExtraerUidSamba(string info)
    {
        foreach (var line in info.Split('\n'))
            if (line.Contains("User SID"))
                return line.Split(':')[1].Trim();

        return "Unknown";
    }

    private string ExtraerEstadoSamba(string info)
    {
        foreach (var line in info.Split('\n'))
            if (line.Contains("Account Flags"))
                return line.Contains("[D]") ? "Disabled" : "Enabled";

        return "Unknown";
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // Cambiar contraseña de forma segura usando Base64
        var newPass = NewPasswordBox.Text;
        if (!string.IsNullOrWhiteSpace(newPass))
        {
            var passBytes = Encoding.UTF8.GetBytes(newPass.ToCharArray());
            var passBase64 = Convert.ToBase64String(passBytes);

            // smbpasswd pide la contraseña dos veces (nueva y confirmación)
            string cmd = $"bash -c \"(echo '{passBase64}' | base64 -d; echo '{passBase64}' | base64 -d) | smbpasswd '{_username}'\"";
            ShellHelper.EjecutarComoRoot(cmd);
        }

        // Cambiar estado (Habilitar / Deshabilitar)
        if (EnableCheck.IsChecked == true)
            ShellHelper.EjecutarComoRoot($"bash -c \"smbpasswd -e '{_username}'\"");
        else
            ShellHelper.EjecutarComoRoot($"bash -c \"smbpasswd -d '{_username}'\"");

        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}