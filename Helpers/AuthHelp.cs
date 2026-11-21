namespace SAMBA_Util.Helpers;

// Avisos que se añaden a los mensajes de autenticación rechazada. Se muestran
// de forma condicional porque la detección de SO puede ser imprecisa (un macOS
// con OpenSSH estándar, por ejemplo, suele identificarse como Linux).
public static class AuthHelp
{
    public const string PlatformNotice =
        "\n\nIf this is a Windows server, use a local account with the appropriate " +
        "share and NTFS permissions. Microsoft accounts and the built-in Administrator " +
        "account often cannot authenticate to SMB shares; a dedicated local user " +
        "usually resolves these issues." +
        "\n\nIf this is a macOS server, the account needs SMB-NT enabled for Windows " +
        "File Sharing: sudo pwpolicy -u <user> -sethashtypes SMB-NT on, then set the " +
        "password again. If SMB3 authentication still fails, Apple documents forcing " +
        "SMB2 on the server with ProtocolVersionMap=2 in com.apple.smb.server.plist " +
        "(sudo scutil --prefs com.apple.smb.server.plist, d.add ProtocolVersionMap # 2, " +
        "set /, commit, apply, quit, then restart smbd).";
}
