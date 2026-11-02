using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SAMBA_Util.Models;
using SAMBA_Util.Helpers;

namespace SAMBA_Util.Helpers;

public class CliApp
{
    private static readonly string ShareNameRegex = @"^[A-Za-z0-9._\-$ ]+$";
    private static readonly string[] SambaUnits = { "smb", "smbd", "nmbd", "samba", "samba4" };

    public static async Task RunAsync()
    {
        AskPasswordOnce();   // Inicializa Credenciales.AdminPassword

        while (true)
        {
            SafeClearScreen();
            Console.WriteLine("===== Samba-Util CLI =====\n");
            Console.WriteLine("1) List shared resources");
            Console.WriteLine("2) Create shared resource");
            Console.WriteLine("3) Delete shared resource");
            Console.WriteLine("4) Edit shared resource");
            Console.WriteLine("5) Network scanner");
            Console.WriteLine("6) Check Samba status");
            Console.WriteLine("7) Validate configuration (testparm)");
            Console.WriteLine("8) Restart Samba");
            Console.WriteLine("9) Reload Samba");
            Console.WriteLine("10) Active connections (smbstatus)");
            Console.WriteLine("11) Samba version");
            Console.WriteLine("0) Exit");
            Console.WriteLine("==========================\n");
            Console.Write("Option: ");

            var opt = Console.ReadLine();

            switch (opt)
            {
                case "1": ListShares(); break;
                case "2": CreateShare(); break;
                case "3": DeleteShare(); break;
                case "4": EditShare(); break;
                case "5": await NetworkScannerMenu(); break;
                case "6": CheckSambaStatus(); break;
                case "7": RunTestparm(); break;
                case "8": RestartSamba(); break;
                case "9": ReloadSamba(); break;
                case "10": ShowSmbStatus(); break;
                case "11": ShowSambaVersion(); break;
                case "0": return;
                default: Console.WriteLine("Invalid option."); break;
            }

            Console.WriteLine("\nPress ENTER to continue...");
            Console.ReadLine();
        }
    }

    private static void SafeClearScreen()
    {
        try
        {
            if (!Console.IsOutputRedirected)
                Console.Clear();
        }
        catch
        {
            // Ignorar si la terminal no soporta secuencias de borrado
        }
    }

    // ---------------------------------------------------------
    //  ASK FOR PASSWORD ONCE AND STORE IT IN MEMORY
    // ---------------------------------------------------------
    private static void AskPasswordOnce()
    {
        if (!string.IsNullOrEmpty(Credenciales.AdminPassword))
            return;

        Console.Write("Enter sudo password: ");
        Credenciales.AdminPassword = ReadPassword();
    }

    // Hidden password input
    private static string ReadPassword()
    {
        string pass = "";
        ConsoleKeyInfo key;

        while ((key = Console.ReadKey(true)).Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace && pass.Length > 0)
            {
                pass = pass[..^1];
                continue;
            }

            if (!char.IsControl(key.KeyChar))
                pass += key.KeyChar;
        }

