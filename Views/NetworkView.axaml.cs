using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Platform;

namespace SAMBA_Util.Views
{
    public partial class NetworkView : UserControl
    {
        public NetworkView()
        {
            InitializeComponent();
            ScanButton.Click += OnScanClicked;
        }

        private async void OnScanClicked(object? sender, RoutedEventArgs e)
        {
            Log("Starting network scan...");
            StatusText.Text = "Scanning...";

            DevicesPanel.Children.Clear();
            SharesPanel.Children.Clear();

            var devices = await FakeScanAsync();

            Log($"Scan finished. Devices found: {devices.Count}");

            foreach (var dev in devices)
            {
                Log($"Creating card for device: {dev.Name} ({dev.IP})");
                DevicesPanel.Children.Add(CreateDeviceCard(dev));
            }

            StatusText.Text = $"Found {devices.Count} devices";
        }

        // ---------------------------------------------------------
        // TARJETA DE DISPOSITIVO (compacta + icono 128 + botón arriba)
        // ---------------------------------------------------------
        private Control CreateDeviceCard(FakeDevice dev)
        {
            Log($"Selecting icon for device: {dev.Name}");

            string iconPath = "avares://SAMBA-Util/Assets/Icons/samba-util.png";

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
                icon = new Image
                {
                    Width = 128,
                    Height = 128,
                   // Background = Brushes.Red
                };
            }

            // Panel izquierdo: texto + botón
            var leftPanel = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                Margin = new Thickness(0, 0, 10, 0)
            };

            // Nombre + IP
            leftPanel.Children.Add(new StackPanel
            {
                Spacing = 1, // más compacto
                Children =
                {
                    new TextBlock { Text = dev.Name, Foreground = Brushes.White, FontSize = 18 },
                    new TextBlock { Text = dev.IP, Foreground = Brushes.Gray, FontSize = 13 }
                }
            });

            // Botón justo debajo del nombre
            var btn = new Button
            {
                Content = "Show Shares",
                Width = 120,
                Margin = new Thickness(0, 15, 0, 0) // más compacto
            };

            btn.Click += async (_, __) =>
            {
                Log($"Loading shares for device: {dev.Name}");
                StatusText.Text = "Loading shares...";

                SharesPanel.Children.Clear();

                var shares = await FakeSharesAsync(dev);

                foreach (var share in shares)
                    SharesPanel.Children.Add(CreateShareItem(share));

                StatusText.Text = $"Shares loaded for {dev.Name}";
            };

            Grid.SetRow(btn, 1);
            leftPanel.Children.Add(btn);

            // Grid principal: izquierda (texto+botón) + derecha (icono)
            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,150"),
                Margin = new Thickness(0)
            };

            header.Children.Add(leftPanel);
            header.Children.Add(icon);
            Grid.SetColumn(icon, 1);

            // Tarjeta final (compactada)
            return new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2A475E")),
                Padding = new Thickness(8, 6, 8, 6), // altura reducida
                CornerRadius = new CornerRadius(6),
                Child = header
            };
        }

        // ---------------------------------------------------------
        // TARJETA DE SHARE
        // ---------------------------------------------------------
        private Control CreateShareItem(FakeShare share)
        {
            var panel = new StackPanel { Spacing = 4 };

            panel.Children.Add(new TextBlock
            {
                Text = $"Share: {share.Name}",
                Foreground = Brushes.White
            });

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8
            };

            btnPanel.Children.Add(new Button { Content = "Mount", Width = 80 });
            btnPanel.Children.Add(new Button { Content = "Open", Width = 80 });

            panel.Children.Add(btnPanel);

            return new Border
            {
                Background = new SolidColorBrush(Color.Parse("#1B2838")),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(6),
                Child = panel
            };
        }

        // ---------------------------------------------------------
        // FAKE DATA
        // ---------------------------------------------------------
        private Task<List<FakeDevice>> FakeScanAsync()
        {
            return Task.FromResult(new List<FakeDevice>
            {
                new FakeDevice("PC-01", "192.168.1.10"),
                new FakeDevice("NAS-LivingRoom", "192.168.1.20")
            });
        }

        private Task<List<FakeShare>> FakeSharesAsync(FakeDevice dev)
        {
            return Task.FromResult(new List<FakeShare>
            {
                new FakeShare("Public"),
                new FakeShare("Media"),
                new FakeShare("Backup")
            });
        }

        // ---------------------------------------------------------
        // INSTRUMENTACIÓN
        // ---------------------------------------------------------
        private void Log(string msg)
        {
            Console.WriteLine($"[NetworkView] {msg}");
            StatusText.Text = msg;
        }
    }

    public record FakeDevice(string Name, string IP);
    public record FakeShare(string Name);
}
