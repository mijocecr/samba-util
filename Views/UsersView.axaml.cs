using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SAMBA_Util.Helpers;


namespace SAMBA_Util.Views;

public partial class UsersView : UserControl
{
    // Evita lanzar dos consultas a pdbedit a la vez (clic + cambio de pestaña).
    private bool _isLoading;
    private int _lastCount;

    public UsersView()
    {
        InitializeComponent();
        // No cargamos aquí: el constructor se ejecuta antes de que el usuario
        // introduzca la contraseña de administrador. La carga real la hace
        // MainWindow al abrir la pestaña Users.
    }

    // ---------------------------------------------------------
    // CARGAR USUARIOS SAMBA
    // ---------------------------------------------------------
    public async Task<(int Count, string? Error)> LoadUsersAsync()
    {
        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
            return (0, "administrator authentication is not available");

        if (_isLoading)
            return (_lastCount, null);

        _isLoading = true;
        try
        {
            // pdbedit puede tardar; lo ejecutamos fuera del hilo de UI para que
            // la ventana no se congele y el diálogo de carga se dibuje.
            var result = await Task.Run(() =>
                ShellHelper.EjecutarComoRoot("pdbedit -L", ShellHelper.LongTimeoutMs));

            if (result.ExitCode != 0)
            {
                // Si la consulta falla no vaciamos la lista: conservamos lo
                // último cargado correctamente para no dejar la vista en blanco.
                return (_lastCount, DescribeLoadError(result));
            }

            var users = SambaUsers.ParseList(result.Stdout);

            await Dispatcher.UIThread.InvokeAsync(() => UsersList.ItemsSource = users);
            _lastCount = users.Count;
            return (users.Count, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[USERS ERROR] {ex}");
            return (_lastCount, ex.Message);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static string DescribeLoadError((int ExitCode, string Stdout, string Stderr) result)
    {
        return result.ExitCode switch
        {
            ShellHelper.ExitAdminPasswordRequired => "administrator authentication is not available",
            ShellHelper.ExitPasswordIncorrect => "sudo authentication failed",
            ShellHelper.ExitTimeout => "the request timed out",
            ShellHelper.ExitStartFailure => "could not start pdbedit",
            _ => string.IsNullOrWhiteSpace(result.Stderr)
                ? $"pdbedit exited with code {result.ExitCode}"
                : result.Stderr.Trim()
        };
    }

    private async Task ReloadUsersAsync()
    {
        var (count, error) = await LoadUsersAsync();

        if (TopLevel.GetTopLevel(this) is MainWindow main)
            main.UpdateStatus(error is null
                ? $"Loaded {count} users."
                : $"Could not load users: {error}.");
    }

  
    // ---------------------------------------------------------
// AÑADIR USUARIO SAMBA (CORREGIDO Y SEGURO)
// ---------------------------------------------------------
    private async void OnAddUser(object? sender, RoutedEventArgs e)
    {
        try
        {
            var username = UsernameBox.Text?.Trim();
            var password = PasswordBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(username))
                return;

            if (string.IsNullOrEmpty(Credenciales.AdminPassword))
                return;

            // 1) Crear usuario UNIX si no existe
            ShellHelper.EjecutarComoRoot(
                $"bash -c \"id -u '{username}' >/dev/null 2>&1 || useradd -M -s $(which nologin || echo /sbin/nologin) '{username}'\""
            );

            // 2) Crear o actualizar usuario Samba con contraseña
            if (!string.IsNullOrWhiteSpace(password))
            {
                // Pasamos la contraseña duplicada con un salto de línea (\n) para cumplir con el prompt dual de smbpasswd (-s)
                var passBytes = System.Text.Encoding.UTF8.GetBytes($"{password}\n{password}\n");
                var passBase64 = System.Convert.ToBase64String(passBytes);

                // Usamos printf y base64 para inyectar correctamente ambos saltos de línea al stdin de smbpasswd
                string cmd = $"bash -c \"printf '%s' '{passBase64}' | base64 -d | smbpasswd -a -s '{username}'\"";
                ShellHelper.EjecutarComoRoot(cmd);
            }
            else
            {
                // Usuario Samba sin contraseña (deshabilitado/guest)
                ShellHelper.EjecutarComoRoot(
                    $"bash -c \"smbpasswd -a -n '{username}'\""
                );
            }

            UsernameBox.Text = "";
            PasswordBox.Text = "";

            await ReloadUsersAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[USERS ERROR] {ex}");
        }
    }
    
    
    
    // ---------------------------------------------------------
    // ELIMINAR USUARIO SAMBA + UNIX (SEGURO)
    // ---------------------------------------------------------
    private async void OnDeleteUser(object? sender, RoutedEventArgs e)
    {
        try
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

                await ReloadUsersAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[USERS ERROR] {ex}");
        }
    }

    // ---------------------------------------------------------
    // EDITAR USUARIO
    // ---------------------------------------------------------
    private async void OnEditUser(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.Tag is string username)
            {
                var parentWindow = TopLevel.GetTopLevel(this) as Window;

                if (parentWindow != null)
                {
                    var win = new EditUserWindow(username);
                    await win.ShowDialog(parentWindow);
                    await ReloadUsersAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[USERS ERROR] {ex}");
        }
    }
}