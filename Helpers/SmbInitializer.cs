namespace SAMBA_Util.Helpers;

using System;
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
   guest account = nobody
   browseable = yes
   dns proxy = no
   logging = systemd
";

    public static Task InitializeEnvironmentAsync()
    {
        return Task.Run(() =>
        {
            var configuredPath = ConfigManager.Load().SmbConfPath;
            if (string.IsNullOrWhiteSpace(configuredPath))
                throw new InvalidOperationException("La ruta de smb.conf no está configurada.");

            var confPath = Path.GetFullPath(configuredPath);
            var sambaDir = Path.GetDirectoryName(confPath);
            if (string.IsNullOrWhiteSpace(sambaDir))
                throw new InvalidOperationException($"La ruta de smb.conf no es válida: '{configuredPath}'.");

            var tempPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempPath, DefaultSmbConf);

                var createDirectory = ShellHelper.EjecutarComoRoot(
                    $"mkdir -p {ShellQuote(sambaDir)}");
                EnsureSuccess(createDirectory, "No se pudo crear el directorio de configuración de Samba");

                var createConfig = ShellHelper.EjecutarComoRoot(
                    $"if [ ! -e {ShellQuote(confPath)} ]; then install -m 644 {ShellQuote(tempPath)} {ShellQuote(confPath)}; fi");
                EnsureSuccess(createConfig, "No se pudo crear smb.conf");

                var verifyConfig = ShellHelper.EjecutarComoRoot(
                    $"test -f {ShellQuote(confPath)}");
                EnsureSuccess(verifyConfig, "No se encontró smb.conf después de la inicialización");

                StartSambaServices();
            }
            finally
            {
                File.Delete(tempPath);
            }
        });
    }

    private static void EnsureSuccess((int ExitCode, string Stdout, string Stderr) result, string message)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{message}: {result.Stderr}{result.Stdout}");
    }

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\''", System.StringComparison.Ordinal) + "'";

    private static void StartSambaServices()
    {
        if (File.Exists("/bin/systemctl") || File.Exists("/usr/bin/systemctl"))
        {
            var smbd = ShellHelper.EjecutarComoRoot("systemctl enable --now smbd.service");
            if (smbd.ExitCode != 0)
                smbd = ShellHelper.EjecutarComoRoot("systemctl enable --now smb.service");
            EnsureSuccess(smbd, "No se pudo habilitar ni iniciar el servicio smbd/smb");

            var nmbd = ShellHelper.EjecutarComoRoot("systemctl enable --now nmbd.service");
            if (nmbd.ExitCode != 0)
                nmbd = ShellHelper.EjecutarComoRoot("systemctl enable --now nmb.service");
            if (nmbd.ExitCode != 0)
                Console.WriteLine($"[SMB INIT] NetBIOS nmbd no está disponible; SMB por IP sigue dependiendo de smbd. {nmbd.Stderr}");

            return;
        }

        if (File.Exists("/sbin/service") || File.Exists("/usr/sbin/service"))
        {
            var smbd = ShellHelper.EjecutarComoRoot("service smbd start");
            if (smbd.ExitCode != 0)
                smbd = ShellHelper.EjecutarComoRoot("service smb start");
            EnsureSuccess(smbd, "No se pudo iniciar el servicio smbd/smb");

            var nmbd = ShellHelper.EjecutarComoRoot("service nmbd start");
            if (nmbd.ExitCode != 0)
                nmbd = ShellHelper.EjecutarComoRoot("service nmb start");
            if (nmbd.ExitCode != 0)
                Console.WriteLine($"[SMB INIT] NetBIOS nmbd no está disponible; SMB por IP sigue dependiendo de smbd. {nmbd.Stderr}");

            return;
        }

        if (File.Exists("/sbin/rc-service"))
        {
            var samba = ShellHelper.EjecutarComoRoot("rc-update add samba default && rc-service samba start");
            EnsureSuccess(samba, "No se pudo habilitar ni iniciar Samba");
            return;
        }

        throw new InvalidOperationException("No se detectó un gestor de servicios compatible para iniciar Samba.");
    }
}