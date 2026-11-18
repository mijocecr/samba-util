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

    // Valores por defecto para usershares.conf. smb.conf puede incluir este
    // archivo (include = /etc/samba/usershares.conf). Si el include apunta a un
    // archivo inexistente, Samba lo ignora pero SAMBA-Util abortaba al leer la
    // configuración; lo creamos con una configuración usershare razonable.
    private const string DefaultUsersharesConf = @"[global]
   usershare path = /var/lib/samba/usershares
   usershare max shares = 100
   usershare allow guests = yes
   usershare owner only = yes
   usershare prefix deny list = /etc /dev /sys /proc
   usershare prefix allow list = /home /srv /mnt /media /var
";

    public static Task InitializeEnvironmentAsync()
    {
        return Task.Run(() =>
        {
            var configuredPath = ConfigManager.Load().SmbConfPath;
            if (string.IsNullOrWhiteSpace(configuredPath))
                throw new InvalidOperationException("The smb.conf path is not configured.");

            var confPath = Path.GetFullPath(configuredPath);
            var sambaDir = Path.GetDirectoryName(confPath);
            if (string.IsNullOrWhiteSpace(sambaDir))
                throw new InvalidOperationException($"The smb.conf path is not valid: '{configuredPath}'.");

            var createDirectory = ShellHelper.EjecutarComoRoot(
                $"mkdir -p {ShellQuote(sambaDir)}");
            EnsureSuccess(createDirectory, "Could not create the Samba configuration directory");

            EnsureRootFile(confPath, DefaultSmbConf, "smb.conf");

            // smb.conf puede incluir usershares.conf. Si el archivo no existe,
            // la lectura de la configuración fallaba; lo creamos antes de
            // arrancar los servicios y de cargar los recursos compartidos.
            EnsureRootFile(
                Path.Combine(sambaDir, "usershares.conf"),
                DefaultUsersharesConf,
                "usershares.conf");

            StartSambaServices();

            // Asegura la regla Samba en el cortafuegos salvo que el usuario lo
            // haya desactivado en la configuración.
            if (ConfigManager.Load().ManageSambaFirewallRule)
            {
                var firewall = FirewallHelper.EnsureSambaAllowed();
                Console.WriteLine($"[SMB INIT] Firewall: {firewall.Message}");
            }
        });
    }

    // Crea 'path' con 'content' si aún no existe (como root, modo 644) y
    // comprueba que queda disponible. No sobreescribe archivos existentes.
    private static void EnsureRootFile(string path, string content, string label)
    {
        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, content);

            var create = ShellHelper.EjecutarComoRoot(
                $"if [ ! -e {ShellQuote(path)} ]; then install -m 644 {ShellQuote(temp)} {ShellQuote(path)}; fi");
            EnsureSuccess(create, $"Could not create {label}");

            var verify = ShellHelper.EjecutarComoRoot(
                $"test -f {ShellQuote(path)}");
            EnsureSuccess(verify, $"Could not find {label} after initialization");
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static void EnsureSuccess((int ExitCode, string Stdout, string Stderr) result, string message)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{message}: {result.Stderr}{result.Stdout}");
    }

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\''", System.StringComparison.Ordinal) + "'";

    // Arranca (y habilita) los servicios de Samba usando el gestor disponible
    // en el sistema: systemd, SysV/`service` o OpenRC. Se expone para que la
    // vista Status pueda reintentar el arranque cuando el servicio está parado.
    public static void StartSambaServices()
    {
        if (File.Exists("/bin/systemctl") || File.Exists("/usr/bin/systemctl"))
        {
            var smbd = ShellHelper.EjecutarComoRoot(
                "systemctl enable --now smbd.service", ShellHelper.LongTimeoutMs);
            if (smbd.ExitCode != 0)
                smbd = ShellHelper.EjecutarComoRoot(
                    "systemctl enable --now smb.service", ShellHelper.LongTimeoutMs);
            EnsureSuccess(smbd, "Could not enable or start the smbd/smb service");

            var nmbd = ShellHelper.EjecutarComoRoot(
                "systemctl enable --now nmbd.service", ShellHelper.LongTimeoutMs);
            if (nmbd.ExitCode != 0)
                nmbd = ShellHelper.EjecutarComoRoot(
                    "systemctl enable --now nmb.service", ShellHelper.LongTimeoutMs);
            if (nmbd.ExitCode != 0)
                Console.WriteLine($"[SMB INIT] NetBIOS nmbd is not available; IP-based SMB still depends on smbd. {nmbd.Stderr}");

            return;
        }

        if (File.Exists("/sbin/service") || File.Exists("/usr/sbin/service"))
        {
            var smbd = ShellHelper.EjecutarComoRoot(
                "service smbd start", ShellHelper.LongTimeoutMs);
            if (smbd.ExitCode != 0)
                smbd = ShellHelper.EjecutarComoRoot(
                    "service smb start", ShellHelper.LongTimeoutMs);
            EnsureSuccess(smbd, "Could not start the smbd/smb service");

            var nmbd = ShellHelper.EjecutarComoRoot(
                "service nmbd start", ShellHelper.LongTimeoutMs);
            if (nmbd.ExitCode != 0)
                nmbd = ShellHelper.EjecutarComoRoot(
                    "service nmb start", ShellHelper.LongTimeoutMs);
            if (nmbd.ExitCode != 0)
                Console.WriteLine($"[SMB INIT] NetBIOS nmbd is not available; IP-based SMB still depends on smbd. {nmbd.Stderr}");

            return;
        }

        if (File.Exists("/sbin/rc-service"))
        {
            var samba = ShellHelper.EjecutarComoRoot(
                "rc-update add samba default && rc-service samba start", ShellHelper.LongTimeoutMs);
            EnsureSuccess(samba, "Could not enable or start Samba");
            return;
        }

        throw new InvalidOperationException("No compatible service manager was found to start Samba.");
    }
}
