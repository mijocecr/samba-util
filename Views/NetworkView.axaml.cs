using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Platform;
using SAMBA_Util.Helpers;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace SAMBA_Util.Views;

public partial class NetworkView : UserControl
{
    private bool _isLoadingShares = false;
    private CancellationTokenSource? _currentOperationCts;
    private Guid _activeSessionId = Guid.Empty;
    private Window? _currentLoadingDialog;

    public NetworkView()
    {
        InitializeComponent();
        LoadInterfaces();
        ScanButton.Click += OnScanClicked;

        SetTwoColumnLayout(false);
        Console.WriteLine("[DIAGNOSTIC] NetworkView initialized.");
    }

    private void SetTwoColumnLayout(bool showDevices)
    {
        Console.WriteLine($"[DIAGNOSTIC] SetTwoColumnLayout -> showDevices: {showDevices}");
        if (showDevices)
        {
            DevicesColumn.IsVisible = true;
            ColumnSeparator.Width = 12;
            MainContentGrid.ColumnDefinitions = new ColumnDefinitions("*,12,*");
        }
        else
        {
            DevicesColumn.IsVisible = false;
            ColumnSeparator.Width = 0;
            MainContentGrid.ColumnDefinitions = new ColumnDefinitions("0,0,*");
        }
    }

    private void LoadInterfaces()
    {
        InterfaceSelector.Items.Clear();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;

            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            InterfaceSelector.Items.Add(ni.Name);
        }

        if (InterfaceSelector.Items.Count > 0)
            InterfaceSelector.SelectedIndex = 0;

