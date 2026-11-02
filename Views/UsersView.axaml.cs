using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using SAMBA_Util.Helpers;


namespace SAMBA_Util.Views;

public partial class UsersView : UserControl
{
    public UsersView()
    {
        InitializeComponent();
        LoadUsers();
    }

    // ---------------------------------------------------------
    // CARGAR USUARIOS SAMBA
    // ---------------------------------------------------------
    public int LoadUsers()
    {
        if (string.IsNullOrEmpty(Credenciales.AdminPassword))
            return 0;

        var (exit, output, error) = ShellHelper.EjecutarComoRoot(
            "bash -c \"pdbedit -L\""
        );

        var users = new List<string>();

        foreach (var line in output.Split('\n'))
        {
            if (line.Contains(":"))
            {
                var name = line.Split(':')[0].Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    users.Add(name);
            }
        }

        UsersList.ItemsSource = users;
        return users.Count;
    }

    // ---------------------------------------------------------
    // AÑADIR USUARIO SAMBA (VERSIÓN PROFESIONAL Y COMPATIBLE)
    // ---------------------------------------------------------
    private void OnAddUser(object? sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text?.Trim();
        var password = PasswordBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(username))
            return;

        if (string.IsNullOrEmpty(Credenciales.AdminPassword))
            return;

        // 1) Crear usuario UNIX si no existe (usando 'nologin' genérico compatible con Arch y Debian)
        ShellHelper.EjecutarComoRoot(
            $"bash -c \"id -u '{username}' >/dev/null 2>&1 || useradd -M -s $(which nologin || echo /sbin/nologin) '{username}'\""
        );

        // 2) Crear usuario Samba con contraseña segura (protegiendo contra caracteres especiales)
        if (!string.IsNullOrWhiteSpace(password))
        {
            // Usamos base64 para evitar problemas con comillas o caracteres especiales de bash en la contraseña
            var passBytes = System.Text.Encoding.UTF8.GetBytes(password);
            var passBase64 = System.Convert.ToBase64String(passBytes);

            string cmd = $"bash -c \"echo '{passBase64}' | base64 -d | smbpasswd -a -s '{username}'\"";
            ShellHelper.EjecutarComoRoot(cmd);
        }
        else
        {
            // Usuario Samba sin contraseña
            ShellHelper.EjecutarComoRoot(
                $"bash -c \"smbpasswd -a -n '{username}'\""
            );
        }

        UsernameBox.Text = "";
        PasswordBox.Text = "";

        LoadUsers();
    }

    // ---------------------------------------------------------
    // ELIMINAR USUARIO SAMBA + UNIX (SEGURO)
    // ---------------------------------------------------------
    private void OnDeleteUser(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string username)
        {
            if (string.IsNullOrEmpty(Credenciales.AdminPassword))
                return;

            // 1) Eliminar usuario Samba
            ShellHelper.EjecutarComoRoot(
                $"bash -c \"smbpasswd -x '{username}' 2>/dev/null || true\""
            );

            // 2) Comprobar si es usuario UNIX válido
            var (exitUid, uidOutput, _) = ShellHelper.EjecutarComoRoot(
                $"bash -c \"id -u '{username}' 2>/dev/null\""
            );

            if (exitUid == 0)
            {
                if (int.TryParse(uidOutput.Trim(), out int uid) && uid >= 1000)
                {
                    // 3) Eliminar usuario UNIX y su home
                    ShellHelper.EjecutarComoRoot(
                        $"bash -c \"userdel -r '{username}' 2>/dev/null || true\""
                    );
                }
            }

            LoadUsers();
        }
    }

    // ---------------------------------------------------------
    // EDITAR USUARIO
    // ---------------------------------------------------------
    private void OnEditUser(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string username)
        {
            var parentWindow = TopLevel.GetTopLevel(this) as Window;

            if (parentWindow != null)
            {
                var win = new EditUserWindow(username);
                win.ShowDialog(parentWindow);
            }
        }
    }
}