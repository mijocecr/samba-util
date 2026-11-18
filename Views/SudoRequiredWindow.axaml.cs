using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SAMBA_Util.Views;

public partial class SudoRequiredWindow : Window
{
    // Acción a ejecutar si el usuario decide continuar sin privilegios. Puede ser
    // null cuando la ventana se crea desde el diseñador de XAML.
    private readonly Action? _onContinue;

    public SudoRequiredWindow()
    {
        InitializeComponent();
    }

    public SudoRequiredWindow(Action onContinue)
    {
        InitializeComponent();
        _onContinue = onContinue;
    }

    private void OnContinue(object? sender, RoutedEventArgs e)
    {
        _onContinue?.Invoke();
        Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