        Console.WriteLine($"[DIAGNOSTIC] Loaded {InterfaceSelector.Items.Count} network interfaces.");
    }

    private async void OnScanClicked(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("[DIAGNOSTIC] === Scan Network Clicked ===");

        if (_isLoadingShares)
        {
            Console.WriteLine("[DIAGNOSTIC] Scan blocked: _isLoadingShares is true.");
            return;
        }

        _activeSessionId = Guid.NewGuid();
        _currentOperationCts?.Cancel();
        _currentOperationCts = new CancellationTokenSource();
        var token = _currentOperationCts.Token;

        Window? loading = null;

        try
        {
            SetTwoColumnLayout(true);

            var main = TopLevel.GetTopLevel(this) as MainWindow;
            if (main == null)
            {
                Console.WriteLine("[DIAGNOSTIC] ERROR: MainWindow not found.");
                Log("Main window not found.");
                return;
            }

            loading = ShowLoading("Scanning network...");

            Log("Starting network scan...");
            DevicesPanel.DataContext = null;
            DevicesPanel.Children.Clear();
            SharesPanel.DataContext = null;
            SharesPanel.Children.Clear();

            var iface = InterfaceSelector.SelectedItem?.ToString();
            Console.WriteLine($"[DIAGNOSTIC] Selected interface for scan: '{iface}'");

            if (string.IsNullOrWhiteSpace(iface))
            {
                Log("No interface selected.");
                Console.WriteLine("[DIAGNOSTIC] Scan aborted: No interface selected.");
                return;
            }

            var devices = await NetworkScanner.DiscoverAsync(iface, token);
            if (token.IsCancellationRequested)
            {
                Console.WriteLine("[DIAGNOSTIC] Scan task cancelled during DiscoverAsync.");
                return;
            }

            CloseLoading(loading);
            loading = null;

            Console.WriteLine($"[DIAGNOSTIC] Scan finished. Devices found: {devices?.Count ?? 0}");

            if (devices != null && devices.Count > 0)
            {
                foreach (var dev in devices)
                {
                    Console.WriteLine($"[DIAGNOSTIC] Adding card for Device -> Name: '{dev.Name}', IP: '{dev.IP}', OS: '{dev.OS}'");
                    DevicesPanel.Children.Add(CreateDeviceCard(dev));
                }

                Log($"Found {devices.Count} active devices.");
            }
            else
            {
                DevicesPanel.Children.Add(new TextBlock
                {
                    Text = "No devices found on this network segment.",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyle.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(15)
                });
                Log("Scan returned no devices.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIAGNOSTIC ERROR] OnScanClicked Exception: {ex.Message}\n{ex.StackTrace}");

            CloseLoading(loading);
            loading = null;

            try
            {
                var main = TopLevel.GetTopLevel(this) as MainWindow;
                _ = main?.ShowErrorDialog("Scan Error", ex.Message);
            }
            catch { }

            Log("Scan failed with error.");
        }
        finally
        {
            CloseLoading(loading);
        }
    }

    private Control CreateDeviceCard(NetworkDevice dev)
    {
        string iconPath = dev.OS switch
        {
            "Windows"    => "avares://SAMBA-Util/Assets/Icons/resource-windows.jpeg",
            "Linux"      => "avares://SAMBA-Util/Assets/Icons/resource-linux.jpeg",
            "macOS"      => "avares://SAMBA-Util/Assets/Icons/resource-mac.jpeg",
            "BSD"        => "avares://SAMBA-Util/Assets/Icons/resource-bsd.jpeg",
            "Unix"       => "avares://SAMBA-Util/Assets/Icons/resource-unix.jpeg",
            "Router"     => "avares://SAMBA-Util/Assets/Icons/resource-router.jpeg",
            "NAS"        => "avares://SAMBA-Util/Assets/Icons/resource-nas.jpeg",
            "SMB Device" => "avares://SAMBA-Util/Assets/Icons/resource-other.jpeg",
            _            => "avares://SAMBA-Util/Assets/Icons/resource-other.jpeg"
        };

        Image icon;
        try
        {
            var uri = new Uri(iconPath);
            icon = new Image
            {
                Source = new Avalonia.Media.Imaging.Bitmap(AssetLoader.Open(uri)),
                Width = 56,
                Height = 56,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        catch
        {
            icon = new Image { Width = 56, Height = 56 };
        }

        var fgBrush = this.FindResource("ForegroundBrush") as IBrush ?? Brushes.White;
        var fgColor = (fgBrush as ISolidColorBrush)?.Color ?? Colors.White;

        string cleanName = dev.Name;
        if (!string.IsNullOrEmpty(cleanName))
        {
            cleanName = cleanName.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                ? cleanName.Substring(0, cleanName.Length - 6)
                : cleanName;
        }

        var infoPanel = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock 
                { 
                    Text = cleanName, 
                    Foreground = fgBrush, 
                    FontWeight = FontWeight.Bold, 
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock 
                { 
                    Text = $"{dev.IP} • {dev.OS}", 
                    Foreground = new SolidColorBrush(fgColor, 0.7), 
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        var layoutGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };

        layoutGrid.Children.Add(icon);
        Grid.SetColumn(icon, 0);

        layoutGrid.Children.Add(infoPanel);
        Grid.SetColumn(infoPanel, 1);
        infoPanel.Margin = new Thickness(8, 0, 0, 0);

        var border = new Border
        {
            Background = this.FindResource("ControlBackgroundBrush") as IBrush ?? Brushes.Gray,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2, 6, 6, 6),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = new Avalonia.Input.Cursor(StandardCursorType.Hand),
            Child = layoutGrid
        };

        string targetIp = dev.IP;
        string targetName = cleanName;

        // Usamos Tapped en lugar de PointerPressed para evitar colas de clics fantasmas tras cerrar modales
        border.Tapped += async (sender, e) =>
        {
            e.Handled = true;
            await LoadDeviceSharesAsync(targetIp, targetName, fgColor);
        };

        border.ContextMenu = BuildOsContextMenu(dev, border);
        return border;
    }

    private async Task LoadDeviceSharesAsync(string targetIp, string targetName, Color fgColor)
    {
        Guid sessionGuid = Guid.NewGuid();
        _activeSessionId = sessionGuid;

        Console.WriteLine($"[DIAGNOSTIC] === NEW SESSION [{sessionGuid}] -> Target: {targetName} ({targetIp}) ===");

        if (_currentOperationCts != null)
        {
            Console.WriteLine("[DIAGNOSTIC] Cancelling previous ongoing network/share operation.");
            _currentOperationCts.Cancel();
        }
        
        _currentOperationCts = new CancellationTokenSource();
        var token = _currentOperationCts.Token;

        _isLoadingShares = true;
        DevicesPanel.IsEnabled = false;
        Window? loading = null;

        try
        {
            var mainWindow = TopLevel.GetTopLevel(this) as MainWindow;
            if (mainWindow == null) return;

            CredStore.User = null;
            CredStore.Password = null;
            Console.WriteLine("[DIAGNOSTIC] CredStore cleared (User=null, Password=null).");

            SharesPanel.DataContext = null;
            SharesPanel.Children.Clear();

            Log($"Loading shares for device: {targetName} ({targetIp})");

            var loadingText = new TextBlock
            {
                Text = $"Connecting to {targetName} ({targetIp})...",
                Foreground = new SolidColorBrush(fgColor, 0.6),
                FontStyle = FontStyle.Italic,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(15)
            };
            SharesPanel.Children.Add(loadingText);

            loading = ShowLoading($"Connecting to {targetName} ({targetIp})...");

            List<NetworkShare> shares = new();
            try
            {
                shares = await NetworkScanner.GetSharesAsync(targetIp, token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DIAGNOSTIC ERROR] GetSharesAsync exception: {ex.Message}");
            }

            if (_activeSessionId != sessionGuid || token.IsCancellationRequested) return;

            if (shares.Count == 0)
            {
                SharesPanel.DataContext = null;
                SharesPanel.Children.Clear();

                // El diálogo de credenciales es modal: cerramos la carga antes de abrirlo.
                CloseLoading(loading);
                loading = null;

                var cred = await mainWindow.ShowCredentialsDialog();
                
                bool isCanceledOrEmpty = cred == null || 
                                         string.IsNullOrWhiteSpace(cred.Value.user) && string.IsNullOrWhiteSpace(cred.Value.pass);

                if (_activeSessionId != sessionGuid || token.IsCancellationRequested || isCanceledOrEmpty)
                {
                    Console.WriteLine("[DIAGNOSTIC] Dialog was canceled or credentials were left empty. Aborting.");
                    mainWindow.ShowToast("Canceled");
                    Log("Shares loading canceled.");
                    SharesPanel.DataContext = null;
                    SharesPanel.Children.Clear();
                    SharesPanel.Children.Add(new TextBlock
                    {
                        Text = "Operation canceled or credentials required.",
                        Foreground = Brushes.Gray,
                        FontStyle = FontStyle.Italic,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(15)
                    });
                    return;
                }

                CredStore.User = cred.Value.user;
                CredStore.Password = cred.Value.pass;
                Console.WriteLine($"[DIAGNOSTIC] CredStore updated with user: '{CredStore.User}'");

                loading = ShowLoading($"Connecting to {targetName} ({targetIp})...");

                try
                {
                    shares = await NetworkScanner.GetSharesAsync(targetIp, token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DIAGNOSTIC ERROR] GetSharesAsync with credentials error: {ex.Message}");
                }
            }

            if (_activeSessionId != sessionGuid || token.IsCancellationRequested) return;

            SharesPanel.DataContext = null;
            SharesPanel.Children.Clear();

            if (shares.Count == 0)
            {
                SharesPanel.Children.Add(new TextBlock
                {
                    Text = "Unable to enumerate SMB shares (credentials required or access denied).",
                    Foreground = new SolidColorBrush(fgColor, 0.7),
                    FontSize = 13,
                    FontStyle = FontStyle.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(15)
                });
                Log($"No shares visible for {targetName}.");
                return;
            }

            foreach (var share in shares)
                SharesPanel.Children.Add(CreateShareItem(share));

            Log($"Loaded {shares.Count} shares for {targetName}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIAGNOSTIC ERROR] LoadDeviceSharesAsync Exception: {ex.Message}");
        }
        finally
        {
            CloseLoading(loading);
            DevicesPanel.IsEnabled = true;

            if (_activeSessionId == sessionGuid)
            {
                _isLoadingShares = false;
            }
        }
    }
    
    private ContextMenu BuildOsContextMenu(NetworkDevice dev, Border card)
    {
        var menu = new ContextMenu();
        var root = new MenuItem { Header = "Identify the right O.S" };

        var osItems = new List<MenuItem>();

        void AddOsItem(string label)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, __) =>
            {
                Console.WriteLine($"[DIAGNOSTIC] ContextMenu OS override selected for IP {dev.IP} -> {label}");
                OsOverrideManager.SetOverride(dev.IP, label);
                dev.OS = label;

                var parent = DevicesPanel;
                int index = parent.Children.IndexOf(card);
                if (index >= 0)
                {
                    parent.Children.RemoveAt(index);
                    parent.Children.Insert(index, CreateDeviceCard(dev));
                }

                Log($"OS override applied: {dev.IP} → {label}");
            };

            osItems.Add(item);
        }

        AddOsItem("Windows");
        AddOsItem("Linux");
        AddOsItem("macOS");
        AddOsItem("BSD");
        AddOsItem("Unix");
        AddOsItem("NAS");
        AddOsItem("SMB Device");
        AddOsItem("Other");

        root.ItemsSource = osItems;
        menu.ItemsSource = new List<MenuItem> { root };

        return menu;
    }

    private Control CreateShareItem(NetworkShare share)
    {
        var main = TopLevel.GetTopLevel(this) as MainWindow;
        if (main == null)
            return new TextBlock { Text = "Main window not found." };

        var fgBrush = this.FindResource("ForegroundBrush") as IBrush ?? Brushes.White;
        var fgColor = (fgBrush as ISolidColorBrush)?.Color ?? Colors.White;

        var openBtn = new Button
        {
            Content = "Open",
            Width = 64,
            Height = 22,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(8, 0),
            FontSize = 11,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        openBtn.Click += async (_, __) =>
        {
            Console.WriteLine($"[DIAGNOSTIC] Share Open Clicked -> Share Name: '{share.Name}', IP: '{share.IP}', Access: '{share.Access}'");
            Window? loading = null;
            try
            {
                bool isAnonymous = share.Access?.Equals("Anonymous", StringComparison.OrdinalIgnoreCase) ?? true;

                if (!isAnonymous)
                {
                    bool haveStoredCreds =
                        !string.IsNullOrWhiteSpace(CredStore.User) &&
                        CredStore.User != "guest";

                    if (!haveStoredCreds)
                    {
                        Console.WriteLine("[DIAGNOSTIC] Share requires credentials and none stored. Showing credentials dialog...");
                        var cred = await main.ShowCredentialsDialog();
                        if (cred == null)
                        {
                            Console.WriteLine("[DIAGNOSTIC] Share open credentials dialog canceled.");
                            main.ShowToast("Opening canceled");
                            return;
                        }

                        CredStore.User = cred.Value.user;
                        CredStore.Password = cred.Value.pass;
                    }
                }

                loading = ShowLoading($"Opening {share.Name}...");

                // OpenShare puede bloquear varios segundos en Linux; lo ejecutamos fuera del hilo de UI.
                await Task.Run(() => SmbHelper.OpenShare(
                    share.IP,
                    share.Name,
                    isAnonymous ? null : CredStore.User,
                    isAnonymous ? null : CredStore.Password
                ));

                main.ShowToast($"Opening {share.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DIAGNOSTIC ERROR] OpenShare Exception: {ex.Message}");
                CloseLoading(loading);
                loading = null;
                _ = main.ShowErrorDialog("Open failed", ex.Message);
            }
            finally
            {
                CloseLoading(loading);
            }
        };

        var accessText = new TextBlock
        {
            Text = share.Access,
            Foreground = new SolidColorBrush(fgColor, 0.7),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var commentText = new TextBlock
        {
            Text = share.Comment,
            Foreground = new SolidColorBrush(fgColor, 0.5),
            FontSize = 11,
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = !string.IsNullOrWhiteSpace(share.Comment)
        };

        var nameText = new TextBlock
        {
            Text = share.Name,
            Foreground = fgBrush,
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap
        };

        commentText.Margin = new Thickness(0, 4, 0, 0);
        accessText.Margin = new Thickness(0, 4, 0, 0);

        openBtn.HorizontalAlignment = HorizontalAlignment.Right;
        openBtn.VerticalAlignment = VerticalAlignment.Top;
        openBtn.Margin = new Thickness(10, 0, 0, 0);

        var cardGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto")
        };

        cardGrid.Children.Add(nameText);
        Grid.SetColumn(nameText, 0);
        Grid.SetRow(nameText, 0);

        cardGrid.Children.Add(openBtn);
        Grid.SetColumn(openBtn, 1);
        Grid.SetRow(openBtn, 0);

        cardGrid.Children.Add(commentText);
        Grid.SetColumn(commentText, 0);
        Grid.SetColumnSpan(commentText, 2);
        Grid.SetRow(commentText, 1);

        cardGrid.Children.Add(accessText);
        Grid.SetColumn(accessText, 0);
        Grid.SetColumnSpan(accessText, 2);
        Grid.SetRow(accessText, 2);

        bool isAnonymousShare = share.Access?.Equals("Anonymous", StringComparison.OrdinalIgnoreCase) ?? true;
        var borderBrushColor = isAnonymousShare ? Brushes.LimeGreen : Brushes.DodgerBlue;

        var border = new Border
        {
            Background = this.FindResource("ControlBackgroundBrush") as IBrush ?? Brushes.Gray,
            BorderBrush = borderBrushColor,
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 8, 0),
            Child = cardGrid
        };

        return border;
    }

    private async void OnQuickConnectClicked(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("[DIAGNOSTIC] === Quick Connect Clicked ===");

        if (_isLoadingShares) return;

        string ip = TxtQuickIp.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(ip))
        {
            Log("Please enter a valid IP or hostname.");
            Console.WriteLine("[DIAGNOSTIC] Quick connect aborted: Empty IP/hostname.");
            return;
        }

        Guid sessionGuid = Guid.NewGuid();
        _activeSessionId = sessionGuid;

        _currentOperationCts?.Cancel();
        _currentOperationCts = new CancellationTokenSource();
        var token = _currentOperationCts.Token;

        _isLoadingShares = true;
        Window? loading = null;

        try
        {
            SetTwoColumnLayout(false);

            CredStore.User = null;
            CredStore.Password = null;

            Log($"Connecting to {ip}...");
            SharesPanel.DataContext = null;
            SharesPanel.Children.Clear();

            loading = ShowLoading($"Connecting to {ip}...");

            List<NetworkShare> shares = new();
            try
            {
                Console.WriteLine($"[DIAGNOSTIC] QuickConnect GetSharesAsync for IP: {ip} (Attempt 1)");
                shares = await NetworkScanner.GetSharesAsync(ip, token);
                Console.WriteLine($"[DIAGNOSTIC] QuickConnect GetSharesAsync returned {shares.Count} shares for IP: {ip}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DIAGNOSTIC ERROR] QuickConnect GetSharesAsync exception for IP {ip}: {ex.Message}");
            }

            if (_activeSessionId != sessionGuid || token.IsCancellationRequested) return;

            if (shares.Count == 0)
            {
                var mainWindow = TopLevel.GetTopLevel(this) as MainWindow;
                if (mainWindow != null)
                {
                    // El diálogo de credenciales es modal: cerramos la carga antes de abrirlo.
                    CloseLoading(loading);
                    loading = null;

                    Console.WriteLine("[DIAGNOSTIC] QuickConnect showing credentials dialog...");
                    SharesPanel.DataContext = null;
                    SharesPanel.Children.Clear();
                    var cred = await mainWindow.ShowCredentialsDialog();
                    
                    if (_activeSessionId != sessionGuid || token.IsCancellationRequested || cred == null)
                    {
                        Console.WriteLine("[DIAGNOSTIC] QuickConnect credentials dialog canceled or superseded.");
                        mainWindow.ShowToast("Connection canceled");
                        Log("Connection canceled.");
                        SharesPanel.Children.Add(new TextBlock
                        {
                            Text = "Connection canceled or credentials required.",
                            Foreground = Brushes.Gray,
                            FontStyle = FontStyle.Italic,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(15)
                        });
                        return;
                    }

                    CredStore.User = cred.Value.user;
                    CredStore.Password = cred.Value.pass;

                    loading = ShowLoading($"Connecting to {ip}...");

                    try
                    {
                        Console.WriteLine($"[DIAGNOSTIC] QuickConnect GetSharesAsync for IP: {ip} (Attempt 2 with credentials)");
                        shares = await NetworkScanner.GetSharesAsync(ip, token);
                        Console.WriteLine($"[DIAGNOSTIC] QuickConnect Attempt 2 returned {shares.Count} shares for IP: {ip}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DIAGNOSTIC ERROR] QuickConnect Attempt 2 exception: {ex.Message}");
                    }
                }
            }

            if (_activeSessionId != sessionGuid || token.IsCancellationRequested) return;

            SharesPanel.DataContext = null;
            SharesPanel.Children.Clear();

            if (shares.Count == 0)
            {
                SharesPanel.Children.Add(new TextBlock
                {
                    Text = $"No shares found for {ip}.",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyle.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(15)
                });
                Log($"No shares found for {ip}.");
                return;
            }

            foreach (var share in shares)
            {
                SharesPanel.Children.Add(CreateShareItem(share));
            }

            Log($"Connected to {ip} successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIAGNOSTIC ERROR] OnQuickConnectClicked Exception: {ex.Message}\n{ex.StackTrace}");

            CloseLoading(loading);
            loading = null;

            try
            {
                var main = TopLevel.GetTopLevel(this) as MainWindow;
                _ = main?.ShowErrorDialog("Connection Error", ex.Message);
            }
            catch { }

            Log("Quick connect failed with error.");
        }
        finally
        {
            CloseLoading(loading);

            if (_activeSessionId == sessionGuid)
            {
                _isLoadingShares = false;
            }
        }
    }

    private Window? ShowLoading(string message)
    {
        CloseLoading(_currentLoadingDialog);

        var main = TopLevel.GetTopLevel(this) as MainWindow;
        if (main == null)
            return null;

        _currentLoadingDialog = main.ShowLoadingDialog(message);
        return _currentLoadingDialog;
    }

    private void CloseLoading(Window? loading)
    {
        if (loading == null)
            return;

        try
        {
            loading.Close();
        }
        catch
        {
            // La ventana pudo haberse cerrado ya.
        }

        if (ReferenceEquals(_currentLoadingDialog, loading))
            _currentLoadingDialog = null;
    }

    private void Log(string msg)
    {
        Console.WriteLine($"[NetworkView] {msg}");

        if (TopLevel.GetTopLevel(this) is MainWindow main)
        {
            try
            {
                main.UpdateStatus(msg);
            }
            catch { }
        }
    }
}