using System;
using System.IO;
using System.Text.Json;

namespace SAMBA_Util.Helpers;

public static class ConfigManager
{
    private static readonly string ConfigPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config/samba-util/config.json");

    public class AppConfig
    {
        public string SmbConfPath { get; set; } = "/etc/samba/smb.conf";
        public string DefaultPermissions { get; set; } = "777";
        
        // Propiedad añadida para el protocolo mínimo de Samba
        public string MinProtocol { get; set; } = "SMB2";
    }

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return new AppConfig();

            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    // Valores admitidos por la app para el protocolo mínimo (cliente y servidor).
    public static string NormalizeProtocol(string? value)
    {
        return value?.Trim().ToUpperInvariant() switch
        {
            "SMB3" => "SMB3",
            "NT1" or "SMB1" => "NT1",
            _ => "SMB2"
        };
    }

    // Opción que se pasa a smbclient para respetar el protocolo configurado.
    public static string ClientMinProtocolOption() =>
        $"--option='client min protocol={NormalizeProtocol(Load().MinProtocol)}'";

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}