using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SAMBA_Util.Helpers;
using System.Threading.Tasks;

namespace SAMBA_Util.Views;

public partial class ConfigWindow : Window
{
    private ConfigManager.AppConfig _config;
    private string? _firewallBackend;
    private bool? _initialFirewallState;
    private bool _initialPrintingState;

    public event Func<Task>? ConfigSaved;

    public ConfigWindow()
    {
        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _config = ConfigManager.Load();

        // Cargar ruta smb.conf
        TxtSmbConf.Text = _config.SmbConfPath;

        // Estado real de impresión en smb.conf
        _initialPrintingState = SambaConfigWriter.IsPrintingEnabled(_config.SmbConfPath);
        PrintingToggle.IsChecked = _initialPrintingState;

        // Seleccionar protocolo SMB actual en el ComboBox
        foreach (var entry in ProtocolCombo.Items)
        {
            if (entry is ComboBoxItem item && item.Tag?.ToString() == _config.MinProtocol)
            {
                ProtocolCombo.SelectedItem = item;
                break;
            }
        }
        if (ProtocolCombo.SelectedItem == null && ProtocolCombo.Items.Count > 0)
            ProtocolCombo.SelectedIndex = 0; // Fallback a SMB2

        // Seleccionar permiso actual en el ComboBox
        foreach (var entry in PermCombo.Items)
        {
            if (entry is ComboBoxItem item && item.Tag?.ToString() == _config.DefaultPermissions)
            {
                PermCombo.SelectedItem = item;
                break;
            }
        }

        // Eventos
        BtnBrowseSmbConf.Click += OnBrowseSmbConf;
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
            Console.WriteLine($"[CONFIG ERROR] {ex}");
            TxtFirewallStatus.Text = $"Could not initialize configuration: {ex.Message}";
        }
    }

    private async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
        {
            TxtFirewallStatus.Text = "Firewall cannot be checked without administrator authentication.";
            return;
        }

        var result = await Task.Run(DetectFirewall);
        _firewallBackend = result.Backend;
        _initialFirewallState = result.IsEnabled;

        if (_initialFirewallState.HasValue)
        {
            FirewallToggle.IsChecked = _initialFirewallState.Value;
            FirewallToggle.IsEnabled = true;
            TxtFirewallStatus.Text = $"Detected: {result.Backend}.";
        }
        else
        {
            FirewallToggle.IsChecked = false;
            FirewallToggle.IsEnabled = false;
            TxtFirewallStatus.Text = result.Message;
        }
    }

    private async void OnBrowseSmbConf(object? sender, RoutedEventArgs e)
    {
        try
        {
            await BrowseSmbConfAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CONFIG ERROR] {ex}");
            TxtFirewallStatus.Text = $"Could not select smb.conf: {ex.Message}";
        }
    }

    private async Task BrowseSmbConfAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select smb.conf",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Samba config")
                {
                    Patterns = new[] { "*.conf" }
                }
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                TxtSmbConf.Text = path;
            }
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            await SaveConfigurationAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CONFIG ERROR] {ex}");
            TxtFirewallStatus.Text = $"Could not save configuration: {ex.Message}";
        }
    }

    private async Task SaveConfigurationAsync()
    {
        var desiredFirewallState = FirewallToggle.IsChecked == true;
        if (_initialFirewallState.HasValue && desiredFirewallState != _initialFirewallState.Value)
        {
            if (!desiredFirewallState && !await ConfirmDisableFirewallAsync())
            {
                FirewallToggle.IsChecked = _initialFirewallState.Value;
                return;
            }

            if (_firewallBackend == null)
            {
                TxtFirewallStatus.Text = "No supported firewall manager was detected; firewall state was not changed.";
                return;
            }

            var result = await Task.Run(() => SetFirewallState(_firewallBackend, desiredFirewallState));
            if (result.ExitCode != 0)
            {
                TxtFirewallStatus.Text = $"Could not change firewall state: {result.Stderr}{result.Stdout}";
                FirewallToggle.IsChecked = _initialFirewallState.Value;
                return;
            }

            _initialFirewallState = desiredFirewallState;
            TxtFirewallStatus.Text = $"{_firewallBackend} is now {(desiredFirewallState ? "enabled" : "disabled")}.";
        }

        // Guardar ruta
        _config.SmbConfPath = TxtSmbConf.Text?.Trim() ?? "/etc/samba/smb.conf";

        // Guardar protocolo SMB mínimo
        if (ProtocolCombo.SelectedItem is ComboBoxItem protoItem && protoItem.Tag is string protoStr)
        {
            _config.MinProtocol = protoStr;
        }

        // Guardar permisos desde el ComboBox
        if (PermCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string permStr &&
            !string.IsNullOrWhiteSpace(permStr))
        {
            _config.DefaultPermissions = permStr;
        }
        else
        {
            _config.DefaultPermissions = "755";
        }

        // Activar o desactivar la impresión en smb.conf.
        var desiredPrinting = PrintingToggle.IsChecked == true;
        if (desiredPrinting != _initialPrintingState)
        {
            TxtPrintingStatus.Text = desiredPrinting
                ? "Enabling Samba printing..."
                : "Disabling Samba printing...";
            try
            {
                await Task.Run(() => SambaConfigWriter.SetPrintingEnabled(desiredPrinting));
                _initialPrintingState = SambaConfigWriter.IsPrintingEnabled(_config.SmbConfPath);
                PrintingToggle.IsChecked = _initialPrintingState;
                TxtPrintingStatus.Text = _initialPrintingState == desiredPrinting
                    ? $"Samba printing {(desiredPrinting ? "enabled" : "disabled")}."
                    : "Samba printing is still controlled by configuration outside the blocks managed by SAMBA-Util; review smb.conf.";
            }
            catch (Exception ex)
            {
                TxtPrintingStatus.Text = $"Could not change Samba printing: {ex.Message}";
                PrintingToggle.IsChecked = _initialPrintingState;
                return;
            }
        }
        _config.PrintingEnabled = _initialPrintingState;

        ConfigManager.Save(_config);

        // Aplicar también en el servidor: [global] server min protocol.
        TxtProtocolStatus.Text = "Applying minimum SMB protocol to Samba...";
        try
        {
            await Task.Run(() => SambaConfigWriter.SetServerMinProtocol(_config.MinProtocol));
            TxtProtocolStatus.Text =
                $"Server minimum SMB protocol applied: {ConfigManager.NormalizeProtocol(_config.MinProtocol)}.";
        }
        catch (Exception ex)
        {
            TxtProtocolStatus.Text =
                $"Configuration saved, but the server protocol could not be applied: {ex.Message}";
            return;
        }

        Close();
        if (ConfigSaved != null)
            await ConfigSaved.Invoke();
    }

    private static (string? Backend, bool? IsEnabled, string Message) DetectFirewall()
    {
        var ufw = ShellHelper.EjecutarComoRoot(
            "if command -v ufw >/dev/null 2>&1; then ufw status; else echo __UFW_NOT_INSTALLED__; fi");
        var ufwInstalled = !ufw.Stdout.Contains("__UFW_NOT_INSTALLED__", StringComparison.Ordinal);
        var ufwActive = ufw.Stdout.Contains("Status: active", StringComparison.OrdinalIgnoreCase);
        if (ufwActive)
            return ("ufw", true, "UFW is active.");

        var firewalld = ShellHelper.EjecutarComoRoot(
            "if command -v firewall-cmd >/dev/null 2>&1; then firewall-cmd --state 2>&1; else echo __FIREWALLD_NOT_INSTALLED__; fi");
        var firewalldInstalled = !firewalld.Stdout.Contains("__FIREWALLD_NOT_INSTALLED__", StringComparison.Ordinal);
        var firewalldActive = firewalld.Stdout.Trim().Equals("running", StringComparison.OrdinalIgnoreCase);
        if (firewalldActive)
            return ("firewalld", true, "firewalld is active.");

        if (firewalldInstalled)
            return ("firewalld", false, "firewalld is installed but not running.");

        if (ufwInstalled)
            return ("ufw", false, "UFW is installed but inactive.");

        return (null, null, "No supported manager found (UFW or firewalld). Other firewall rules are left untouched.");
    }

    private static (int ExitCode, string Stdout, string Stderr) SetFirewallState(string backend, bool enabled)
    {
        var command = backend switch
        {
            "ufw" => enabled ? "ufw --force enable" : "ufw disable",
            "firewalld" => enabled
                ? "systemctl enable --now firewalld.service"
                : "systemctl disable --now firewalld.service",
            _ => throw new InvalidOperationException($"Unsupported firewall manager: {backend}")
        };

        return ShellHelper.EjecutarComoRoot(command);
    }

    private async Task<bool> ConfirmDisableFirewallAsync()
    {
        var dialog = new Window
        {
            Title = "Disable firewall?",
            Width = 380,
            Height = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = this.FindResource("BackgroundBrush") as IBrush
        };
        var result = false;
        var disableButton = new Button { Content = "Disable firewall", Width = 130 };
        var cancelButton = new Button { Content = "Cancel", Width = 90 };
        disableButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "This disables host firewall protection and may expose services to the network. Continue?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = this.FindResource("ForegroundBrush") as IBrush
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { cancelButton, disableButton }
                }
            }
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}