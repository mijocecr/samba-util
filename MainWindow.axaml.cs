using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        StatusText.Text = "Initializing...";
        await Task.Delay(200);

        // Bucle de validación de contraseña
        while (true)
        {
            await SolicitarPassword();

            if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
            {
                StatusText.Text = "Initialization aborted.";
                return;
            }

            StatusText.Text = "Validating password...";
            var result = ShellHelper.EjecutarComoRoot("echo OK");

            if (result.ExitCode == 1001)
            {
                StatusText.Text = "Incorrect admin password.";
                await MostrarPasswordIncorrecta();
                continue; // vuelve a pedir contraseña
            }

            if (result.ExitCode != 0)
            {
                StatusText.Text = "Admin password validation failed.";
                return;
            }

            break; // contraseña correcta
        }

        // Cargar shares
        StatusText.Text = "Loading Samba configuration...";

        var shares = await Task.Run(() => SambaConfigReader.LoadShares());

        SharesViewControl.SetShares(shares);
        StatusText.Text = $"Loaded {shares.Count} shares.";
    }

    // ---------------------------
    // EVENTOS
    // ---------------------------

    private async void OnOpenConfig(object? sender, RoutedEventArgs e)
    {
        var win = new ConfigWindow();

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
        UpdateStatus("Cerratonix  |  https://github.com/mijocecr");
    }

    // ---------------------------
    // UTILIDADES
    // ---------------------------

    public async Task<(string user, string pass)?> ShowCredentialsDialog()
    {
        var dialog = new Window
        {
            Width = 350,
            Height = 200,
            Background = new SolidColorBrush(Color.Parse("#1B2838")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = "Credentials Required",
            CanResize = false
        };

        var userBox = new TextBox { Watermark = "Username" };
        var passBox = new TextBox { Watermark = "Password", PasswordChar = '•' };

        var okBtn = new Button { Content = "OK", Width = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancelBtn = new Button { Content = "Cancel", Width = 80, HorizontalContentAlignment = HorizontalAlignment.Center };

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Enter credentials", Foreground = Brushes.White },
                userBox,
                passBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children = { okBtn, cancelBtn }
                }
            }
        };

        dialog.Content = panel;

        TaskCompletionSource<(string, string)?> tcs = new();

        void Accept()
        {
            tcs.SetResult((userBox.Text ?? "", passBox.Text ?? ""));
            dialog.Close();
        }

        okBtn.Click += (_, __) => Accept();
        cancelBtn.Click += (_, __) =>
        {
            tcs.SetResult(null);
            dialog.Close();
        };

        userBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
                Accept();
        };

        passBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
                Accept();
        };

        dialog.ShowDialog(this);

        return await tcs.Task;
    }

    public async void ShowErrorDialog(string title, string message)
    {
        var dialog = new Window
        {
            Width = 360,
            Height = 200,
            Background = new SolidColorBrush(Color.Parse("#1B2838")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = title,
            CanResize = false
        };

        var text = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var okBtn = new Button
        {
            Content = "OK",
            Width = 80,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        okBtn.Click += (_, __) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    Foreground = Brushes.White,
                    FontSize = 18,
                    Margin = new Thickness(0,0,0,10)
                },
                text,
                okBtn
            }
        };

        await dialog.ShowDialog(this);
    }

    public void ShowToast(string message)
    {
        var toast = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2A475E")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Child = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White
            }
        };

        ToastLayer.Children.Add(toast);

        Task.Run(async () =>
        {
            await Task.Delay(3000);
            Dispatcher.UIThread.Post(() =>
            {
                ToastLayer.Children.Remove(toast);
            });
        });
    }

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
        var dialog = new Window
        {
            Width = 360,
            Height = 160,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = "Authentication Error",
            Background = (IBrush)Application.Current!.FindResource("BackgroundBrush")!,
            Foreground = (IBrush)Application.Current!.FindResource("ForegroundBrush")!
        };

        var border = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(10),
            Background = (IBrush)Application.Current!.FindResource("ControlBackgroundBrush")!,
            BorderBrush = (IBrush)Application.Current!.FindResource("BorderBrush")!,
            BorderThickness = new Thickness(1),

            // ⭐ Avalonia 11: DropShadowEffect está en Avalonia.Media
            Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                
                Color = Colors.Black,
                Opacity = 0.33
            }
        };

        var stack = new StackPanel { Spacing = 14 };

        var textBlock = new TextBlock
        {
            Text = "Incorrect administrator password.",
            Foreground = (IBrush)Application.Current!.FindResource("ForegroundBrush")!,
            TextWrapping = TextWrapping.Wrap
        };

        var okBtn = new Button
        {
            Content = "OK",
            Width = 90,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment =  VerticalAlignment.Center,
            HorizontalAlignment =  HorizontalAlignment.Center,
            Classes = { "AccentButton" }
        };

        okBtn.Click += (_, __) => dialog.Close();

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 10,
            Children = { okBtn }
        };

        stack.Children.Add(textBlock);
        stack.Children.Add(buttonPanel);

        border.Child = stack;
        dialog.Content = border;

        await dialog.ShowDialog(this);
    }


    public Window ShowLoadingDialog(string message)
    {
        var progress = new ProgressBar
        {
            IsIndeterminate = true,
            Width = 250,
            Height = 20,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 16
                },
                progress
            }
        };

        var win = new Window
        {
            Width = 350,
            Height = 150,
            Title = "Loading",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = panel
        };

        win.Show(this);
        return win;
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        UpdateStatus("Remote Shares");
    }

    private void onLogsTabClick(object? sender, PointerPressedEventArgs e)
    {
        UpdateStatus("Latest Samba logs");
    }
}
