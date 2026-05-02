using SAMBA_Util.Models;

namespace SAMBA_Util.Helpers;

public static class ShareValidator
{
    public static string? ValidateShare(Share s)
    {
        if (string.IsNullOrWhiteSpace(s.Path))
            return "The share has no assigned path.";

        var (owner, group, mode) = FileSystemHelper.GetPermissions(s.Path);

        // ⭐ Validate that mode has exactly 3 characters
        if (string.IsNullOrWhiteSpace(mode) || mode.Length != 3)
            return "File system permissions could not be read.";

        // ⭐ Now it's safe to access mode[0], mode[1], mode[2]
        bool ownerWrite = mode[0] == '7' || mode[0] == '6' || mode[0] == '2';
        bool groupWrite = mode[1] == '7' || mode[1] == '6' || mode[1] == '2';
        bool otherWrite = mode[2] == '7' || mode[2] == '6' || mode[2] == '2';

        // Samba allows write but Linux does not
        if (!s.ReadOnly && !(ownerWrite || groupWrite || otherWrite))
            return "Samba allows write access, but the file system does NOT.";

        // Guests allowed but Linux does not allow write for others
        if (s.AllowGuests && !otherWrite)
            return "Guests are allowed, but the file system does not allow write access for others.";

        // Force user but the directory belongs to another owner
        if (!string.IsNullOrWhiteSpace(s.ForceUser) && s.ForceUser != owner)
            return $"Samba forces user '{s.ForceUser}', but the directory belongs to '{owner}'.";

        return null;
    }
}