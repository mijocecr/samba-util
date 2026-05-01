using System;
using System.Diagnostics;
using System.Text;

namespace SAMBA_Util.Helpers;

public static class ShellHelper
{
   
    public static (int ExitCode, string Stdout, string Stderr) EjecutarComoRoot(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sudo",
            Arguments = $"-S {command}",
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

        // Enviar contraseña como en iSCSI-util
        if (!string.IsNullOrEmpty(Credenciales.AdminPassword))
        {
            var pass = Credenciales.AdminPassword.TrimEnd('\r', '\n');
            process.StandardInput.WriteLine(pass);
            process.StandardInput.Flush();
            process.StandardInput.Close();
        }

        const int timeoutMs = 15000;
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(); } catch { }
            return (1, "", "Timeout");
        }

        string stdout = outputBuilder.ToString();
        string stderr = errorBuilder.ToString();

        return (process.ExitCode, stdout, stderr);
    }


}
