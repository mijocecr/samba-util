using System.Diagnostics;
using System.Text;

namespace SAMBA_Util.Helpers;

public static class ShellHelper
{
    public static (int ExitCode, string Output, string Error) EjecutarComoRoot(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            Arguments = "-S " + args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null) outputBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null) errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Enviar contraseña IGUAL que iscsi-util
        if (!string.IsNullOrEmpty(Credenciales.AdminPassword))
        {
            process.StandardInput.WriteLine(Credenciales.AdminPassword);
            process.StandardInput.Flush();
            process.StandardInput.Close();
        }

        process.WaitForExit();

        return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }
}