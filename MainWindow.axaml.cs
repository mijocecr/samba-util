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
        UpdateStatus("Cerratonix  |  https://github.com/mijocec");
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

        var okBtn = new Button { Content = "OK", Width = 80 , HorizontalContentAlignment = HorizontalAlignment.Center};
        var cancelBtn = new Button { Content = "Cancel", Width = 80 , HorizontalContentAlignment = HorizontalAlignment.Center};

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

        // ⭐ EVENTO ENTER EN USERNAME
        userBox.KeyDown += (s, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
                Accept();
        };

        // ⭐ EVENTO ENTER EN PASSWORD
        passBox.KeyDown += (s, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
                Accept();
        };

        dialog.ShowDialog(this);

        return await tcs.Task;
    }


    //----------------------------
    public async void ShowErrorDialog(string title, string message)
    {
        var dialog = new Window
        {
            Width = 400,
            Height = 220,
            Background = new SolidColorBrush(Color.Parse("#1B2838")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = title,
            CanResize = false
        };

        var text = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var okBtn = new Button
        {
            Content = "OK",
            Width = 80,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
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

    
    //----------------------------
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

    
    
    //----------------------------

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
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
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

        win.Show(this); // No ShowDialog → no bloquea el hilo
        return win;
    }


    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        UpdateStatus("Remote Shares");
    }
}
