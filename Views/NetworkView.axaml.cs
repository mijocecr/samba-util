using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Platform;
using SAMBA_Util.Helpers;

using Avalonia.Controls.Primitives; // ← IMPORTANTE


namespace SAMBA_Util.Views
{
    public partial class NetworkView : UserControl
    {
        public NetworkView()
        {
            InitializeComponent();
            LoadInterfaces();
            ScanButton.Click += OnScanClicked;
        }

        // ---------------------------------------------------------
        // CARGAR INTERFACES EN EL COMBOBOX
        // ---------------------------------------------------------
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
        }

        // ---------------------------------------------------------
        // SCAN
        // ---------------------------------------------------------
        private async void OnScanClicked(object? sender, RoutedEventArgs e)
        {
            var main = (MainWindow)TopLevel.GetTopLevel(this);

            var loading = main.ShowLoadingDialog("Scanning network...");

            Log("Starting network scan...");
            StatusText.Text = "Scanning...";

            DevicesPanel.Children.Clear();
            SharesPanel.Children.Clear();

            var iface = InterfaceSelector.SelectedItem?.ToString();

            if (string.IsNullOrWhiteSpace(iface))
            {
                loading.Close();
                Log("No interface selected.");
                return;
            }

            var devices = await NetworkScanner.DiscoverAsync(iface);

            foreach (var dev in devices)
                dev.OS = await NetworkScanner.DetectOSAsync(dev.IP, dev.Name);

            loading.Close();

            Log($"Scan finished. Devices found: {devices.Count}");

            foreach (var dev in devices)
                DevicesPanel.Children.Add(CreateDeviceCard(dev));

            StatusText.Text = $"Found {devices.Count} devices";
        }

        // ---------------------------------------------------------
        // TARJETA DE DISPOSITIVO
        // ---------------------------------------------------------
        private Control CreateDeviceCard(NetworkDevice dev)
        {
            Log($"Selecting icon for device: {dev.Name} ({dev.OS})");

            string iconPath = dev.OS switch
            {
                "Windows" => "avares://SAMBA-Util/Assets/Icons/resource-windows.jpeg",
                "Linux"   => "avares://SAMBA-Util/Assets/Icons/resource-linux.jpeg",
                "macOS"   => "avares://SAMBA-Util/Assets/Icons/resource-mac.jpeg",
                "BSD"     => "avares://SAMBA-Util/Assets/Icons/resource-bsd.jpeg",
                "Unix"    => "avares://SAMBA-Util/Assets/Icons/resource-unix.jpeg",
                "Router"  => "avares://SAMBA-Util/Assets/Icons/resource-router.jpeg",
                _         => "avares://SAMBA-Util/Assets/Icons/resource-other.jpeg"
            };

            Image icon;

            try
            {
                var uri = new Uri(iconPath);
                icon = new Image
                {
                    Source = new Avalonia.Media.Imaging.Bitmap(AssetLoader.Open(uri)),
                    Width = 128,
                    Height = 128,
                    Stretch = Avalonia.Media.Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            catch
            {
                icon = new Image { Width = 128, Height = 128 };
            }

            var leftPanel = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                Margin = new Thickness(0, 0, 10, 0)
            };

            leftPanel.Children.Add(new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock
                    {
                        Text = dev.Name,
                        Foreground = Brushes.White,
                        FontSize = 18
                    },
                    new TextBlock
                    {
                        Text = dev.IP,
                        Foreground = Brushes.Gray,
                        FontSize = 13
                    },
                    new TextBlock
                    {
                        Text = $"OS: {dev.OS}",
                        Foreground = Brushes.LightGray,
                        FontSize = 12
                    }
                }
            });

            var btn = new Button
            {
                Content = "Show Shares",
                Width = 120,
                Margin = new Thickness(0, 15, 0, 0)
            };

            btn.Click += async (_, __) =>
            {
                Log($"Loading shares for device: {dev.Name}");
                StatusText.Text = "Loading shares...";

                SharesPanel.Children.Clear();

                var shares = await NetworkScanner.GetSharesAsync(dev.IP);

                if (shares.Count == 0)
                {
                    SharesPanel.Children.Add(new TextBlock
                    {
                        Text = "No SMB shares found.",
                        Foreground = Brushes.Gray,
                        FontSize = 14
                    });
                }
                else
                {
                    foreach (var share in shares)
                        SharesPanel.Children.Add(CreateShareItem(share));
                }

                StatusText.Text = $"Shares loaded for {dev.Name}";
            };

