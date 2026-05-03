using SAMBA_Util.Models;

namespace SAMBA_Util.Helpers;

public static class ShareValidator
{
    public static string? ValidateShare(Share s)
    {
        if (string.IsNullOrWhiteSpace(s.Path))
            return "The share has no assigned path.";

        var (owner, group, mode) = FileSystemHelper.GetPermissions(s.Path);

        // Directory not found (FileSystemHelper returns "", "", "")
        if (owner == "" && group == "" && mode == "")
            return "Directory does not exist.";

        // stat error ("?", "?", "?")
        if (mode.Contains('?'))
            return "File system permissions could not be read.";

        // Validate mode format
        if (string.IsNullOrWhiteSpace(mode) || mode.Length < 3)
            return "File system permissions could not be read.";

        // Normalize mode to 3 digits
        mode = mode[^3..]; // last 3 digits

        // Extract write bits
        bool ownerWrite = mode[0] == '7' || mode[0] == '6' || mode[0] == '2';
        bool groupWrite = mode[1] == '7' || mode[1] == '6' || mode[1] == '2';
        bool otherWrite = mode[2] == '7' || mode[2] == '6' || mode[2] == '2';

        // Samba allows write but Linux does not
        if (!s.ReadOnly && !(ownerWrite || groupWrite || otherWrite))
            return "Samba allows write access, but the file system does NOT.";

        // Guests allowed but Linux does not allow write for others
        if (s.AllowGuests && !otherWrite)
            return "Guests allowed, but filesystem blocks write for others.";

        // Force user mismatch
        if (!string.IsNullOrWhiteSpace(s.ForceUser) && s.ForceUser != owner)
            return $"Samba forces user '{s.ForceUser}', but the directory belongs to '{owner}'.";

        // Compare with configured default permissions
        var config = ConfigManager.Load();
        string expected = config.DefaultPermissions ?? "0755";

        // Normalize both to 3 digits
        string normMode = mode.PadLeft(3, '0');
        string normExpected = expected.Trim().TrimStart('0').PadLeft(3, '0');

        if (normMode != normExpected)
            return $"Folder permissions are {mode}, expected {expected}.";

        return null;
    }
}
