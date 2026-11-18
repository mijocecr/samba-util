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
}