            Grid.SetRow(btn, 1);
            leftPanel.Children.Add(btn);

            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,150"),
                Margin = new Thickness(0)
            };

            header.Children.Add(leftPanel);
            header.Children.Add(icon);
            Grid.SetColumn(icon, 1);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2A475E")),
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(6),
                Child = header
            };

            border.ContextMenu = BuildOsContextMenu(dev, border);

            return border;
        }

        // ---------------------------------------------------------
        // MENÚ CONTEXTUAL PARA OVERRIDE DE SO
        // ---------------------------------------------------------
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
            AddOsItem("Other");

            root.ItemsSource = osItems;
            menu.ItemsSource = new List<MenuItem> { root };

            return menu;
        }

        // ---------------------------------------------------------
        // TARJETA DE SHARE
        // ---------------------------------------------------------
       
       private Control CreateShareItem(NetworkShare share)
{
    var main = (MainWindow)TopLevel.GetTopLevel(this);

    // ---------------------------------------------------------
    // RUTA DE MONTAJE SEGURA (sin root)
    // ~/.local/share/samba-util/mounts/<IP>/<Share>
    // ---------------------------------------------------------
    string baseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "samba-util", "mounts"
    );

    string mountPoint = Path.Combine(baseDir, share.IP, share.Name);
    Directory.CreateDirectory(mountPoint);

    // ---------------------------------------------------------
    // BOTÓN MOUNT / UNMOUNT (dinámico)
    // ---------------------------------------------------------
    var mountBtn = new Button
    {
        Width = 80,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    void UpdateMountButton()
    {
        mountBtn.Content = NetworkScanner.IsMounted(mountPoint) ? "Unmount" : "Mount";
    }

    UpdateMountButton();

    mountBtn.Click += async (_, __) =>
    {
        try
        {
            // -------------------------
            // UNMOUNT
            // -------------------------
            if (NetworkScanner.IsMounted(mountPoint))
            {
                string cmd = $"sudo umount \"{mountPoint}\"";
                string result = await ShellHelper.RunAsync(cmd);

                if (result.Contains("error") || result.Contains("failed"))
                {
                    main.ShowErrorDialog("Unmount failed", result);
                    return;
                }

                main.ShowToast($"{share.Name} unmounted");
                UpdateMountButton();
                return;
            }

            // -------------------------
            // MOUNT
            // -------------------------
            if (share.Access.Contains("Requires"))
            {
                var cred = await main.ShowCredentialsDialog();
                if (cred == null)
                {
                    main.ShowToast("Mount canceled");
                    return;
                }

                CredStore.User = cred.Value.user;
                CredStore.Password = cred.Value.pass;
            }

            string options = share.Access.Contains("Anonymous")
                ? "-o guest"
                : $"-o username={CredStore.User},password={CredStore.Password}";

            string cmdMount = $"sudo mount.cifs //{share.IP}/{share.Name} \"{mountPoint}\" {options}";
            string resultMount = await ShellHelper.RunAsync(cmdMount);

            if (resultMount.Contains("mount error") || resultMount.Contains("NT_STATUS"))
            {
                main.ShowErrorDialog("Mount failed", resultMount);
                return;
            }

            main.ShowToast($"{share.Name} mounted successfully");
            UpdateMountButton();
        }
        catch (Exception ex)
        {
            main.ShowErrorDialog("Mount/Unmount failed", ex.Message);
        }
    };

    // ---------------------------------------------------------
    // BOTÓN OPEN
    // ---------------------------------------------------------
    var openBtn = new Button
    {
        Content = "Open",
        Width = 80,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    openBtn.Click += async (_, __) =>
    {
        try
        {
            // Si no está montado → montarlo automáticamente
            if (!NetworkScanner.IsMounted(mountPoint))
            {
                if (share.Access.Contains("Requires"))
                {
                    var cred = await main.ShowCredentialsDialog();
                    if (cred == null)
                    {
                        main.ShowToast("Opening canceled");
                        return;
                    }

                    CredStore.User = cred.Value.user;
                    CredStore.Password = cred.Value.pass;
                }

                string options = share.Access.Contains("Anonymous")
                    ? "-o guest"
                    : $"-o username={CredStore.User},password={CredStore.Password}";

                string cmd = $"sudo mount.cifs //{share.IP}/{share.Name} \"{mountPoint}\" {options}";
                string result = await ShellHelper.RunAsync(cmd);

                if (result.Contains("mount error") || result.Contains("NT_STATUS"))
                {
                    main.ShowErrorDialog("Open failed", result);
                    return;
                }

                UpdateMountButton();
            }

            // Abrir carpeta
            await ShellHelper.RunAsync($"xdg-open \"{mountPoint}\"");
            main.ShowToast($"Abriendo {share.Name}");
        }
        catch (Exception ex)
        {
            main.ShowErrorDialog("Open failed", ex.Message);
        }
    };

    // ---------------------------------------------------------
    // FILA HORIZONTAL (BOTONES + ACCESS)
    // ---------------------------------------------------------
    var row = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        VerticalAlignment = VerticalAlignment.Center
    };

    row.Children.Add(mountBtn);
    row.Children.Add(openBtn);

    row.Children.Add(new TextBlock
    {
        Text = $"Access: {share.Access}",
        Foreground = Brushes.LightGray,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(20, 0, 0, 0)
    });

    // ---------------------------------------------------------
    // TARJETA COMPLETA
    // ---------------------------------------------------------
    var panel = new StackPanel
    {
        Spacing = 4,
        Children =
        {
            new TextBlock
            {
                Text = $"Share: {share.Name}",
                Foreground = Brushes.White,
                FontSize = 16
            },
            row
        }
    };

    var border = new Border
    {
        Background = new SolidColorBrush(Color.Parse("#1B2838")),
        Padding = new Thickness(8),
        CornerRadius = new CornerRadius(6),
        Child = panel
    };

    if (!string.IsNullOrWhiteSpace(share.Comment))
        ToolTip.SetTip(border, share.Comment);

    return border;
}

    
        
        // ---------------------------------------------------------

        

        
        private void Log(string msg)
        {
            Console.WriteLine($"[NetworkView] {msg}");
            StatusText.Text = msg;
        }
    }

    
}
