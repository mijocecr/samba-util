using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.ApplicationLifetimes;
using SAMBA_Util.ViewModels;

namespace SAMBA_Util.Views;

public partial class PasswordDialog : Window
{
    private bool _shutdownOnClose = true; // por defecto, cerrar app si se cierra la ventana

    public PasswordDialog()
    {
        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        this.Closed += (s, e) =>
        {
            if (_shutdownOnClose)
            {
                var lifetime = Avalonia.Application.Current?.ApplicationLifetime
                    as IClassicDesktopStyleApplicationLifetime;

                lifetime?.Shutdown();
            }
        };
    }
    
    private void OnPasswordKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            OnAccept(sender, e);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        // Cancelar = cerrar aplicación
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;

        lifetime?.Shutdown();
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        string pass = PwdBox.Text ?? "";

        // Caso 1: password vacío → mostrar mensaje y NO cerrar nada
        if (string.IsNullOrWhiteSpace(pass))
        {
            EmptyPasswordMessage.IsVisible = true; // ← etiqueta que ya tienes en XAML
            return;
        }

        // Caso 2: password correcto → cerrar diálogo sin cerrar app
        if (DataContext is PasswordDialogViewModel vm)
        {
            _shutdownOnClose = false; // evita cerrar la app
            vm.OnPasswordEntered(pass);
        }
    }
}