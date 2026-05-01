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

    private void OnAddUser(object? sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text?.Trim();
        var password = PasswordBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(username))
            return;

        if (string.IsNullOrEmpty(Credenciales.AdminPassword))
            return;

        // 🔥 NECESARIO: Samba NO puede crear usuario si no existe en UNIX
        ShellHelper.EjecutarComoRoot(
            $"bash -c \"id -u {username} >/dev/null 2>&1 || useradd {username}\""
        );

        if (!string.IsNullOrWhiteSpace(password))
        {
            // 🔥 ESTA ES LA CLAVE: usar bash -c + printf para que sudo NO toque stdin
            var cmd =
                $"bash -c \"printf \\\"{password}\\n{password}\\n\\\" | smbpasswd -a {username}\"";

            ShellHelper.EjecutarComoRoot(cmd);
        }
        else
        {
            ShellHelper.EjecutarComoRoot(
                $"bash -c \"smbpasswd -a -n {username}\""
            );
        }

        UsernameBox.Text = "";
        PasswordBox.Text = "";

        LoadUsers();
    }
/*
    private void OnDeleteUser(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string username)
        {
            if (string.IsNullOrEmpty(Credenciales.AdminPassword))
                return;

            ShellHelper.EjecutarComoRoot(
                $"bash -c \"smbpasswd -x {username}\""
            );

            LoadUsers();
        }
    }*/


    private void OnDeleteUser(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string username)
        {
            if (string.IsNullOrEmpty(Credenciales.AdminPassword))
                return;

            // 1) Eliminar usuario Samba (si existe)
            ShellHelper.EjecutarComoRoot(
                $"bash -c \"smbpasswd -x {username} 2>/dev/null || true\""
            );

            // 2) Comprobar si es usuario UNIX con UID >= 1000
            var (exitUid, uidOutput, _) = ShellHelper.EjecutarComoRoot(
                $"bash -c \"id -u {username} 2>/dev/null\""
            );

            if (exitUid == 0)
            {
                if (int.TryParse(uidOutput.Trim(), out int uid) && uid >= 1000)
                {
                    // 3) Eliminar usuario UNIX y su home
                    ShellHelper.EjecutarComoRoot(
                        $"bash -c \"userdel -r {username} 2>/dev/null || true\""
                    );
                }
            }

            LoadUsers();
        }
    }



    private void OnEditUser(object? sender, RoutedEventArgs e)
    {
        // Pendiente
    }
}
