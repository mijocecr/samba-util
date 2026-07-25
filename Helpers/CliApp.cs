using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using SAMBA_Util.Models;
using SAMBA_Util.Helpers;

namespace SAMBA_Util.Helpers;

public class CliApp
{
    public static void Run()
    {
        AskPasswordOnce();   // Initialize Credenciales.AdminPassword

        while (true)
        {
            Console.Clear();
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
                case "1":
                    ListShares();
                    break;

                case "2":
                    CreateShare();
                    break;

                case "3":
                    DeleteShare();
                    break;

                case "4":
                    EditShare();
                    break;

                case "5":
                    // IMPORTANTE: esperar al escáner de red
                    NetworkScannerMenu().GetAwaiter().GetResult();
                    break;
                

                case "6": CheckSambaStatus(); break;
                case "7": RunTestparm(); break;
                case "8": RestartSamba(); break;
                case "9": ReloadSamba(); break;
                case "10": ShowSmbStatus(); break;
                case "11": ShowSambaVersion(); break;

                
                case "0":
                    return;

                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }

            Console.WriteLine("Press ENTER to continue...");
            Console.ReadLine();
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
            Console.WriteLine("Operation cancelled.");
            return;
        }

        Console.Write("Path (ENTER to cancel): ");
        var path = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine("Operation cancelled.");
            return;
        }

        var share = new Share
        {
            Name = name,
            Path = path,
            ReadOnly = true,
            AllowGuests = false,
            Browseable = true,
            CreateMask = "0744",
            DirectoryMask = "0755",
            UnknownParameters = new List<string>()
        };

        SambaConfigWriter.AddShare(share);
        Console.WriteLine("Share created successfully.");
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
            Console.WriteLine("Operation cancelled.");
            return;
        }

        SambaConfigWriter.DeleteShare(name);
        Console.WriteLine("Share deleted.");
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
        Console.WriteLine("Operation cancelled.");
        return;
    }

    var shares = SambaConfigReader.LoadShares();
    var share = shares.FirstOrDefault(s => s.Name == name);

    if (share == null)
    {
        Console.WriteLine("Share not found.");
        return;
    }

    Console.WriteLine($"Editing [{share.Name}]");
    Console.WriteLine();

    // PATH
    Console.WriteLine($"Current path: {share.Path}");
    Console.Write("New path (ENTER to keep): ");
    var newPath = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(newPath))
        share.Path = newPath;

    // READ ONLY
    Console.WriteLine($"ReadOnly (current: {(share.ReadOnly ? "yes" : "no")})");
    Console.WriteLine("1) yes");
    Console.WriteLine("2) no");
    Console.Write("Select (ENTER to keep): ");
    var roSel = Console.ReadLine();
    if (roSel == "1") share.ReadOnly = true;
    if (roSel == "2") share.ReadOnly = false;

    // GUESTS
    Console.WriteLine($"Allow guests (current: {(share.AllowGuests ? "yes" : "no")})");
    Console.WriteLine("1) yes");
    Console.WriteLine("2) no");
    Console.Write("Select (ENTER to keep): ");
    var guestSel = Console.ReadLine();
    if (guestSel == "1") share.AllowGuests = true;
    if (guestSel == "2") share.AllowGuests = false;

    // BROWSEABLE
    Console.WriteLine($"Browseable (current: {(share.Browseable ? "yes" : "no")})");
    Console.WriteLine("1) yes");
    Console.WriteLine("2) no");
    Console.Write("Select (ENTER to keep): ");
    var brSel = Console.ReadLine();
    if (brSel == "1") share.Browseable = true;
    if (brSel == "2") share.Browseable = false;

    // VALID USERS
    Console.WriteLine($"Valid users (current: {share.ValidUsers})");
    Console.Write("New value (ENTER to keep): ");
    var vu = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(vu))
        share.ValidUsers = vu;

    // WRITE LIST
    Console.WriteLine($"Write list (current: {share.WriteList})");
    Console.Write("New value (ENTER to keep): ");
    var wl = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(wl))
        share.WriteList = wl;

    // READ LIST
    Console.WriteLine($"Read list (current: {share.ReadList})");
    Console.Write("New value (ENTER to keep): ");
    var rl = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(rl))
        share.ReadList = rl;

    // FORCE USER
    Console.WriteLine($"Force user (current: {share.ForceUser})");
    Console.Write("New value (ENTER to keep): ");
    var fu = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(fu))
        share.ForceUser = fu;

    // FORCE GROUP
    Console.WriteLine($"Force group (current: {share.ForceGroup})");
    Console.Write("New value (ENTER to keep): ");
    var fg = Console.ReadLine();
    if (!string.IsNullOrWhiteSpace(fg))
        share.ForceGroup = fg;

    // CREATE MASK
    Console.WriteLine($"Create mask (current: {share.CreateMask})");
    Console.WriteLine("1) 0644");
    Console.WriteLine("2) 0660");
    Console.WriteLine("3) 0744");
    Console.WriteLine("4) custom");
    Console.Write("Select (ENTER to keep): ");
    var cmSel = Console.ReadLine();
    switch (cmSel)
    {
        case "1": share.CreateMask = "0644"; break;
        case "2": share.CreateMask = "0660"; break;
        case "3": share.CreateMask = "0744"; break;
        case "4":
            Console.Write("Enter custom mask: ");
            var custom = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(custom))
                share.CreateMask = custom;
            break;
    }

    // DIRECTORY MASK
    Console.WriteLine($"Directory mask (current: {share.DirectoryMask})");
    Console.WriteLine("1) 0755");
    Console.WriteLine("2) 0770");
    Console.WriteLine("3) 0775");
    Console.WriteLine("4) custom");
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

    SambaConfigWriter.UpdateShare(share);
    Console.WriteLine("Share updated.");
}


    // ---------------------------------------------------------
    //  NETWORK SCANNER MENU (SYNC VIA TASK)
    // ---------------------------------------------------------
    static async System.Threading.Tasks.Task NetworkScannerMenu()
{
    Console.Clear();
    Console.WriteLine("=== Network Scanner ===");

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
        Console.WriteLine($"{idx}) {ni.Name}  ({ni.Description})");
        idx++;
    }

    Console.WriteLine();
    Console.Write("Select interface number: ");
    var sel = Console.ReadLine();

    if (!int.TryParse(sel, out int ifaceNum) ||
        ifaceNum <= 0 ||
        ifaceNum > interfaces.Count)
    {
        Console.WriteLine("Invalid selection.");
        return;
    }

    string ifaceName = interfaces[ifaceNum - 1].Name;

    Console.WriteLine();
    Console.WriteLine($"Scanning network on interface: {ifaceName}");
    Console.WriteLine();

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

    if (!int.TryParse(devSel, out int devNum) ||
        devNum <= 0 ||
        devNum > devices.Count)
        return;

    var device = devices[devNum - 1];

    Console.WriteLine($"Getting shares from {device.IP}...");
    var shares = await NetworkScanner.GetSharesAsync(device.IP);

    Console.WriteLine();
    Console.WriteLine($"=== Shares on {device.IP} ({device.OS}) ===");

    int sidx = 1;
    foreach (var s in shares)
    {
        Console.WriteLine($"{sidx}) [{s.Name}]  Access={s.Access}  Comment={s.Comment}");
        sidx++;
    }

    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("1) Mount share");
    Console.WriteLine("2) Unmount share");
    Console.WriteLine("3) Set OS override");
    Console.WriteLine("0) Back");
    Console.Write("Select: ");

    var opt = Console.ReadLine();

    switch (opt)
    {
        case "1":
            await MountShareFromScanner(device.IP, shares);
            break;

        case "2":
            await UnmountShareFromScanner();
            break;

        case "3":
            Console.Write("Enter OS override (e.g., Windows, Linux, Android): ");
            var os = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(os))
            {
                OsOverrideManager.SetOverride(device.IP, os);
                Console.WriteLine("Override saved.");
            }
            break;

        case "0":
        default:
            return;
    }
}
    
    static async System.Threading.Tasks.Task MountShareFromScanner(string ip, List<NetworkShare> shares)
    {
        Console.Write("Select share number to mount: ");
        var sel = Console.ReadLine();

        if (!int.TryParse(sel, out int num) || num <= 0 || num > shares.Count)
        {
            Console.WriteLine("Invalid selection.");
            return;
        }

        var share = shares[num - 1];

        Console.WriteLine($"Selected: {share.Name}");

        Console.Write("SMB username (ENTER for guest): ");
        var user = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(user))
            user = "guest";

        Console.Write("SMB password (ENTER for none): ");
        var pass = ReadPassword();

        CredStore.User = user;
        CredStore.Password = pass;

        Console.Write("Mount point (e.g., /mnt/share): ");
        var mountPoint = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            Console.WriteLine("Invalid mount point.");
            return;
        }

        if (!Directory.Exists(mountPoint))
            Directory.CreateDirectory(mountPoint);

        string cmd =
            $"mount -t cifs //{ip}/{share.Name} \"{mountPoint}\" " +
            $"-o username={CredStore.User},password={CredStore.Password},rw,vers=3.0";

        Console.WriteLine("Mounting...");
        var result = ShellHelper.EjecutarComoRoot(cmd);

        if (result.ExitCode == 0)
            Console.WriteLine("Mounted successfully.");
        else
            Console.WriteLine($"Mount failed: {result.Stderr}");
    }

    static async System.Threading.Tasks.Task UnmountShareFromScanner()
    {
        Console.Write("Enter mount point to unmount: ");
        var mountPoint = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(mountPoint))
        {
            Console.WriteLine("Invalid mount point.");
            return;
        }

        if (!NetworkScanner.IsMounted(mountPoint))
        {
            Console.WriteLine("This mount point is not mounted.");
            return;
        }

        Console.WriteLine("Unmounting...");
        var result = ShellHelper.EjecutarComoRoot($"umount \"{mountPoint}\"");

        if (result.ExitCode == 0)
            Console.WriteLine("Unmounted successfully.");
        else
            Console.WriteLine($"Unmount failed: {result.Stderr}");
    }


    static void CheckSambaStatus()
    {
        Console.Clear();
        Console.WriteLine("=== Samba Status ===\n");

        // Unidades posibles según distro
        string[] units =
        {
            "smb",      // Fedora / RHEL / Rocky / Alma / SUSE
            "smbd",     // Debian / Ubuntu / Arch
            "nmbd",     // Debian / Ubuntu
            "samba",    // Alpine / Gentoo / Void
            "samba4"    // OpenWRT / algunas NAS
        };

        bool any = false;

        foreach (var unit in units)
        {
            var result = ShellHelper.Ejecutar($"systemctl is-active {unit}");
            string status = result.Stdout.Trim();

            // Si la unidad no existe → "unknown"
            if (status == "unknown" || string.IsNullOrWhiteSpace(status))
                continue;

            any = true;
            Console.WriteLine($"{unit}: {FormatStatus(status)}");
        }

        if (!any)
            Console.WriteLine("No Samba services found.");

        Console.WriteLine();
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
        Console.Clear();
        Console.WriteLine("=== Validate Samba Configuration (testparm) ===\n");

        var result = ShellHelper.Ejecutar("testparm -s");

        string stdout = result.Stdout.Trim();
        string stderr = result.Stderr.Trim();

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            Console.WriteLine("Configuration errors detected:\n");
            Console.WriteLine(stderr);
            Console.WriteLine();
            return;
        }

        if (stdout.Contains("Loaded services file OK", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Configuration is valid.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine(stdout);
        Console.WriteLine();
    }


    
    static void ReloadSamba()
    {
        Console.Clear();
        Console.WriteLine("=== Reload Samba ===\n");

        string[] units = { "smb", "smbd", "samba", "samba4" };

        foreach (var unit in units)
        {
            var check = ShellHelper.Ejecutar($"systemctl is-active {unit}");
            if (check.Stdout.Trim() == "unknown")
                continue;

            Console.WriteLine($"Reloading {unit}...");
            var res = ShellHelper.EjecutarComoRoot($"systemctl reload {unit}");

            if (res.ExitCode == 0)
                Console.WriteLine($"{unit} reloaded.");
            else
                Console.WriteLine($"{unit} failed to reload.");
        }

        Console.WriteLine();
    }


    static void RestartSamba()
    {
        Console.Clear();
        Console.WriteLine("=== Restart Samba ===\n");

        // Probar todas las unidades posibles
        string[] units = { "smb", "smbd", "nmbd", "samba", "samba4" };

        foreach (var unit in units)
        {
            var check = ShellHelper.Ejecutar($"systemctl is-active {unit}");
            if (check.Stdout.Trim() == "unknown")
                continue;

            Console.WriteLine($"Restarting {unit}...");
            var res = ShellHelper.EjecutarComoRoot($"systemctl restart {unit}");

            if (res.ExitCode == 0)
                Console.WriteLine($"{unit} restarted.");
            else
                Console.WriteLine($"{unit} failed to restart.");
        }

        Console.WriteLine();
    }


    static void ShowSmbStatus()
    {
        Console.Clear();
        Console.WriteLine("=== Active SMB Connections ===\n");

        var result = ShellHelper.Ejecutar("smbstatus");

        if (string.IsNullOrWhiteSpace(result.Stdout))
        {
            Console.WriteLine("No active connections.");
        }
        else
        {
            Console.WriteLine(result.Stdout);
        }

        Console.WriteLine();
    }

    
    static void ShowSambaVersion()
    {
        Console.Clear();
        Console.WriteLine("=== Samba Version ===\n");

        var result = ShellHelper.Ejecutar("smbd --version");

        if (string.IsNullOrWhiteSpace(result.Stdout))
            Console.WriteLine("Unable to determine Samba version.");
        else
            Console.WriteLine(result.Stdout.Trim());

        Console.WriteLine();
    }


}
