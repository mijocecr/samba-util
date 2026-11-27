using System;
using System.IO;
using SAMBA_Util.Helpers;
using Xunit;

namespace SAMBA_Util.Tests;

public sealed class CliLogIsolationTests
{
    [Fact]
    public void HelperDiagnosticsGoToLogNotConsole()
    {
        var tempHome = Path.Combine(
            Path.GetTempPath(), "samba-util-log-test-" + Guid.NewGuid().ToString("N"));
        var originalHome = Environment.GetEnvironmentVariable("HOME");
        var originalOut = Console.Out;

        try
        {
            Directory.CreateDirectory(tempHome);
            Environment.SetEnvironmentVariable("HOME", tempHome);
            var output = new StringWriter();
            Console.SetOut(output);

            SambaConfigReader.LoadShares(Path.Combine(tempHome, "missing.conf"));

            Assert.DoesNotContain("[CONF]", output.ToString());
            var logPath = Path.Combine(tempHome, ".config", "samba-util", "samba-util.log");
            Assert.True(File.Exists(logPath));
            Assert.Contains("[CONF]", File.ReadAllText(logPath));
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.SetEnvironmentVariable("HOME", originalHome);
            try { Directory.Delete(tempHome, recursive: true); } catch (IOException) { }
        }
    }
}
