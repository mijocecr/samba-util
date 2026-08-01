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
using Avalonia.Controls.Primitives;

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

        private async void OnScanClicked(object? sender, RoutedEventArgs e)
        {
            Console.Clear();

            var main = TopLevel.GetTopLevel(this) as MainWindow;
            if (main == null)
            {
                Log("Main window not found.");
                return;
            }

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

            loading.Close();

            Log($"Scan finished. Devices found: {devices.Count}");

            foreach (var dev in devices)
                DevicesPanel.Children.Add(CreateDeviceCard(dev));

            StatusText.Text = $"Found {devices.Count} devices";
        }

        private Control CreateDeviceCard(NetworkDevice dev)
        {
            Log($"Selecting icon for device: {dev.Name} ({dev.OS})");

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

            var fg = (SolidColorBrush)this.FindResource("ForegroundBrush");

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
                        Foreground = fg,
                        FontSize = 18
                    },
                    new TextBlock
                    {
                        Text = dev.IP,
                        Foreground = new SolidColorBrush(fg.Color, 0.7),
                        FontSize = 13
                    },
                    new TextBlock
                    {
                        Text = $"OS: {dev.OS}",
                        Foreground = new SolidColorBrush(fg.Color, 0.5),
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
                Console.Clear();
                Console.WriteLine($"[NetworkView] SHOW SHARES FOR {dev.IP}");

                var main = TopLevel.GetTopLevel(this) as MainWindow;
                if (main == null)
                {
                    Log("Main window not found.");
                    return;
                }

                Log($"Loading shares for device: {dev.Name}");
                StatusText.Text = "Loading shares...";

                SharesPanel.Children.Clear();

                var shares = await NetworkScanner.GetSharesAsync(dev.IP);

                if (shares.Count == 0)
                {
                    var cred = await main.ShowCredentialsDialog();
                    if (cred == null)
                    {
                        main.ShowToast("Canceled");
                        StatusText.Text = "Shares loading canceled";
                        return;
                    }

                    CredStore.User = cred.Value.user;
                    CredStore.Password = cred.Value.pass;

                    shares = await NetworkScanner.GetSharesAsync(dev.IP);
                }

                if (shares.Count == 0)
                {
                    SharesPanel.Children.Add(new TextBlock
                    {
                        Text = "Unable to enumerate SMB shares (credentials required or access denied).",
                        Foreground = new SolidColorBrush(fg.Color, 0.7),
                        FontSize = 14
                    });

                    StatusText.Text = $"No shares visible for {dev.Name}";
                    return;
                }

                foreach (var share in shares)
                    SharesPanel.Children.Add(CreateShareItem(share));

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
                Background = this.FindResource("ControlBackgroundBrush") as IBrush,
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(6),
                Child = header
            };

            border.ContextMenu = BuildOsContextMenu(dev, border);

            return border;
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
            {
                Log("Main window not found.");
                return new TextBlock { Text = "Main window not found." };
            }

            var fg = (SolidColorBrush)this.FindResource("ForegroundBrush");

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
                    bool isAnonymous = share.Access == "Anonymous";

                    // Reuse stored credentials if available; only prompt if we don't have any
                    if (!isAnonymous)
                    {
                        bool haveStoredCreds =
                            !string.IsNullOrWhiteSpace(CredStore.User) &&
                            CredStore.User != "guest";

                        if (!haveStoredCreds)
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
                    }

                    SmbHelper.OpenShare(
                        share.IP,
                        share.Name,
                        isAnonymous ? null : CredStore.User,
                        isAnonymous ? null : CredStore.Password
                    );

                    main.ShowToast($"Opening {share.Name}");
                }
                catch (Exception ex)
                {
                    main.ShowErrorDialog("Open failed", ex.Message);
                }
            };

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                VerticalAlignment = VerticalAlignment.Center
            };

            row.Children.Add(openBtn);

            row.Children.Add(new TextBlock
            {
                Text = $"Access: {share.Access}",
                Foreground = new SolidColorBrush(fg.Color, 0.7),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 0, 0)
            });

            var panel = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Share: {share.Name}",
                        Foreground = fg,
                        FontSize = 16
                    },
                    row
                }
            };

            var border = new Border
            {
                Background = this.FindResource("ControlBackgroundBrush") as IBrush,
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(6),
                Child = panel
            };

            if (!string.IsNullOrWhiteSpace(share.Comment))
                ToolTip.SetTip(border, share.Comment);

            return border;
        }

        private void Log(string msg)
        {
            Console.WriteLine($"[NetworkView] {msg}");
            StatusText.Text = msg;
        }
    }
}
