namespace SAMBA_Util.Helpers;

using System;
using System.IO;
using System.Linq;

// Comprueba el entorno del sistema para saber si Samba está instalado.
// No necesita privilegios de root: solo inspecciona binarios y unidades
// systemd, por lo que puede ejecutarse antes de autenticar con sudo.
public static class SambaEnvironment
{
    // Rutas habituales del binario del servidor Samba en las principales
    // distribuciones (Debian/Ubuntu, Fedora/RHEL, Arch, openSUSE, Alpine…).
    private static readonly string[] KnownSmbdPaths =
    {
        "/usr/sbin/smbd",
        "/sbin/smbd",
        "/usr/bin/smbd",
        "/usr/local/sbin/smbd",
        "/usr/local/bin/smbd"
    };

    // Hechos recogidos del sistema, separados de la decisión para poder
    // probarlos sin tocar el sistema real.
    public readonly record struct Probe(
        bool SmbdOnPath,
        bool SmbdAtKnownPath,
        bool SystemdUnitPresent,
        bool TestparmOnPath);

    // Samba está instalado si aparece cualquiera de sus componentes conocidos.
    public static bool IsInstalled(in Probe probe) =>
        probe.SmbdOnPath ||
        probe.SmbdAtKnownPath ||
        probe.SystemdUnitPresent ||
        probe.TestparmOnPath;

    // Recoge los hechos del sistema actual. Es la única parte que ejecuta
    // comandos; la decisión vive en IsInstalled(Probe).
    public static Probe Inspect()
    {
        if (!OperatingSystem.IsLinux())
        {
            // En plataformas no Linux no bloqueamos el arranque: el programa
            // está pensado para Linux, pero no debe impedir su ejecución.
            return new Probe(true, false, false, false);
        }

        var smbd = ShellHelper.Ejecutar("command -v smbd 2>/dev/null");
        var testparm = ShellHelper.Ejecutar("command -v testparm 2>/dev/null");
        var unit = ShellHelper.Ejecutar(
            "systemctl list-unit-files smbd.service smb.service 2>/dev/null");

        return new Probe(
            SmbdOnPath: smbd.ExitCode == 0 && !string.IsNullOrWhiteSpace(smbd.Stdout),
            SmbdAtKnownPath: KnownSmbdPaths.Any(File.Exists),
            SystemdUnitPresent: HasSambaUnit(unit.Stdout),
            TestparmOnPath: testparm.ExitCode == 0 && !string.IsNullOrWhiteSpace(testparm.Stdout));
    }

    public static bool IsSambaInstalled() => IsInstalled(Inspect());

    // Detecta las unidades smbd.service/smb.service en la salida de
    // `systemctl list-unit-files`. Es tolerante con formatos y estados.
    public static bool HasSambaUnit(string? systemctlOutput)
    {
        if (string.IsNullOrWhiteSpace(systemctlOutput))
            return false;

        return systemctlOutput
            .Split('\n')
            .Select(line => line.Trim())
            .Any(line =>
                line.StartsWith("smbd.service", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("smb.service", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------
    // HERRAMIENTAS DE CLIENTE SMB (smbclient / rpcclient)
    // ---------------------------------------------------------
    // En varias distribuciones (por ejemplo Debian/Ubuntu) el cliente SMB vive
    // en un paquete aparte (`smbclient`) del servidor (`samba`). Sin él la
    // aplicación no puede enumerar recursos compartidos, así que conviene
    // detectarlo para mostrar un mensaje claro en lugar de pedir credenciales
    // cuando en realidad falta el programa.

    // Hechos recogidos del sistema, separados de la decisión para poder
    // probarlos sin tocar el sistema real.
    public readonly record struct SmbClientProbe(
        bool SmbclientOnPath,
        bool RpcclientOnPath);

    // smbclient enumera recursos en equipos que no son Windows.
    public static bool IsSmbClientAvailable(in SmbClientProbe probe) => probe.SmbclientOnPath;

    // rpcclient enumera recursos en equipos Windows.
    public static bool IsRpcClientAvailable(in SmbClientProbe probe) => probe.RpcclientOnPath;

    // Recoge los hechos del sistema actual. Es la única parte que ejecuta
    // comandos; la decisión vive en las funciones puras de arriba.
    public static SmbClientProbe InspectSmbClient()
    {
        if (!OperatingSystem.IsLinux())
        {
            // En plataformas no Linux no bloqueamos: el programa está pensado
            // para Linux, pero no debe impedir su ejecución.
            return new SmbClientProbe(true, true);
        }

        var smbclient = ShellHelper.Ejecutar("command -v smbclient 2>/dev/null");
        var rpcclient = ShellHelper.Ejecutar("command -v rpcclient 2>/dev/null");

        return new SmbClientProbe(
            SmbclientOnPath: smbclient.ExitCode == 0 && !string.IsNullOrWhiteSpace(smbclient.Stdout),
            RpcclientOnPath: rpcclient.ExitCode == 0 && !string.IsNullOrWhiteSpace(rpcclient.Stdout));
    }

    public static bool HasSmbClient() => IsSmbClientAvailable(InspectSmbClient());

    public static bool HasRpcClient() => IsRpcClientAvailable(InspectSmbClient());
}
