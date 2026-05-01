using System;
using System.Threading.Tasks;
using Avalonia.Controls;
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

    
    private async void OnStatusTextClicked(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        await SolicitarPassword();

        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
        {
            StatusText.Text = "No admin password provided.";
            return;
        }

        StatusText.Text = "Admin password updated.";
    }

    public void UpdateStatus(string message)
    {
        StatusText.Text = message;
    }

    
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // 1) Solicitar contraseña de administrador
        await SolicitarPassword();

        // 2) Si el usuario cancela → no continuar
        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
        {
            StatusText.Text = "Initialization aborted: no admin password provided.";
            Console.WriteLine("[ERROR] No se ingresó contraseña. Abortando.");
            return;
        }

        // 3) Validar contraseña sin romper la app
        var result = ShellHelper.EjecutarComoRoot("echo OK");

        if (result.ExitCode != 0)
        {
            StatusText.Text = "Incorrect admin password. Samba operations disabled.";
            Console.WriteLine("[SUDO ERROR] " + result.Error);
            return; // No romper la app
        }

        // 4) Cargar shares (tu lógica original)
        SharesViewControl.LoadShares();

        var shares = SambaConfigReader.LoadShares();
        StatusText.Text = $"Loaded {shares.Count} shares from smb.conf";

        foreach (var s in shares)
        {
            Console.WriteLine($"[{s.Name}] path={s.Path} ro={s.ReadOnly} guests={s.AllowGuests}");
        }
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