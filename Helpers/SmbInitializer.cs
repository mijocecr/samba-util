namespace SAMBA_Util.Helpers;

using System.IO;
using System.Threading.Tasks;


public static class SmbInitializer
{
    private const string DefaultSmbConf = @"[global]
   workgroup = WORKGROUP
   server string = Samba Server %v
   netbios name = SAMBA-HOST
   security = user
   map to guest = Bad User
   dns proxy = no
   logging = systemd

[homes]
   comment = Home Directories
   browseable = no
   writable = yes
   create mask = 0700
   directory mask = 0700
";

    public static async Task InitializeEnvironmentAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                string sambaDir = "/etc/samba";
                string confPath = System.IO.Path.Combine(sambaDir, "smb.conf");

                // Verificar o crear directorio en el host
                var checkDir = ShellHelper.EjecutarComoRoot($"mkdir -p {sambaDir}");
                
                // Si el archivo smb.conf no existe, se genera la plantilla base
                if (!File.Exists(confPath))
                {
                    File.WriteAllText("/tmp/smb.conf.default", DefaultSmbConf);
                    ShellHelper.EjecutarComoRoot($"cp /tmp/smb.conf.default {confPath} && chmod 644 {confPath}");
                }
            }
            catch
            {
                // Manejo de excepciones en segundo plano sin afectar la UI
            }
        });
    }
}