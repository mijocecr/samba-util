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
    private const int StatusTabIndex = 0;
    private const int UsersTabIndex = 2;

    private bool _isLoaded = false;
    private bool _isRefreshingStatus;
    private bool _isRefreshingUsers;

    public MainWindow()
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        try
        {
            await InitializeAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INIT ERROR] {ex}");
            try { StatusText.Text = "Initialization failed."; } catch { }
            try { await ShowErrorDialog("Initialization Error", ex.Message); } catch { }
        }
    }

    private async Task InitializeAsync()
    {
        StatusText.Text = "Initializing...";
        await Task.Delay(200);

        const int maxPasswordAttempts = 3;
        var authenticated = false;
        for (var attempt = 1; attempt <= maxPasswordAttempts; attempt++)
        {
            await SolicitarPassword();

            if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
            {
                Credenciales.AdminPassword = "";
                StatusText.Text = "Initialization aborted.";
                return;
            }

            StatusText.Text = "Validating password...";
            var result = ShellHelper.EjecutarComoRoot("echo OK");

            if (result.ExitCode == 1001)
            {
                Credenciales.AdminPassword = "";
                if (attempt < maxPasswordAttempts)
                {
                    StatusText.Text = $"Incorrect admin password. Attempt {attempt} of {maxPasswordAttempts}.";
                    await MostrarPasswordIncorrecta();
                }
                continue;
            }

            if (result.ExitCode != 0)
            {
                Credenciales.AdminPassword = "";
                StatusText.Text = "Admin password validation failed.";
                return;
            }

            authenticated = true;
            break;
        }

        if (!authenticated)
        {
            Credenciales.AdminPassword = "";
            StatusText.Text = $"Authentication failed after {maxPasswordAttempts} attempts.";
            return;
        }

        // MOSTRAR DIÁLOGO DE CARGA MIENTRAS SE PROCESA EL ENTORNO
        var loadingWindow = ShowLoadingDialog("Configuring environment and loading Samba...");

        try
        {
            // 1. Inicialización en segundo plano
            await SmbInitializer.InitializeEnvironmentAsync();

            // 2. Cargar shares de forma asíncrona
            var shares = await Task.Run(() => SambaConfigReader.LoadShares());

            // 3. Actualizar datos en la vista principal
            SharesViewControl.SetShares(shares);
            StatusText.Text = $"Loaded {shares.Count} shares.";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INIT ERROR] No se pudo inicializar Samba: {ex}");
            StatusText.Text = "Samba initialization failed.";
            await ShowErrorDialog("Samba initialization failed", ex.Message);
            return;
        }
        finally
        {
            // CERRAR EL DIÁLOGO DE CARGA DE FORMA SEGURA
            Dispatcher.UIThread.Post(() =>
            {
                loadingWindow.Close();
            });
        }

        // Seleccionar la pestaña de estado y refrescar con su propio diálogo de carga
        MainTabs.SelectedIndex = 0;
        await RefreshStatusSafelyAsync();
        _isLoaded = true;
    }
   
    // ---------------------------
    // EVENTOS
    // ---------------------------

    private async void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || StatusViewControl == null)
            return;

        if (MainTabs.SelectedIndex == StatusTabIndex)
        {
            await RefreshStatusSafelyAsync();
        }
        else if (MainTabs.SelectedIndex == UsersTabIndex)
        {
            // Cargamos al cambiar de pestaña, no solo al pulsar el texto del
            // encabezado: así funciona también con teclado o clic en el borde.
            await RefreshUsersSafelyAsync();
        }
    }

    private async void OnOpenConfig(object? sender, RoutedEventArgs e)
    {
        try
        {
            var win = new ConfigWindow();

            win.ConfigSaved += async () =>
            {
                int count = SharesViewControl.LoadShares();
                await RefreshStatusSafelyAsync();
                UpdateStatus($"Configuration updated. Reloaded {count} shares.");
            };

            await win.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CONFIG ERROR] {ex}");
            await ShowErrorDialog("Configuration Error", ex.Message);
        }
    }

    private async void OnStatusRefreshClicked(object? sender, PointerPressedEventArgs e)
    {
        await RefreshStatusSafelyAsync();
        UpdateStatus("Status refreshed.");
    }

    private async void OnUsersTabClicked(object? sender, PointerPressedEventArgs e)
    {
        // Si la pestaña aún no está activa, la carga la hará OnTabChanged.
        // Si ya lo está, el clic actúa como recarga manual.
        if (MainTabs.SelectedIndex != UsersTabIndex)
            return;

        await RefreshUsersSafelyAsync();
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
    // UTILIDADES DE REFRECO Y CARGA
    // ---------------------------

    private async Task RefreshStatusSafelyAsync()
    {
        if (_isRefreshingStatus)
            return;

        _isRefreshingStatus = true;
        Window? loadingWindow = null;
        try
        {
            loadingWindow = ShowLoadingDialog("Refreshing status...");

            // Breve respiro para asegurar que el diálogo de carga se renderice visualmente
            await Task.Delay(50);

            if (StatusViewControl != null)
                await StatusViewControl.RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STATUS ERROR] {ex}");
            UpdateStatus("Status refresh failed.");
        }
        finally
        {
            _isRefreshingStatus = false;
            if (loadingWindow != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    loadingWindow.Close();
                });
            }
        }
    }

    private async Task RefreshUsersSafelyAsync()
    {
        if (_isRefreshingUsers)
            return;

        _isRefreshingUsers = true;
        Window? loadingWindow = null;
        try
        {
            loadingWindow = ShowLoadingDialog("Loading Samba users...");

            // Breve respiro para asegurar que el diálogo de carga se renderice.
            await Task.Delay(50);

            if (UsersViewControl == null)
                return;

            var (count, error) = await UsersViewControl.LoadUsersAsync();
            UpdateStatus(error is null
                ? $"Loaded {count} users."
                : $"Could not load users: {error}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[USERS ERROR] {ex}");
            UpdateStatus("Could not load users.");
        }
        finally
        {
            _isRefreshingUsers = false;
            if (loadingWindow != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    loadingWindow.Close();
                });
            }
        }
    }

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

        _ = dialog.ShowDialog(this);

        return await tcs.Task;
    }

    public async Task ShowErrorDialog(string title, string message)
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
        var foreground = Application.Current?.FindResource("ForegroundBrush") as IBrush;
        var background = Application.Current?.FindResource("BackgroundBrush") as IBrush;
        var controlBackground = Application.Current?.FindResource("ControlBackgroundBrush") as IBrush;
        var borderBrush = Application.Current?.FindResource("BorderBrush") as IBrush;

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
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 16,
                    Foreground = foreground,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    MaxWidth = 300
                },
                progress
            }
        };

        var border = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(10),
            Background = controlBackground,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Child = panel
        };

        var win = new Window
        {
            Width = 350,
            Height = 150,
            Title = "Loading",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = background,
            Foreground = foreground,
            Content = border
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