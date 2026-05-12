using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using SAMBA_Util.Helpers;
using SambaUtil.Views;

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
    // AÑADIR USUARIO SAMBA (VERSIÓN PROFESIONAL)
    // ---------------------------------------------------------
    private void OnAddUser(object? sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text?.Trim();
        var password = PasswordBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(username))
            return;

        if (string.IsNullOrEmpty(Credenciales.AdminPassword))
            return;

        // 1) Crear usuario UNIX si no existe
        ShellHelper.EjecutarComoRoot(
            $"bash -c \"id -u '{username}' >/dev/null 2>&1 || useradd -M -s /usr/sbin/nologin '{username}'\""
        );

        // 2) Crear usuario Samba con contraseña segura (heredoc)
        if (!string.IsNullOrWhiteSpace(password))
        {
            string cmd =
                $"bash -c \"smbpasswd -a '{username}' <<EOF\n{password}\n{password}\nEOF\"";

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
    // EDITAR USUARIO (SIN CAMBIOS)
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
