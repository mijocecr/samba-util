using Avalonia.Controls;
using Avalonia.Interactivity;
using SAMBA_Util.ViewModels;

namespace SAMBA_Util.Views;

public partial class PasswordDialog : Window
{
    public PasswordDialog()
    {
        InitializeComponent();
        base.WindowStartupLocation= WindowStartupLocation.CenterScreen;
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
        Close();
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PasswordDialogViewModel vm)
        {
            vm.OnPasswordEntered(PwdBox.Text ?? "");
        }
    }

}