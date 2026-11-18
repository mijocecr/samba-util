using Avalonia.Controls;
using Avalonia.Interactivity;
using SAMBA_Util.Helpers;

namespace SAMBA_Util.Views;

public partial class EditUserWindow : Window
{
    private readonly string _username;

    // Constructor sin parámetros requerido por el cargador de Avalonia/designer.
    public EditUserWindow() : this(string.Empty, loadUserInfo: false)
    {
    }

    public EditUserWindow(string username) : this(username, loadUserInfo: true)
    {
    }

    private EditUserWindow(string username, bool loadUserInfo)
    {
        InitializeComponent();
        _username = username;
        base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UsernameText.Text = username;

        if (loadUserInfo)
            LoadUserInfo(username);
    }

    private void LoadUserInfo(string username)
    {
        var quotedUser = ShellHelper.Quote(username);

        // Obtener info Samba
        var (_, sambaInfo, _) = ShellHelper.EjecutarComoRoot(
            $"pdbedit -L -v {quotedUser}", ShellHelper.LongTimeoutMs
        );

        SambaUidText.Text = ExtraerUidSamba(sambaInfo);
        EnableCheck.IsChecked = ExtraerEstadoSamba(sambaInfo) == "Enabled";

        // Obtener UID UNIX
        var (exitUnix, unixUid, _) = ShellHelper.EjecutarComoRoot(
            $"id -u -- {quotedUser} 2>/dev/null"
        );

        UnixUidText.Text = exitUnix == 0 ? unixUid.Trim() : "Does not exist";
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
        {
            if (!line.Contains("Account Flags"))
                continue;

            // pdbedit muestra las banderas con relleno, p. ej. "[DU         ]".
            // Hay que leer el contenido entre corchetes y buscar la 'D'.
            int open = line.IndexOf('[');
            int close = line.IndexOf(']');
            string flags = (open >= 0 && close > open)
                ? line[(open + 1)..close]
                : line;

            return flags.Contains('D') ? "Disabled" : "Enabled";
        }

        return "Unknown";
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var quotedUser = ShellHelper.Quote(_username);

        // Cambiar contraseña: smbpasswd -s lee la clave (dos veces) por stdin,
        // así no aparece en la línea de comandos ni en los logs.
        var newPass = NewPasswordBox.Text;
        if (!string.IsNullOrWhiteSpace(newPass))
        {
            ShellHelper.EjecutarComoRootConEntrada(
                $"smbpasswd -s {quotedUser}", $"{newPass}\n{newPass}\n");
        }

        // Cambiar estado (Habilitar / Deshabilitar)
        if (EnableCheck.IsChecked == true)
            ShellHelper.EjecutarComoRoot($"smbpasswd -e {quotedUser}");
        else
            ShellHelper.EjecutarComoRoot($"smbpasswd -d {quotedUser}");

        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}