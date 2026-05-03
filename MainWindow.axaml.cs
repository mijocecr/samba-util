using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SAMBA_Util.Helpers;
using SAMBA_Util.Models;
using SAMBA_Util.ViewModels;
using SAMBA_Util.Views;

namespace SAMBA_Util;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        StatusText.Text = "Initializing...";

        // Pedir contraseña después de mostrar la ventana
        await Task.Delay(200);
        await SolicitarPassword();

        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
        {
            StatusText.Text = "Initialization aborted.";
            return;
        }

        // Validar contraseña
        StatusText.Text = "Validating password...";
        var result = ShellHelper.EjecutarComoRoot("echo OK");

        // 🔥 Manejo de contraseña incorrecta
        if (result.ExitCode == 1001)
        {
            StatusText.Text = "Incorrect admin password.";
            await MostrarPasswordIncorrecta();
            await SolicitarPassword();
            return;
        }

        if (result.ExitCode != 0)
        {
            StatusText.Text = "Admin password validation failed.";
            return;
        }

        // Cargar shares
        StatusText.Text = "Loading Samba configuration...";

        var shares = await Task.Run(() =>
        {
            return SambaConfigReader.LoadShares();
        });

        SharesViewControl.SetShares(shares);
        StatusText.Text = $"Loaded {shares.Count} shares.";
    }

    // ---------------------------
    // EVENTOS
    // ---------------------------

    private async void OnOpenConfig(object? sender, RoutedEventArgs e)
    {
        var win = new ConfigWindow();

        // 🔥 Suscribirse al evento para refrescar vistas
        win.ConfigSaved += () =>
        {
            int count = SharesViewControl.LoadShares();
            UpdateStatus($"Configuration updated. Reloaded {count} shares.");
        };

        await win.ShowDialog(this);
    }

    private void OnStatusRefreshClicked(object? sender, PointerPressedEventArgs e)
    {
        StatusViewControl?.RefreshStatus();
        UpdateStatus("Status refreshed.");
    }

    private void OnUsersTabClicked(object? sender, PointerPressedEventArgs e)
    {
        int count = UsersViewControl.LoadUsers();
        UpdateStatus($"Loaded {count} users.");
    }

    private void OnSharesTextClicked(object? sender, PointerPressedEventArgs e)
    {
        int count = SharesViewControl.LoadShares();
        UpdateStatus($"Loaded {count} shares.");
    }

    private void OnStatusTextClicked(object? sender, PointerPressedEventArgs e)
    {
        UpdateStatus("Admin password OK.");
    }

    // ---------------------------
    // UTILIDADES
    // ---------------------------

    public void UpdateStatus(string message)
    {
        StatusText.Text = message;
    }

    private async Task SolicitarPassword()
    {
        var dialog = new PasswordDialog
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        dialog.DataContext = new PasswordDialogViewModel(pass =>
        {
            Credenciales.AdminPassword = pass;
            dialog.Close();
        });

        await dialog.ShowDialog(this);
    }

    private async Task MostrarPasswordIncorrecta()
    {
        var msg = new Window
        {
            Width = 350,
            Height = 150,
            Title = "Authentication Error",
            Content = new TextBlock
            {
                Text = "Incorrect administrator password.",
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            },
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        await msg.ShowDialog(this);
    }
}