        Console.WriteLine();
        return pass;
    }

    // ---------------------------------------------------------
    //  LIST SHARES
    // ---------------------------------------------------------
    static void ListShares()
    {
        var shares = SambaConfigReader.LoadShares();

        Console.WriteLine("=== Detected shares ===");
        foreach (var s in shares)
        {
            Console.WriteLine($"[{s.Name}]");
            Console.WriteLine($"  Path: {s.Path}");
            Console.WriteLine($"  ReadOnly: {s.ReadOnly}");
            Console.WriteLine($"  Guests: {s.AllowGuests}");
            Console.WriteLine($"  Browseable: {s.Browseable}");
            Console.WriteLine($"  ValidUsers: {s.ValidUsers}");
            if (!string.IsNullOrWhiteSpace(s.Comment))
                Console.WriteLine($"  Comment: {s.Comment}");
            Console.WriteLine();
        }
    }

    // ---------------------------------------------------------
    //  CREATE SHARE
    // ---------------------------------------------------------
    static void CreateShare()
    {
        Console.Write("Share name (ENTER to cancel): ");
        var name = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("Operation canceled.");
            return;
        }

        if (!Regex.IsMatch(name, ShareNameRegex))
        {
            Console.WriteLine("Invalid share name. Allowed characters: letters, numbers, spaces, dot, underscore, dash, dollar.");
            return;
        }

        Console.Write("Path (ENTER to cancel): ");
        var path = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine("Operation canceled.");
            return;
        }

        var share = new Share
        {
            Name = name,
            Path = path.Trim().Trim('"'),
            ReadOnly = true,
            AllowGuests = false,
            Browseable = true,
            CreateMask = "0744",
            DirectoryMask = "0755",
            UnknownParameters = new List<string>()
        };

        try
        {
            SambaConfigWriter.AddShare(share);
            Console.WriteLine("Share created successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating share: {ex.Message}");
        }
    }

    // ---------------------------------------------------------
    //  DELETE SHARE
    // ---------------------------------------------------------
    static void DeleteShare()
    {
        Console.Write("Share name to delete (ENTER to cancel): ");
        var name = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("Operation canceled.");
            return;
        }

        try
        {
            SambaConfigWriter.DeleteShare(name);
            Console.WriteLine("Share deleted.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting share: {ex.Message}");
        }
    }

    // ---------------------------------------------------------
    //  EDIT SHARE
    // ---------------------------------------------------------
    static void EditShare()
    {
        Console.Write("Share name to edit (ENTER to cancel): ");
        var name = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("Operation canceled.");
            return;
        }

        var shares = SambaConfigReader.LoadShares();
        var share = shares.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (share == null)
        {
            Console.WriteLine("Share not found.");
            return;
        }

        Console.WriteLine($"Editing [{share.Name}]\n");

        Console.WriteLine($"Current path: {share.Path}");
        Console.Write("New path (ENTER to keep): ");
        var newPath = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(newPath))
            share.Path = newPath.Trim().Trim('"');

        Console.WriteLine($"\nReadOnly (current: {(share.ReadOnly ? "yes" : "no")})");
        Console.WriteLine("1) yes\n2) no");
        Console.Write("Select (ENTER to keep): ");
        var roSel = Console.ReadLine();
        if (roSel == "1") share.ReadOnly = true;
        if (roSel == "2") share.ReadOnly = false;

        Console.WriteLine($"\nAllow guests (current: {(share.AllowGuests ? "yes" : "no")})");
        Console.WriteLine("1) yes\n2) no");
        Console.Write("Select (ENTER to keep): ");
        var guestSel = Console.ReadLine();
        if (guestSel == "1") share.AllowGuests = true;
        if (guestSel == "2") share.AllowGuests = false;

        Console.WriteLine($"\nBrowseable (current: {(share.Browseable ? "yes" : "no")})");
        Console.WriteLine("1) yes\n2) no");
        Console.Write("Select (ENTER to keep): ");
        var brSel = Console.ReadLine();
        if (brSel == "1") share.Browseable = true;
        if (brSel == "2") share.Browseable = false;

        Console.WriteLine($"\nValid users (current: {share.ValidUsers})");
        Console.Write("New value (ENTER to keep): ");
        var vu = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(vu))
            share.ValidUsers = vu;

        Console.WriteLine($"Write list (current: {share.WriteList})");
        Console.Write("New value (ENTER to keep): ");
        var wl = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(wl))
            share.WriteList = wl;

        Console.WriteLine($"Read list (current: {share.ReadList})");
        Console.Write("New value (ENTER to keep): ");
        var rl = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(rl))
            share.ReadList = rl;

        Console.WriteLine($"Force user (current: {share.ForceUser})");
        Console.Write("New value (ENTER to keep): ");
        var fu = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(fu))
            share.ForceUser = fu;

        Console.WriteLine($"Force group (current: {share.ForceGroup})");
        Console.Write("New value (ENTER to keep): ");
        var fg = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(fg))
            share.ForceGroup = fg;

        Console.WriteLine($"\nCreate mask (current: {share.CreateMask})");
        Console.WriteLine("1) 0644\n2) 0660\n3) 0744\n4) 0775\n5) custom");
        Console.Write("Select (ENTER to keep): ");
        var cmSel = Console.ReadLine();
        switch (cmSel)
        {
            case "1": share.CreateMask = "0644"; break;
            case "2": share.CreateMask = "0660"; break;
            case "3": share.CreateMask = "0744"; break;
            case "4": share.CreateMask = "0775"; break;
            case "5":
                Console.Write("Enter custom mask: ");
                var custom = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(custom))
                    share.CreateMask = custom;
                break;
        }

        Console.WriteLine($"\nDirectory mask (current: {share.DirectoryMask})");
        Console.WriteLine("1) 0755\n2) 0770\n3) 0775\n4) custom");
        Console.Write("Select (ENTER to keep): ");
        var dmSel = Console.ReadLine();
        switch (dmSel)
        {
            case "1": share.DirectoryMask = "0755"; break;
            case "2": share.DirectoryMask = "0770"; break;
            case "3": share.DirectoryMask = "0775"; break;
            case "4":
                Console.Write("Enter custom mask: ");
                var custom = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(custom))
                    share.DirectoryMask = custom;
                break;
        }

        share.UnknownParameters ??= new List<string>();

        try
        {
            SambaConfigWriter.UpdateShare(share);
            Console.WriteLine("Share updated successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating share: {ex.Message}");
        }
    }

    // ---------------------------------------------------------
    //  NETWORK SCANNER MENU
    // ---------------------------------------------------------
    static async Task NetworkScannerMenu()
    {
        CredStore.User = null;
        CredStore.Password = null;

        SafeClearScreen();
        Console.WriteLine("=== Network Scanner ===\n");

        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni =>
                ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                ni.OperationalStatus == OperationalStatus.Up)
            .ToList();

        if (interfaces.Count == 0)
        {
            Console.WriteLine("No active network interfaces found.");
            return;
        }

        Console.WriteLine("Available network interfaces:");
        int idx = 1;
        foreach (var ni in interfaces)
        {
            Console.WriteLine($"{idx}) {ni.Name} ({ni.Description})");
            idx++;
        }

        Console.WriteLine();
        Console.Write("Select interface number: ");
        var sel = Console.ReadLine();

        if (!int.TryParse(sel, out int ifaceNum) || ifaceNum <= 0 || ifaceNum > interfaces.Count)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }

        string ifaceName = interfaces[ifaceNum - 1].Name;

        Console.WriteLine($"\nScanning network on interface: {ifaceName}...\n");

        var devices = await NetworkScanner.DiscoverAsync(ifaceName);

        if (devices.Count == 0)
        {
            Console.WriteLine("No devices found.");
            return;
        }

        Console.WriteLine("=== Devices detected ===");
        int index = 1;
        foreach (var dev in devices)
        {
            Console.WriteLine($"{index}) {dev.IP}  {dev.Name}  OS={dev.OS}");
            index++;
        }

        Console.WriteLine();
        Console.Write("Select device number (0 to exit): ");
        var devSel = Console.ReadLine();

        if (!int.TryParse(devSel, out int devNum) || devNum <= 0 || devNum > devices.Count)
            return;

        var device = devices[devNum - 1];

        Console.WriteLine($"Getting shares from {device.IP}...");

        CredStore.User = null;
        CredStore.Password = null;

        var shares = await NetworkScanner.GetSharesAsync(device.IP);

        Console.WriteLine($"\n=== Shares on {device.IP} ({device.OS}) ===");

        int sidx = 1;
        foreach (var s in shares)
        {
            Console.WriteLine($"{sidx}) [{s.Name}]  Access={s.Access}  Comment={s.Comment}");
            sidx++;
        }

        Console.WriteLine("\nOptions:");
        Console.WriteLine("1) Open share");
        Console.WriteLine("2) Set OS override");
        Console.WriteLine("0) Back");
        Console.Write("Select: ");

        var opt = Console.ReadLine();

        switch (opt)
        {
            case "1":
                OpenShareFromScanner(device.IP, shares);
                break;

            case "2":
                Console.Write("Enter OS override (e.g., Windows, Linux, Android): ");
                var os = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(os))
                {
                    OsOverrideManager.SetOverride(device.IP, os);
                    Console.WriteLine("Override saved.");
                }
                break;

            default:
                return;
        }
    }

    // ---------------------------------------------------------
    //  OPEN SHARE (NO CIFS, NO ROOT)
    // ---------------------------------------------------------
    static void OpenShareFromScanner(string ip, List<NetworkShare> shares)
    {
        CredStore.User = null;
        CredStore.Password = null;

        Console.Write("Select share number to open: ");
        var sel = Console.ReadLine();

        if (!int.TryParse(sel, out int num) || num <= 0 || num > shares.Count)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }

        var share = shares[num - 1];
        string cleanShareName = share.Name.Split('|')[0].Trim();

        Console.WriteLine($"Selected: {cleanShareName}");

        bool isAnonymous = share.Access == "Anonymous";
        string? user = null;
        string? pass = null;

        if (!isAnonymous)
        {
            Console.Write("SMB username: ");
            user = Console.ReadLine();

            Console.Write("SMB password: ");
            pass = ReadPassword();
        }

        Console.WriteLine("Opening share...");
        SmbHelper.OpenShare(ip, cleanShareName, user, pass);
    }

    // ---------------------------------------------------------
    //  SAMBA STATUS & MANAGEMENT
    // ---------------------------------------------------------
    static void CheckSambaStatus()
    {
        SafeClearScreen();
        Console.WriteLine("=== Samba Status ===\n");

        bool any = false;

        foreach (var unit in SambaUnits)
        {
            try
            {
                var result = ShellHelper.Ejecutar($"systemctl is-active {unit}");
                string status = result.Stdout.Trim();

                if (status == "unknown" || string.IsNullOrWhiteSpace(status))
                    continue;

                any = true;
                Console.WriteLine($"{unit}: {FormatStatus(status)}");
            }
            catch { }
        }

        if (!any)
            Console.WriteLine("No Samba services found.");
    }

    static string FormatStatus(string status)
    {
        return status switch
        {
            "active"   => "active",
            "inactive" => "inactive",
            "failed"   => "failed",
            _          => "unknown"
        };
    }

    static void RunTestparm()
    {
        SafeClearScreen();
        Console.WriteLine("=== Validate Samba Configuration (testparm) ===\n");

        try
        {
            var result = ShellHelper.Ejecutar("testparm -s");

            string stdout = result.Stdout.Trim();
            string stderr = result.Stderr.Trim();

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.WriteLine("Configuration notes / warnings:\n");
                Console.WriteLine(stderr);
                Console.WriteLine();
            }

            if (stdout.Contains("Loaded services file OK", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Configuration is valid.");
            }
            else
            {
                Console.WriteLine(stdout);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error running testparm: {ex.Message}");
        }
    }

    static void ReloadSamba()
    {
        SafeClearScreen();
        Console.WriteLine("=== Reload Samba ===\n");

        bool reloadedAny = false;

        foreach (var unit in SambaUnits)
        {
            try
            {
                var check = ShellHelper.Ejecutar($"systemctl is-active {unit}");
                if (check.Stdout.Trim() == "unknown" || string.IsNullOrWhiteSpace(check.Stdout))
                    continue;

                Console.WriteLine($"Reloading {unit}...");
                var res = ShellHelper.EjecutarComoRoot($"systemctl reload {unit}");

                if (res.ExitCode == 0)
                {
                    Console.WriteLine($"{unit} reloaded successfully.");
                    reloadedAny = true;
                }
                else
                {
                    Console.WriteLine($"{unit} failed to reload.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reloading {unit}: {ex.Message}");
            }
        }

        if (!reloadedAny)
            Console.WriteLine("No active Samba services found to reload.");
    }

    static void RestartSamba()
    {
        SafeClearScreen();
        Console.WriteLine("=== Restart Samba ===\n");

        bool restartedAny = false;

        foreach (var unit in SambaUnits)
        {
            try
            {
                var check = ShellHelper.Ejecutar($"systemctl is-active {unit}");
                if (check.Stdout.Trim() == "unknown" || string.IsNullOrWhiteSpace(check.Stdout))
                    continue;

                Console.WriteLine($"Restarting {unit}...");
                var res = ShellHelper.EjecutarComoRoot($"systemctl restart {unit}");

                if (res.ExitCode == 0)
                {
                    Console.WriteLine($"{unit} restarted successfully.");
                    restartedAny = true;
                }
                else
                {
                    Console.WriteLine($"{unit} failed to restart.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error restarting {unit}: {ex.Message}");
            }
        }

        if (!restartedAny)
            Console.WriteLine("No active Samba services found to restart.");
    }

    static void ShowSmbStatus()
    {
        SafeClearScreen();
        Console.WriteLine("=== Active SMB Connections ===\n");

        try
        {
            var result = ShellHelper.Ejecutar("smbstatus");

            if (string.IsNullOrWhiteSpace(result.Stdout))
            {
                Console.WriteLine("No active connections or smbstatus unavailable.");
            }
            else
            {
                Console.WriteLine(result.Stdout);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing smbstatus: {ex.Message}");
        }
    }

    static void ShowSambaVersion()
    {
        SafeClearScreen();
        Console.WriteLine("=== Samba Version ===\n");

        try
        {
            var result = ShellHelper.Ejecutar("smbd --version");

            if (string.IsNullOrWhiteSpace(result.Stdout))
                Console.WriteLine("Unable to determine Samba version.");
            else
                Console.WriteLine(result.Stdout.Trim());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching version: {ex.Message}");
        }
    }
}