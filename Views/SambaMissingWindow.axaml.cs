using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SAMBA_Util.Views;

public partial class SambaMissingWindow : Window
{
    public SambaMissingWindow()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
