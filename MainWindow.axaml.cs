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
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // 1) Mostrar ventana inmediatamente
        StatusText.Text = "Initializing...";

        // 2) Pedir contraseña después de que la ventana ya esté visible
        await Task.Delay(200);
        await SolicitarPassword();

        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
        {
            StatusText.Text = "Initialization aborted.";
            return;
        }

        // 3) Validar contraseña (rápido)
        StatusText.Text = "Validating password...";
        var result = ShellHelper.EjecutarComoRoot("echo OK");

        if (result.ExitCode != 0)
        {
            StatusText.Text = "Incorrect admin password.";
            return;
        }

        // 4) Cargar shares en segundo plano (solo una vez)
        StatusText.Text = "Loading Samba configuration...";

        var shares = await Task.Run(() =>
        {
            return SambaConfigReader.LoadShares();
        });

        // 5) Actualizar UI
        SharesViewControl.SetShares(shares);
        StatusText.Text = $"Loaded {shares.Count} shares.";
    }

    // ---------------------------
    // EVENTOS
    // ---------------------------

    private void OnOpenConfig(object? sender, RoutedEventArgs e)
    {
        var win = new ConfigWindow();
        win.ShowDialog(this);
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
}
