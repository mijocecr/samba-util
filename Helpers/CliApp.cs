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
    private static readonly string[] SambaUnits = { "smb", "smbd", "nmb", "nmbd", "samba", "samba4", "winbind" };
    private static readonly string[] LogCandidates =
    {
        "/var/log/samba/log.smbd",
        "/var/log/samba/log.nmbd",
        "/var/log/samba/log.winbindd",
        "/var/log/samba/log.samba"
    };
    private static readonly string[] SupportedOperatingSystems =
    {
        "Windows", "Linux", "macOS", "BSD", "Unix", "NAS", "SMB Device", "Other"
    };

    public static async Task RunAsync()
    {
        if (!AuthenticateAdmin())
            return;

        try
        {
            await SmbInitializer.InitializeEnvironmentAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Samba initialization failed: {ex.Message}");
            return;
        }

        while (true)
        {
            SafeClearScreen();
            Console.WriteLine("===== SAMBA-Util: choose an action =====\n");
            Console.WriteLine("1) Shares       - list, create, edit, delete, and fix permissions");
            Console.WriteLine("2) Users        - list, add, edit, and delete Samba/system users");
            Console.WriteLine("3) Network      - discover devices, list/open remote shares, identify OS");
            Console.WriteLine("4) Status       - services, config, ports, firewall, shares, users, IPs");
            Console.WriteLine("5) Configuration- smb.conf path, default permissions, firewall toggle");
            Console.WriteLine("6) Logs         - view the latest Samba log entries");
            Console.WriteLine("7) Connections  - show active SMB connections (smbstatus)");
            Console.WriteLine("8) Samba version");
            Console.WriteLine("0) Exit");
            Console.Write("Choose a number from the menu: ");

            var opt = Console.ReadLine();
            if (opt == null)
                return;

            switch (opt)
            {
                case "1": ManageShares(); break;
                case "2": ManageUsers(); break;
                case "3": await NetworkScannerMenu(); break;
                case "4": CheckSambaStatus(); break;
                case "5": ManageConfiguration(); break;
                case "6": ShowLogs(); break;
                case "7": ShowSmbStatus(); break;
                case "8": ShowSambaVersion(); break;
                case "0": return;
                default:
                    Console.WriteLine("That number is not in the menu. Choose one of the displayed options.");
                    WaitForEnter();
                    continue;
            }

            if (opt is "3" or "7" or "8")
                WaitForEnter();
        }
    }

    private static bool AuthenticateAdmin()
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            Console.Write("Enter administrator password for sudo (ENTER to cancel): ");
            Credenciales.AdminPassword = ReadPassword();
            if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
            {
                Credenciales.AdminPassword = "";
                Console.WriteLine("No password entered; no privileged commands were run.");
                return false;
            }

            var result = ShellHelper.EjecutarComoRoot("true");
            if (result.ExitCode == 0)
                return true;

            Credenciales.AdminPassword = "";
            Console.WriteLine(result.ExitCode == 1001
                ? $"Incorrect password ({attempt} of {maxAttempts})."
                : $"Could not authenticate with sudo: {result.Stderr}");
            if (result.ExitCode != 1001 || attempt == maxAttempts)
                return false;
        }

        return false;
    }

    private static void WaitForEnter()
    {
        Console.WriteLine("\nPress ENTER to return to the main menu.");
        Console.ReadLine();
    }

    private static int Choose(string title, IReadOnlyList<string> options, bool allowBack = true)
    {
        Console.WriteLine($"\n{title}");
        for (var i = 0; i < options.Count; i++)
            Console.WriteLine($"{i + 1}) {options[i]}");
        if (allowBack)
            Console.WriteLine("0) Back / cancel");

        while (true)
        {
            Console.Write("Enter the number shown above: ");
            var input = Console.ReadLine();
            if (input == null)
                return 0;
            if (allowBack && input == "0")
                return 0;
            if (int.TryParse(input, out var selected) && selected >= 1 && selected <= options.Count)
                return selected;
            Console.WriteLine("Invalid number. Choose one of the options displayed above.");
        }
    }

    private static string? ReadValue(string prompt, string current = "", bool canClear = false)
    {
        Console.WriteLine($"{prompt} (current: {(string.IsNullOrEmpty(current) ? "not set" : current)})");
        Console.WriteLine(canClear
            ? "ENTER keeps the current value; enter - to clear it."
            : "ENTER keeps the current value.");
        Console.Write("New value: ");
        var value = Console.ReadLine();
        if (string.IsNullOrEmpty(value))
            return null;
        return canClear && value == "-" ? "" : value.Trim();
    }

    private static bool? AskYesNo(string title, bool current)
    {
        var selected = Choose($"{title} (current: {(current ? "Yes" : "No")})", new[] { "Yes", "No" });
        return selected switch { 1 => true, 2 => false, _ => null };
    }

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

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

    // Hidden password input
    private static string ReadPassword()
    {
        if (Console.IsInputRedirected)
            return Console.ReadLine() ?? "";

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
    private static void ManageShares()
    {
        while (true)
        {
            var action = Choose("Share management", new[]
            {
                "List shares", "Create a share", "Edit a share", "Delete a share", "Fix a share's filesystem permissions"
            });
            switch (action)
            {
                case 0: return;
                case 1: ListShares(); break;
                case 2: CreateShare(); break;
                case 3: EditShare(); break;
                case 4: DeleteShare(); break;
                case 5: FixSharePermissions(); break;
            }
            WaitForEnter();
        }
    }

    static void ListShares()
    {
        var shares = LoadSharesOrReport();
        if (shares == null)
            return;

        Console.WriteLine($"=== Shared resources ({shares.Count}) ===");
        if (shares.Count == 0)
        {
            Console.WriteLine("No configurable shares found. The reserved [homes] share is not managed by this application.");
            return;
        }

        foreach (var s in shares)
        {
            Console.WriteLine($"[{s.Name}]");
            Console.WriteLine($"  Path: {s.Path}");
            Console.WriteLine($"  ReadOnly: {s.ReadOnly}");
            Console.WriteLine($"  Guests: {s.AllowGuests}");
            Console.WriteLine($"  Browseable: {s.Browseable}");
            Console.WriteLine($"  Comment: {s.Comment}");
            Console.WriteLine($"  Valid users: {s.ValidUsers}");
            Console.WriteLine($"  Write list: {s.WriteList}");
            Console.WriteLine($"  Read list: {s.ReadList}");
            Console.WriteLine($"  Force user/group: {s.ForceUser} / {s.ForceGroup}");
            Console.WriteLine($"  File/directory masks: {s.CreateMask} / {s.DirectoryMask}");
            var warning = ShareValidator.ValidateShare(s);
            if (!string.IsNullOrWhiteSpace(warning))
                Console.WriteLine($"  Warning: {warning}");
            Console.WriteLine();
        }
    }

    private static List<Share>? LoadSharesOrReport()
    {
        try
        {
            return SambaConfigReader.LoadShares();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not read the configured smb.conf: {ex.Message}");
            return null;
        }
    }

    private static Share? SelectShare(string prompt)
    {
        var shares = LoadSharesOrReport();
        if (shares == null)
            return null;
        if (shares.Count == 0)
        {
            Console.WriteLine("There are no configurable shares to select.");
            return null;
        }

        var selected = Choose(prompt, shares.Select(s => $"[{s.Name}]  {s.Path}").ToArray());
        return selected == 0 ? null : shares[selected - 1];
    }

    // Nombres que Samba reserva o que SAMBA-Util conserva y no gestiona.
    private static bool IsReservedShareName(string name) =>
        name.Equals("global", StringComparison.OrdinalIgnoreCase) ||
        SambaSections.IsSpecial(name);

    // Comprueba la ruta antes de hacer al usuario responder el resto de preguntas.
    private static string? ValidateSharePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "The share path cannot be empty.";

        if (path.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            return "The share path cannot contain line breaks.";

        if (!Path.IsPathFullyQualified(path))
            return "The share path must be absolute (for example /srv/share).";

        if (File.Exists(path))
            return $"'{path}' is a file, not a directory.";

        if (!Directory.Exists(path))
            return $"The directory '{path}' does not exist or is not accessible.";

        return null;
    }

    // ---------------------------------------------------------
    //  CREATE SHARE
    // ---------------------------------------------------------
    static void CreateShare()
    {
        Console.WriteLine("Create a new Samba share. ENTER at a required prompt cancels the operation.");
        Console.Write("Share name: ");
        var name = Console.ReadLine()?.Trim();

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

        if (IsReservedShareName(name))
        {
            Console.WriteLine($"'{name}' is a reserved share name and cannot be created.");
            return;
        }

        var existingShares = LoadSharesOrReport();
        if (existingShares == null)
            return;

        if (existingShares.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"A share named '{name}' already exists.");
            return;
        }

        Console.Write("Path (ENTER to cancel): ");
        var path = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine("Operation canceled.");
            return;
        }

        var sharePath = path.Trim().Trim('"');
        var pathError = ValidateSharePath(sharePath);
        if (pathError != null)
        {
            Console.WriteLine(pathError);
            return;
        }

        var share = new Share
        {
            Name = name,
            Path = sharePath,
            ReadOnly = true,
            AllowGuests = false,
            Browseable = true,
            CreateMask = "0744",
            DirectoryMask = "0755",
            UnknownParameters = new List<string>()
        };

        share.Comment = ReadValue("Share comment", canClear: true) ?? "";
        var readOnly = Choose("Allow clients to modify files?", new[] { "No - read-only", "Yes - read/write" });
        if (readOnly == 0) return;
        share.ReadOnly = readOnly == 1;

        var guests = Choose("Allow guest access?", new[] { "No - require an account", "Yes - allow guests" });
        if (guests == 0) return;
        share.AllowGuests = guests == 2;

        var browseable = Choose("Show this share when clients browse the server?", new[] { "Yes - visible", "No - hidden" });
        if (browseable == 0) return;
        share.Browseable = browseable == 1;

        share.ValidUsers = ReadValue("Allowed users (space-separated)", canClear: true) ?? "";
        share.WriteList = ReadValue("Users allowed to write (space-separated)", canClear: true) ?? "";
        share.ReadList = ReadValue("Users restricted to read-only (space-separated)", canClear: true) ?? "";
        share.ForceUser = ReadValue("Force all file operations to this user", canClear: true) ?? "";
        share.ForceGroup = ReadValue("Force all file operations to this group", canClear: true) ?? "";
        share.CreateMask = ReadMask("File creation mask", share.CreateMask, new[] { "0644", "0660", "0744", "0775" });
        share.DirectoryMask = ReadMask("Directory creation mask", share.DirectoryMask, new[] { "0755", "0770", "0775" });

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
        var share = SelectShare("Choose the share to delete");
        if (share == null)
            return;

        var confirmed = AskYesNo($"Delete [{share.Name}] at {share.Path}?", false);
        if (confirmed != true)
        {
            Console.WriteLine("Deletion canceled.");
            return;
        }

        try
        {
            SambaConfigWriter.DeleteShare(share.Name);
            Console.WriteLine($"Share [{share.Name}] deleted.");
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
        var share = SelectShare("Choose the share to edit");
        if (share == null)
            return;

        Console.WriteLine($"Editing [{share.Name}]\n");

        share.Path = ReadValue("Share path", share.Path) is { } path ? path.Trim().Trim('"') : share.Path;
        share.Comment = ReadValue("Comment", share.Comment, canClear: true) ?? share.Comment;
        share.ReadOnly = AskYesNo("Read-only access?", share.ReadOnly) ?? share.ReadOnly;
        share.AllowGuests = AskYesNo("Allow guest access?", share.AllowGuests) ?? share.AllowGuests;
        share.Browseable = AskYesNo("Visible when browsing?", share.Browseable) ?? share.Browseable;
        share.ValidUsers = ReadValue("Allowed users (space-separated)", share.ValidUsers, canClear: true) ?? share.ValidUsers;
        share.WriteList = ReadValue("Users allowed to write (space-separated)", share.WriteList, canClear: true) ?? share.WriteList;
        share.ReadList = ReadValue("Users restricted to read-only (space-separated)", share.ReadList, canClear: true) ?? share.ReadList;
        share.ForceUser = ReadValue("Force user", share.ForceUser, canClear: true) ?? share.ForceUser;
        share.ForceGroup = ReadValue("Force group", share.ForceGroup, canClear: true) ?? share.ForceGroup;
        share.CreateMask = ReadMask("File creation mask", share.CreateMask, new[] { "0644", "0660", "0744", "0775" });
        share.DirectoryMask = ReadMask("Directory creation mask", share.DirectoryMask, new[] { "0755", "0770", "0775" });

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

    private static string ReadMask(string title, string current, string[] presets)
    {
        var choices = presets.Select(mask => $"{mask} (preset)").Concat(new[] { "Enter a custom octal mask" }).ToArray();
        var selected = Choose($"{title} (current: {current})", choices);
        if (selected == 0)
            return current;
        if (selected <= presets.Length)
            return presets[selected - 1];

        while (true)
        {
            Console.Write("Enter 3 or 4 octal digits (example: 0755; ENTER keeps current): ");
            var value = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(value))
                return current;
            if (value != null && Regex.IsMatch(value, @"^0?[0-7]{3}$"))
                return value.Length == 3 ? "0" + value : value;
            Console.WriteLine("Invalid mask. Use only 3 or 4 octal digits from 0 to 7.");
        }
    }

    private static void FixSharePermissions()
    {
        var share = SelectShare("Choose a share whose filesystem permissions need fixing");
        if (share == null)
            return;

        var config = ConfigManager.Load();
        var mode = Regex.IsMatch(config.DefaultPermissions ?? "", @"^[0-7]{3,4}$")
            ? config.DefaultPermissions
            : "0755";
        Console.WriteLine($"Set permissions on {share.Path} to {mode} (configured default)?");
        if (AskYesNo("Proceed?", false) != true)
            return;

        var result = ShellHelper.EjecutarComoRoot(
            $"chmod -R {mode} -- {ShellQuote(share.Path)}", ShellHelper.LongTimeoutMs);
        if (result.ExitCode != 0)
        {
            Console.WriteLine($"Could not change permissions: {result.Stderr}");
            return;
        }

        var (owner, group, actualMode) = FileSystemHelper.GetPermissions(share.Path);
        var updated = ShareValidator.ValidateShare(share);
        Console.WriteLine($"Permissions now: owner={owner}, group={group}, mode={actualMode}");
        Console.WriteLine(string.IsNullOrWhiteSpace(updated) ? "Share permission check passed." : $"Warning: {updated}");
    }

    private static void ManageUsers()
    {
        while (true)
        {
            var action = Choose("Samba user management", new[]
            {
                "List Samba users", "Add a Samba user", "Edit password or enable/disable a user", "Delete a Samba user and its eligible system account"
            });
            switch (action)
            {
                case 0: return;
                case 1: ListUsers(); break;
                case 2: AddUser(); break;
                case 3: EditUser(); break;
                case 4: DeleteUser(); break;
            }
            WaitForEnter();
        }
    }

    private static List<string> LoadUsers()
    {
        var result = ShellHelper.EjecutarComoRoot("pdbedit -L", ShellHelper.LongTimeoutMs);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Could not list Samba users: {result.Stderr}");

        return SambaUsers.ParseList(result.Stdout);
    }

    private static string? SelectUser(string prompt)
    {
        List<string> users;
        try
        {
            users = LoadUsers();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return null;
        }

        if (users.Count == 0)
        {
            Console.WriteLine("No Samba users are configured.");
            return null;
        }

        var selection = Choose(prompt, users);
        return selection == 0 ? null : users[selection - 1];
    }

    private static void ListUsers()
    {
        try
        {
            var users = LoadUsers();
            Console.WriteLine("=== Samba users ===");
            if (users.Count == 0)
                Console.WriteLine("No Samba users are configured.");
            else
                for (var i = 0; i < users.Count; i++)
                    Console.WriteLine($"{i + 1}) {users[i]}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private static void AddUser()
    {
        Console.WriteLine("A Samba login also needs a local system account. The account is created without a home directory and cannot log in interactively.");
        Console.Write("New username (letters, numbers, dot, underscore, dash; ENTER cancels): ");
        var username = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            Console.WriteLine("Operation canceled.");
            return;
        }
        if (!Regex.IsMatch(username, @"^[A-Za-z_][A-Za-z0-9._-]*\$?$"))
        {
            Console.WriteLine("Invalid username. Use a letter/underscore first, then letters, numbers, dot, underscore, dash, or a trailing dollar sign.");
            return;
        }

        var passwordChoice = Choose("Set a Samba password for this account?", new[] { "Yes - enter a password", "No - create a passwordless Samba account" });
        if (passwordChoice == 0)
            return;

        var password = "";
        if (passwordChoice == 1)
        {
            Console.Write("Samba password (input hidden): ");
            password = ReadPassword();
            if (string.IsNullOrEmpty(password))
            {
                Console.WriteLine("An empty password was not accepted; the account was not created.");
                return;
            }
        }

        var unixAccount = ShellHelper.EjecutarComoRoot($"id -u -- {ShellQuote(username)}");
        var createdUnixAccount = false;
        if (unixAccount.ExitCode != 0)
        {
            var createUnix = ShellHelper.EjecutarComoRoot(
                $"useradd -M -s \"$(command -v nologin || echo /sbin/nologin)\" -- {ShellQuote(username)}",
                ShellHelper.LongTimeoutMs);
            if (createUnix.ExitCode != 0)
            {
                Console.WriteLine($"Could not create the local system account: {createUnix.Stderr}");
                return;
            }
            createdUnixAccount = true;
        }

        (int ExitCode, string Stdout, string Stderr) result;
        if (passwordChoice == 1)
        {
            result = ShellHelper.EjecutarComoRootConEntrada(
                $"smbpasswd -a -s {ShellQuote(username)}", $"{password}\n{password}\n");
        }
        else
        {
            result = ShellHelper.EjecutarComoRoot($"smbpasswd -a -n {ShellQuote(username)}");
        }

        if (result.ExitCode == 0)
        {
            Console.WriteLine($"Samba user '{username}' created.");
            return;
        }

        Console.WriteLine($"Could not create Samba user: {result.Stderr}{result.Stdout}");
        if (createdUnixAccount)
        {
            var cleanup = ShellHelper.EjecutarComoRoot($"userdel -- {ShellQuote(username)}");
            if (cleanup.ExitCode != 0)
                Console.WriteLine($"Could not remove the newly created local account: {cleanup.Stderr}");
        }
    }

    private static void EditUser()
    {
        var username = SelectUser("Choose the Samba user to edit");
        if (username == null)
            return;

        var info = ShellHelper.EjecutarComoRoot(
            $"pdbedit -L -v {ShellQuote(username)}", ShellHelper.LongTimeoutMs);
        var unix = ShellHelper.EjecutarComoRoot($"id -u -- {ShellQuote(username)}");
        Console.WriteLine($"Samba account details for {username}:");
        Console.WriteLine(info.ExitCode == 0 ? info.Stdout : $"Could not load Samba details: {info.Stderr}");
        Console.WriteLine($"Unix UID: {(unix.ExitCode == 0 ? unix.Stdout.Trim() : "not found")}");

        var enabled = !info.Stdout.Contains("Account Flags", StringComparison.OrdinalIgnoreCase) ||
                      !Regex.IsMatch(info.Stdout, @"Account Flags\s*:\s*\[.*D.*\]", RegexOptions.IgnoreCase);
        var state = Choose($"Account status (currently {(enabled ? "enabled" : "disabled")})", new[] { "Enable account", "Disable account", "Leave unchanged" });
        var passwordChoice = Choose("Change this user's Samba password?", new[] { "Yes - enter a new password", "No - keep current password" });
        if (state == 0 || passwordChoice == 0)
            return;

        if (passwordChoice == 1)
        {
            Console.Write("New Samba password (input hidden): ");
            var password = ReadPassword();
            if (string.IsNullOrEmpty(password))
            {
                Console.WriteLine("An empty password was not accepted; no changes were made.");
                return;
            }
            var update = ShellHelper.EjecutarComoRootConEntrada(
                $"smbpasswd -s {ShellQuote(username)}", $"{password}\n{password}\n");
            if (update.ExitCode != 0)
            {
                Console.WriteLine($"Could not change password: {update.Stderr}{update.Stdout}");
                return;
            }
        }

        if (state is 1 or 2)
        {
            var action = state == 1 ? "-e" : "-d";
            var update = ShellHelper.EjecutarComoRoot($"smbpasswd {action} {ShellQuote(username)}");
            if (update.ExitCode != 0)
            {
                Console.WriteLine($"Could not update account status: {update.Stderr}");
                return;
            }
        }

        Console.WriteLine($"Samba user '{username}' updated.");
    }

    private static void DeleteUser()
    {
        var username = SelectUser("Choose the Samba user to delete");
        if (username == null)
            return;

        var unix = ShellHelper.EjecutarComoRoot($"id -u -- {ShellQuote(username)}");
        var removeUnix = unix.ExitCode == 0 && int.TryParse(unix.Stdout.Trim(), out var uid) && uid >= 1000;
        var warning = removeUnix
            ? "This will also delete the local account and its home directory."
            : "Only the Samba account will be deleted; a system account, if present, will be kept.";
        if (AskYesNo($"Delete Samba user '{username}'? {warning}", false) != true)
        {
            Console.WriteLine("Deletion canceled.");
            return;
        }

        var sambaDelete = ShellHelper.EjecutarComoRoot($"smbpasswd -x {ShellQuote(username)}");
        if (sambaDelete.ExitCode != 0)
        {
            Console.WriteLine($"Could not delete the Samba account: {sambaDelete.Stderr}");
            return;
        }

        if (removeUnix)
        {
            var unixDelete = ShellHelper.EjecutarComoRoot(
                $"userdel -r -- {ShellQuote(username)}", ShellHelper.LongTimeoutMs);
            if (unixDelete.ExitCode != 0)
            {
                Console.WriteLine($"Samba account deleted, but local account deletion failed: {unixDelete.Stderr}");
                return;
            }
        }
        Console.WriteLine($"User '{username}' deleted.");
    }

    private static void ManageConfiguration()
    {
        while (true)
        {
            var config = ConfigManager.Load();
            Console.WriteLine($"\nCurrent smb.conf: {config.SmbConfPath}");
            Console.WriteLine($"Default filesystem permissions: {config.DefaultPermissions}");
            Console.WriteLine($"Minimum SMB protocol (client and server): {ConfigManager.NormalizeProtocol(config.MinProtocol)}");
            var action = Choose("Configuration", new[]
            {
                "Change smb.conf path",
                "Change default permissions used by Fix Share Permissions",
                "Change minimum SMB protocol (SMB2/SMB3/NT1)",
                "Enable or disable UFW/firewalld"
            });
            if (action == 0)
                return;

            try
            {
                switch (action)
                {
                    case 1:
                        ChangeSmbConfPath(config);
                        break;
                    case 2:
                        ChangeDefaultPermissions(config);
                        break;
                    case 3:
                        ChangeMinProtocol(config);
                        break;
                    case 4:
                        ConfigureFirewall();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not save configuration: {ex.Message}");
            }
            WaitForEnter();
        }
    }

    private static void ChangeSmbConfPath(ConfigManager.AppConfig config)
    {
        var path = ReadValue("Absolute path to smb.conf", config.SmbConfPath);
        if (path == null)
            return;
        if (!Path.IsPathFullyQualified(path))
        {
            Console.WriteLine("The configuration path must be absolute.");
            return;
        }

        config.SmbConfPath = path;
        ConfigManager.Save(config);
        Console.WriteLine("smb.conf path saved.");
    }

    private static void ChangeDefaultPermissions(ConfigManager.AppConfig config)
    {
        var choices = new[] { "755", "775", "770", "777" };
        var selected = Choose("Default permission mode", choices.Select(mode => $"{mode} (octal)").ToArray());
        if (selected == 0)
            return;

        config.DefaultPermissions = choices[selected - 1];
        ConfigManager.Save(config);
        Console.WriteLine($"Default permissions set to {config.DefaultPermissions}.");
    }

    private static void ChangeMinProtocol(ConfigManager.AppConfig config)
    {
        var protocols = new[] { "SMB2", "SMB3", "NT1" };
        var labels = new[]
        {
            "SMB2 (recommended)",
            "SMB3",
            "SMB1 / NT1 (legacy, insecure)"
        };

        var current = ConfigManager.NormalizeProtocol(config.MinProtocol);
        var selected = Choose($"Minimum SMB protocol (client and server, current: {current})", labels);
        if (selected == 0)
            return;

        var protocol = protocols[selected - 1];
        if (protocol == "NT1")
        {
            Console.WriteLine("Warning: SMB1/NT1 is obsolete and insecure. Use it only for legacy devices.");
            if (AskYesNo("Enable SMB1/NT1 anyway?", false) != true)
            {
                Console.WriteLine("Protocol was not changed.");
                return;
            }
        }

        config.MinProtocol = protocol;
        ConfigManager.Save(config);
        Console.WriteLine($"Minimum SMB protocol saved in the app configuration: {protocol}.");

        try
        {
            var changed = SambaConfigWriter.SetServerMinProtocol(protocol);
            var effective = SambaConfigWriter.GetEffectiveServerMinProtocol();
            Console.WriteLine(changed
                ? "server min protocol written to smb.conf and Samba restarted."
                : "smb.conf already had that server min protocol; nothing was rewritten.");

            if (effective is not null)
                Console.WriteLine($"Samba reports the effective server min protocol as {effective}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"The app configuration was saved, but smb.conf could not be updated: {ex.Message}");
        }
    }

    private static void ConfigureFirewall()
    {
        var detected = DetectFirewall();
        Console.WriteLine(detected.Message);
        if (detected.Backend == null || !detected.IsEnabled.HasValue)
            return;

        var desiredState = Choose($"{detected.Backend} is currently {(detected.IsEnabled.Value ? "enabled" : "disabled")}",
            new[] { "Enable firewall", "Disable firewall" });
        if (desiredState == 0)
            return;
        var enable = desiredState == 1;
        if (!enable && AskYesNo("Disabling the firewall can expose this computer and SMB shares to the network. Continue?", false) != true)
        {
            Console.WriteLine("Firewall was not changed.");
            return;
        }

        var command = detected.Backend switch
        {
            "ufw" => enable ? "ufw --force enable" : "ufw disable",
            "firewalld" => enable ? "systemctl enable --now firewalld.service" : "systemctl disable --now firewalld.service",
            _ => throw new InvalidOperationException($"Unsupported firewall manager: {detected.Backend}")
        };
        var result = ShellHelper.EjecutarComoRoot(command);
        Console.WriteLine(result.ExitCode == 0
            ? $"{detected.Backend} is now {(enable ? "enabled" : "disabled")}."
            : $"Could not change firewall state: {result.Stderr}{result.Stdout}");
    }

    private static void ShowLogs()
    {
        var available = LogCandidates
            .Where(path => ShellHelper.EjecutarComoRoot($"test -f {ShellQuote(path)}").ExitCode == 0)
            .ToArray();
        if (available.Length == 0)
        {
            Console.WriteLine("No Samba log files were found in the standard locations.");
            return;
        }

        var selected = Choose("Choose the Samba log to view", available);
        if (selected == 0)
            return;
        var logPath = available[selected - 1];

        while (true)
        {
            var action = Choose($"Log file: {logPath}", new[] { "Show the latest 500 lines", "Open the full log in the desktop viewer" });
            if (action == 0)
                return;
            if (action == 1)
            {
                var result = ShellHelper.EjecutarComoRoot($"tail -n 500 -- {ShellQuote(logPath)}");
                Console.WriteLine(result.ExitCode == 0 ? result.Stdout : $"Could not read log: {result.Stderr}");
            }
            else
            {
                var result = ShellHelper.Ejecutar($"xdg-open {ShellQuote(logPath)}");
                if (result.ExitCode != 0)
                    Console.WriteLine($"Could not open the log viewer: {result.Stderr}");
            }
            WaitForEnter();
        }
    }

    // ---------------------------------------------------------
    //  NETWORK SCANNER MENU
    // ---------------------------------------------------------
    static async Task NetworkScannerMenu()
    {
        CredStore.User = "guest";
        CredStore.Password = "";

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

        var ifaceNum = Choose("Choose the network interface to scan", interfaces
            .Select(ni => $"{ni.Name} ({ni.Description})")
            .ToArray());
        if (ifaceNum == 0)
            return;

        string ifaceName = interfaces[ifaceNum - 1].Name;

        Console.WriteLine($"\nScanning network on interface: {ifaceName}...\n");

        var devices = await NetworkScanner.DiscoverAsync(ifaceName);

        if (devices.Count == 0)
        {
            Console.WriteLine("No devices found.");
            return;
        }

        var devNum = Choose("Choose a discovered device", devices
            .Select(dev => $"{dev.IP}  {dev.Name}  OS={dev.OS}")
            .ToArray());
        if (devNum == 0)
            return;

        var device = devices[devNum - 1];

        Console.WriteLine($"Getting shares from {device.IP}...");

        CredStore.User = "guest";
        CredStore.Password = "";

        var shares = await NetworkScanner.GetSharesAsync(device.IP);
        if (shares.Count == 0)
        {
            var retry = Choose("No shares were listed anonymously. Try again with SMB credentials?", new[] { "Yes, enter credentials", "No, go back" });
            if (retry == 1)
            {
                Console.Write("SMB username: ");
                var username = Console.ReadLine()?.Trim();
                if (!string.IsNullOrWhiteSpace(username))
                {
                    CredStore.User = username;
                    Console.Write("SMB password (input hidden): ");
                    CredStore.Password = ReadPassword();
                    shares = await NetworkScanner.GetSharesAsync(device.IP);
                }
                else
                {
                    Console.WriteLine("No username entered; credential retry canceled.");
                }
            }
        }

        if (shares.Count == 0)
        {
            Console.WriteLine("Unable to enumerate shares. The server may require credentials or deny access.");
            return;
        }

        Console.WriteLine($"\n=== Shares on {device.IP} ({device.OS}) ===");

        int sidx = 1;
        foreach (var s in shares)
        {
            Console.WriteLine($"{sidx}) [{s.Name}]  Access={s.Access}  Comment={s.Comment}");
            sidx++;
        }

        while (true)
        {
            var action = Choose($"Remote device: {device.Name} ({device.IP})", new[]
            {
                "List shares", "Open a share", "Set the device operating system override"
            });
            if (action == 0)
                return;
            if (action == 1)
            {
                Console.WriteLine("\n=== Shares ===");
                for (var i = 0; i < shares.Count; i++)
                    Console.WriteLine($"{i + 1}) [{shares[i].Name}]  Access={shares[i].Access}  Comment={shares[i].Comment}");
            }
            else if (action == 2)
            {
                OpenShareFromScanner(device.IP, shares);
            }
            else
            {
                var os = Choose("Choose the operating system to display for this device", SupportedOperatingSystems);
                if (os != 0)
                {
                    OsOverrideManager.SetOverride(device.IP, SupportedOperatingSystems[os - 1]);
                    Console.WriteLine($"Operating system override saved as {SupportedOperatingSystems[os - 1]}.");
                }
            }
        }
    }

    // ---------------------------------------------------------
    //  OPEN SHARE (NO CIFS, NO ROOT)
    // ---------------------------------------------------------
    static void OpenShareFromScanner(string ip, List<NetworkShare> shares)
    {
        var num = Choose("Choose the remote share to open", shares
            .Select(share => $"[{share.Name}]  Access={share.Access}  {share.Comment}")
            .ToArray());
        if (num == 0)
            return;

        var share = shares[num - 1];
        string cleanShareName = share.Name.Split('|')[0].Trim();

        Console.WriteLine($"Selected: {cleanShareName}");

        bool isAnonymous = share.Access == "Anonymous";
        string? user = null;
        string? pass = null;

        if (!isAnonymous)
        {
            if (!string.IsNullOrWhiteSpace(CredStore.User) &&
                !CredStore.User.Equals("guest", StringComparison.OrdinalIgnoreCase))
            {
                user = CredStore.User;
                pass = CredStore.Password;
            }
            else
            {
                Console.Write("SMB username: ");
                user = Console.ReadLine()?.Trim();
                Console.Write("SMB password (input hidden): ");
                pass = ReadPassword();
            }
        }

        Console.WriteLine("Opening share...");
        SmbHelper.OpenShare(ip, cleanShareName, user, pass);
    }

    // ---------------------------------------------------------
    //  SAMBA STATUS & MANAGEMENT
    // ---------------------------------------------------------
    static void CheckSambaStatus()
    {
        while (true)
        {
            PrintStatusReport();
            var action = Choose("Status actions", new[]
            {
                "Refresh this report", "Validate smb.conf with testparm", "Reload Samba services", "Restart Samba services"
            });
            switch (action)
            {
                case 0: return;
                case 1: continue;
                case 2: RunTestparm(); break;
                case 3: ReloadSamba(); break;
                case 4: RestartSamba(); break;
            }
            WaitForEnter();
        }
    }

    private static void PrintStatusReport()
    {
        SafeClearScreen();
        Console.WriteLine("=== Samba status report ===\n");

        var binary = ShellHelper.Ejecutar("command -v smbd");
        if (binary.ExitCode != 0)
        {
            Console.WriteLine("Samba is not installed (smbd executable not found).");
            return;
        }

        Console.WriteLine("Services:");
        var foundService = false;
        foreach (var unit in SambaUnits)
        {
            var result = ShellHelper.Ejecutar($"systemctl is-active {unit}.service 2>/dev/null");
            var status = result.Stdout.Trim();
            if (string.IsNullOrWhiteSpace(status) || status == "unknown")
                continue;
            foundService = true;
            Console.WriteLine($"  {unit}.service: {FormatStatus(status)}");
        }
        if (!foundService)
            Console.WriteLine("  No Samba systemd services were found.");

        var config = ConfigManager.Load();
        Console.WriteLine($"\nConfiguration: {config.SmbConfPath}");
        Console.WriteLine($"  Minimum SMB protocol (client and server): {ConfigManager.NormalizeProtocol(config.MinProtocol)}");
        var effectiveServerProtocol = SambaConfigWriter.GetEffectiveServerMinProtocol(config.SmbConfPath);
        Console.WriteLine($"  Effective server min protocol in smb.conf: {effectiveServerProtocol ?? "unknown"}");
        var testparm = ShellHelper.EjecutarComoRoot(
            $"testparm -s {ShellQuote(config.SmbConfPath)}", ShellHelper.LongTimeoutMs);
        var testOutput = testparm.Stdout + testparm.Stderr;
        if (testparm.ExitCode == 0 && testOutput.Contains("Loaded services file OK", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine("  testparm: configuration OK");
        else
            Console.WriteLine($"  testparm: configuration error\n{testOutput.Trim()}");

        var ports = ShellHelper.Ejecutar("ss -lnt 2>/dev/null");
        Console.WriteLine("\nListening ports:");
        foreach (var port in new[] { "445", "139" })
        {
            var isOpen = ports.Stdout.Split('\n').Any(line => Regex.IsMatch(line, $@"[:.]{port}\s"));
            Console.WriteLine($"  TCP {port}: {(isOpen ? "open/listening" : "closed")}");
        }
        PrintFirewallStatus(ports.Stdout);

        var shares = LoadSharesOrReport();
        Console.WriteLine("\nConfigured shares:");
        if (shares == null)
            Console.WriteLine("  Could not read shares; see the error above.");
        else if (shares.Count == 0)
            Console.WriteLine("  No configurable shares found.");
        else
            foreach (var share in shares)
                Console.WriteLine($"  [{share.Name}] {share.Path} (browseable={share.Browseable}, guests={share.AllowGuests})");

        var shareCheck = ShellHelper.EjecutarComoRoot(
            $"smbclient -L localhost -N {ConfigManager.ClientMinProtocolOption()} 2>&1");
        Console.WriteLine(shareCheck.Stdout.Contains("Sharename", StringComparison.OrdinalIgnoreCase)
            ? "\nLocal SMB enumeration: Samba responded with a share list."
            : $"\nLocal SMB enumeration failed: {(shareCheck.Stderr + shareCheck.Stdout).Trim()}");

        PrintUserConsistency();
        Console.WriteLine("\nActive network interfaces:");
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses
                .Where(address => address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(address => $"  {ni.Name}: {address.Address}"))
            .ToArray();
        Console.WriteLine(interfaces.Length == 0 ? "  No active IPv4 interfaces found." : string.Join(Environment.NewLine, interfaces));
        Console.WriteLine($"\nLast updated: {DateTime.Now:HH:mm:ss}");
    }

    private static void PrintUserConsistency()
    {
        var sambaResult = ShellHelper.EjecutarComoRoot("pdbedit -L", ShellHelper.LongTimeoutMs);
        var unixResult = ShellHelper.EjecutarComoRoot("getent passwd", ShellHelper.LongTimeoutMs);
        Console.WriteLine("\nUser consistency:");
        if (sambaResult.ExitCode != 0 || unixResult.ExitCode != 0)
        {
            Console.WriteLine($"  Could not compare users: {sambaResult.Stderr}{unixResult.Stderr}");
            return;
        }

        var sambaUsers = sambaResult.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2)[0].Trim()).ToHashSet(StringComparer.Ordinal);
        var unixUsers = unixResult.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2)[0].Trim()).ToHashSet(StringComparer.Ordinal);
        var missingUnix = sambaUsers.Except(unixUsers).ToArray();
        var ignored = new[] { "root", "daemon", "nobody" };
        var missingSamba = unixUsers.Except(sambaUsers)
            .Where(name => !ignored.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)) &&
                           !name.StartsWith("systemd", StringComparison.Ordinal))
            .ToArray();
        Console.WriteLine($"  Samba users without a Unix account: {(missingUnix.Length == 0 ? "none" : string.Join(", ", missingUnix))}");
        Console.WriteLine($"  Unix users without a Samba account: {(missingSamba.Length == 0 ? "none" : string.Join(", ", missingSamba))}");
    }

    private static void PrintFirewallStatus(string listeningPorts)
    {
        Console.WriteLine("\nFirewall / SMB access:");
        if (!Regex.IsMatch(listeningPorts, @"[:.]445\s"))
        {
            Console.WriteLine("  TCP 445 is not listening; remote SMB access will fail regardless of firewall rules.");
            return;
        }

        var firewall = DetectFirewall();
        Console.WriteLine($"  {firewall.Message}");
        if (firewall.Backend == "ufw" && firewall.IsEnabled == true)
        {
            var status = ShellHelper.EjecutarComoRoot("ufw status").Stdout;
            var allowsSmb = status.Split('\n').Any(line =>
                line.Contains("ALLOW", StringComparison.OrdinalIgnoreCase) &&
                (line.Contains("445/tcp", StringComparison.OrdinalIgnoreCase) || line.Contains("Samba", StringComparison.OrdinalIgnoreCase)));
            Console.WriteLine(allowsSmb
                ? "  UFW has an inbound SMB/TCP 445 allow rule. Verify from another computer."
                : "  No UFW inbound SMB/TCP 445 allow rule was found; it may block remote access.");
        }
        else if (firewall.Backend == "firewalld" && firewall.IsEnabled == true)
        {
            var zones = ShellHelper.EjecutarComoRoot("firewall-cmd --get-active-zones").Stdout
                .Split('\n').Where(line => line.Length > 0 && !char.IsWhiteSpace(line[0])).Select(line => line.Trim()).ToArray();
            if (zones.Length == 0)
                zones = new[] { ShellHelper.EjecutarComoRoot("firewall-cmd --get-default-zone").Stdout.Trim() };
            var allowsSmb = zones.Any(zone =>
                ShellHelper.EjecutarComoRoot($"firewall-cmd --zone={ShellQuote(zone)} --query-service=samba").ExitCode == 0 ||
                ShellHelper.EjecutarComoRoot($"firewall-cmd --zone={ShellQuote(zone)} --query-port=445/tcp").ExitCode == 0);
            Console.WriteLine(allowsSmb
                ? "  An active firewalld zone allows Samba or TCP 445. Verify from another computer."
                : "  No active firewalld zone allows Samba or TCP 445; it may block remote access.");
        }
        else if (firewall.Backend is not null)
        {
            // Hay un gestor soportado instalado pero inactivo. No inspeccionamos
            // nftables/iptables para no mezclar reglas de distintos backends.
            Console.WriteLine($"  {firewall.Backend} is installed but inactive; nftables/iptables rules were not inspected.");
        }
        else if (firewall.InspectFallback)
        {
            var nft = ShellHelper.EjecutarComoRoot("nft list ruleset 2>/dev/null");
            if (nft.ExitCode == 0 && !string.IsNullOrWhiteSpace(nft.Stdout))
            {
                var inputPolicyBlocks = nft.Stdout.Contains("hook input", StringComparison.OrdinalIgnoreCase) &&
                                        (nft.Stdout.Contains("policy drop", StringComparison.OrdinalIgnoreCase) ||
                                         nft.Stdout.Contains("policy reject", StringComparison.OrdinalIgnoreCase));
                Console.WriteLine(inputPolicyBlocks
                    ? "  nftables has a restrictive input policy; verify that TCP 445 is explicitly allowed."
                    : "  An nftables ruleset exists; local inspection cannot confirm TCP 445 access.");
            }

            var iptables = ShellHelper.EjecutarComoRoot("iptables -S INPUT 2>/dev/null");
            var lines = iptables.Stdout.Split('\n');
            var blocksInput = lines.Any(line =>
                line.StartsWith("-P INPUT DROP", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("-P INPUT REJECT", StringComparison.OrdinalIgnoreCase));
            var permitsSmb = lines.Any(line =>
                line.Contains("--dport 445", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("-j ACCEPT", StringComparison.OrdinalIgnoreCase));
            if (blocksInput || permitsSmb)
                Console.WriteLine(permitsSmb
                    ? "  iptables has a TCP 445 allow rule. Verify from another computer."
                    : "  iptables has a DROP/REJECT input policy without a TCP 445 allow rule.");
        }
    }

    private static (string? Backend, bool? IsEnabled, bool InspectFallback, string Message) DetectFirewall()
    {
        if (string.IsNullOrWhiteSpace(Credenciales.AdminPassword))
            return (null, null, false, "Firewall state could not be checked because administrator authentication is unavailable.");

        var ufw = ShellHelper.EjecutarComoRoot(
            "if command -v ufw >/dev/null 2>&1; then ufw status; else echo __UFW_NOT_INSTALLED__; fi");
        if (ufw.ExitCode is 1001 or 1002)
            return (null, null, false, "Firewall state could not be checked: sudo authentication failed or is unavailable.");
        var ufwInstalled = !ufw.Stdout.Contains("__UFW_NOT_INSTALLED__", StringComparison.Ordinal);
        if (ufwInstalled && ufw.Stdout.Contains("Status: active", StringComparison.OrdinalIgnoreCase))
            return ("ufw", true, false, "UFW is active.");

        var firewalld = ShellHelper.EjecutarComoRoot(
            "if command -v firewall-cmd >/dev/null 2>&1; then firewall-cmd --state 2>&1; else echo __FIREWALLD_NOT_INSTALLED__; fi");
        if (firewalld.ExitCode is 1001 or 1002)
            return (null, null, false, "Firewall state could not be checked: sudo authentication failed or is unavailable.");
        var firewalldInstalled = !firewalld.Stdout.Contains("__FIREWALLD_NOT_INSTALLED__", StringComparison.Ordinal);
        if (firewalldInstalled && firewalld.Stdout.Trim().Equals("running", StringComparison.OrdinalIgnoreCase))
            return ("firewalld", true, false, "firewalld is active.");
        if (firewalldInstalled)
            return ("firewalld", false, false, "firewalld is installed but not running.");
        if (ufwInstalled)
            return ("ufw", false, false, "UFW is installed but inactive.");

        var nft = ShellHelper.EjecutarComoRoot("if command -v nft >/dev/null 2>&1; then nft list ruleset; else echo __NFT_NOT_INSTALLED__; fi");
        if (!nft.Stdout.Contains("__NFT_NOT_INSTALLED__", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(nft.Stdout))
            return (null, null, true, "No UFW/firewalld manager found; nftables rules exist and need manual review.");

        var iptables = ShellHelper.EjecutarComoRoot("if command -v iptables >/dev/null 2>&1; then iptables -S INPUT; else echo __IPTABLES_NOT_INSTALLED__; fi");
        if (!iptables.Stdout.Contains("__IPTABLES_NOT_INSTALLED__", StringComparison.Ordinal) &&
            (iptables.Stdout.Contains("-P INPUT DROP", StringComparison.OrdinalIgnoreCase) ||
             iptables.Stdout.Contains("-P INPUT REJECT", StringComparison.OrdinalIgnoreCase)))
            return (null, null, true, "No UFW/firewalld manager found; iptables has a restrictive INPUT policy that needs review.");

        return (null, null, true, "No supported firewall manager (UFW/firewalld) was detected. Other rules and network-level filtering are not controlled here.");
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
            var config = ConfigManager.Load();
            var result = ShellHelper.EjecutarComoRoot(
                $"testparm -s {ShellQuote(config.SmbConfPath)}", ShellHelper.LongTimeoutMs);

            string stdout = result.Stdout.Trim();
            string stderr = result.Stderr.Trim();

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.WriteLine("Configuration notes / warnings:\n");
                Console.WriteLine(stderr);
                Console.WriteLine();
            }

            if (result.ExitCode == 0 && stdout.Contains("Loaded services file OK", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Configuration is valid.");
            }
            else
            {
                Console.WriteLine($"Configuration validation failed (exit {result.ExitCode}):\n{stdout}");
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

        var units = GetSambaUnitStates();
        if (units.Count == 0)
        {
            Console.WriteLine("No Samba systemd services were found.");
            return;
        }

        var active = units.Where(unit => unit.IsActive).ToList();
        var skipped = units.Where(unit => !unit.IsActive).ToList();

        if (skipped.Count > 0)
            Console.WriteLine($"Skipping inactive services: {FormatUnitList(skipped)}.");

        if (active.Count == 0)
        {
            Console.WriteLine("No active Samba services found to reload.");
            return;
        }

        bool reloadedAny = false;

        foreach (var unit in active)
        {
            try
            {
                Console.WriteLine($"Reloading {unit.DisplayName}...");
                var res = ShellHelper.EjecutarComoRoot(
                    $"systemctl reload {unit.Id}", ShellHelper.LongTimeoutMs);

                if (res.ExitCode == 0)
                {
                    Console.WriteLine($"{unit.DisplayName} reloaded successfully.");
                    reloadedAny = true;
                }
                else
                {
                    Console.WriteLine($"{unit.DisplayName} failed to reload: {FormatCommandError(res)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reloading {unit.DisplayName}: {ex.Message}");
            }
        }

        if (!reloadedAny)
            Console.WriteLine("No Samba services could be reloaded.");
    }

    static void RestartSamba()
    {
        SafeClearScreen();
        Console.WriteLine("=== Restart Samba ===\n");

        var units = GetSambaUnitStates();
        if (units.Count == 0)
        {
            Console.WriteLine("No Samba systemd services were found.");
            return;
        }

        var active = units.Where(unit => unit.IsActive).ToList();
        var stopped = units.Where(unit => !unit.IsActive).ToList();

        Console.WriteLine($"Active: {FormatUnitList(active)}");
        if (stopped.Count > 0)
            Console.WriteLine($"Stopped or failed: {FormatUnitList(stopped)}");

        List<SambaUnitState> toRestart;

        if (stopped.Count == 0)
        {
            //  está activo: reiniciar no puede arrancar nada que estuviera parado.
            toRestart = active;
        }
        else
        {
            var options = new List<string>();
            if (active.Count > 0)
                options.Add($"Restart only active services ({FormatUnitNames(active)})");

            options.Add(active.Count > 0
                ? $"Restart active services and start stopped ones ({FormatUnitNames(stopped)})"
                : $"Start stopped services ({FormatUnitNames(stopped)})");

            var selected = Choose("Choose how to restart Samba", options);
            if (selected == 0)
            {
                Console.WriteLine("Restart canceled.");
                return;
            }

            // Si no hay activos, la única opción mostrada es arrancar los parados.
            toRestart = active.Count > 0 && selected == 1 ? active : units;
        }

        if (toRestart.Count == 0)
        {
            Console.WriteLine("There are no Samba services to restart.");
            return;
        }

        bool restartedAny = false;
        bool restartFailed = false;
        var changedUnits = new List<SambaUnitState>();

        foreach (var unit in toRestart)
        {
            try
            {
                var action = unit.IsActive ? "Restarting" : "Starting";
                Console.WriteLine($"{action} {unit.DisplayName}...");
                var res = ShellHelper.EjecutarComoRoot(
                    $"systemctl restart {unit.Id}", ShellHelper.LongTimeoutMs);

                if (res.ExitCode == 0)
                {
                    var pastTense = unit.IsActive ? "restarted" : "started";
                    Console.WriteLine($"{unit.DisplayName} {pastTense} successfully.");
                    restartedAny = true;
                    changedUnits.Add(unit);
                }
                else
                {
                    Console.WriteLine($"{unit.DisplayName} failed to restart: {FormatCommandError(res)}");
                    changedUnits.Add(unit);
                    restartFailed = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error restarting {unit.DisplayName}: {ex.Message}");
                changedUnits.Add(unit);
                restartFailed = true;
                break;
            }
        }

        if (restartFailed)
        {
            Console.WriteLine("\nRestart failed. Rolling back the services that were already changed...");

            // Restauramos en orden inverso: los activos vuelven a arrancarse y
            // los que estaban parados vuelven a detenerse.
            foreach (var unit in changedUnits.AsEnumerable().Reverse())
            {
                var rollbackCommand = unit.IsActive
                    ? $"systemctl start {unit.Id}"
                    : $"systemctl stop {unit.Id}";

                var rollback = ShellHelper.EjecutarComoRoot(rollbackCommand, ShellHelper.LongTimeoutMs);
                Console.WriteLine(rollback.ExitCode == 0
                    ? $"{unit.DisplayName} restored to {(unit.IsActive ? "active" : "inactive")}."
                    : $"Could not restore {unit.DisplayName}: {FormatCommandError(rollback)}");
            }
        }

        if (!restartedAny)
            Console.WriteLine("No Samba services could be restarted.");
        else if (restartFailed)
            Console.WriteLine("Restart did not complete; completed services were rolled back.");
    }

    private sealed class SambaUnitState
    {
        public string Id { get; init; } = "";
        public string ActiveState { get; init; } = "";

        public bool IsActive => ActiveState.Equals("active", StringComparison.OrdinalIgnoreCase);

        // "smb.service" → "smb"
        public string DisplayName =>
            Id.EndsWith(".service", StringComparison.OrdinalIgnoreCase) ? Id[..^8] : Id;
    }

    // Consulta el estado real de cada unidad y descarta las que no están instaladas
    // (LoadState=not-found) y los alias que apuntan a la misma unidad.
    private static List<SambaUnitState> GetSambaUnitStates()
    {
        var states = new List<SambaUnitState>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var unit in SambaUnits)
        {
            var result = ShellHelper.Ejecutar(
                $"systemctl show {unit} --property=Id --property=LoadState --property=ActiveState 2>/dev/null");

            var properties = result.Stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(
                    parts => parts[0].Trim(),
                    parts => parts[1].Trim(),
                    StringComparer.OrdinalIgnoreCase);

            if (!properties.TryGetValue("Id", out var id) ||
                !properties.TryGetValue("LoadState", out var loadState) ||
                !properties.TryGetValue("ActiveState", out var activeState))
            {
                continue;
            }

            if (!loadState.Equals("loaded", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!seenIds.Add(id))
                continue;

            states.Add(new SambaUnitState
            {
                Id = id,
                ActiveState = activeState
            });
        }

        return states;
    }

    private static string FormatUnitList(List<SambaUnitState> units) =>
        units.Count == 0
            ? "none"
            : string.Join(", ", units.Select(unit => $"{unit.DisplayName} ({unit.ActiveState})"));

    private static string FormatUnitNames(List<SambaUnitState> units) =>
        units.Count == 0
            ? "none"
            : string.Join(", ", units.Select(unit => unit.DisplayName));

    private static string FormatCommandError((int ExitCode, string Stdout, string Stderr) result)
    {
        var message = (result.Stderr + result.Stdout).Trim();
        return string.IsNullOrWhiteSpace(message) ? $"exit code {result.ExitCode}" : message;
    }

    static void ShowSmbStatus()
    {
        SafeClearScreen();
        Console.WriteLine("=== Active SMB Connections ===\n");

        try
        {
            // smbstatus necesita root para ver las sesiones de todos los usuarios.
            var result = ShellHelper.EjecutarComoRoot("smbstatus", ShellHelper.LongTimeoutMs);

            if (result.ExitCode != 0)
            {
                var error = (result.Stderr + result.Stdout).Trim();
                Console.WriteLine($"Could not run smbstatus: {(string.IsNullOrWhiteSpace(error) ? "unknown error" : error)}");
            }
            else if (string.IsNullOrWhiteSpace(result.Stdout))
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