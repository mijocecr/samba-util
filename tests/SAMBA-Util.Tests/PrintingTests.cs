using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAMBA_Util.Helpers;
using Xunit;

namespace SAMBA_Util.Tests;

public class PrintingTests
{
    private static List<string> BaseConfig() => new()
    {
        "[global]",
        "    workgroup = WORKGROUP",
        "",
        "[docs]",
        "    path = /srv/docs",
        "    read only = yes"
    };

    [Fact]
    public void BuildPrintingLines_ActivaCupsEImpresoras()
    {
        var output = SambaConfigWriter.BuildPrintingLines(BaseConfig(), true);

        Assert.Contains("    printing = cups", output);
        Assert.Contains("    printcap name = cups", output);
        Assert.Contains("    load printers = yes", output);
        Assert.Contains("[printers]", output);
        Assert.Contains("    printable = yes", output);
        Assert.Contains("[print$]", output);
        Assert.Contains("    path = /var/lib/samba/printers", output);
    }

    [Fact]
    public void BuildPrintingLines_DesactivarRestauraContenidoOriginal()
    {
        var original = BaseConfig();
        var enabled = SambaConfigWriter.BuildPrintingLines(original, true);
        var disabled = SambaConfigWriter.BuildPrintingLines(enabled, false);

        Assert.Equal(
            original.Where(line => !string.IsNullOrWhiteSpace(line)),
            disabled.Where(line => !string.IsNullOrWhiteSpace(line)));
        Assert.DoesNotContain(disabled, line => line.Contains("SAMBA-UTIL PRINTING", StringComparison.Ordinal));
        Assert.DoesNotContain("[printers]", disabled);
        Assert.DoesNotContain("[print$]", disabled);
    }

    [Fact]
    public void BuildPrintingLines_EsIdempotente()
    {
        var enabled = SambaConfigWriter.BuildPrintingLines(BaseConfig(), true);
        var enabledAgain = SambaConfigWriter.BuildPrintingLines(enabled, true);

        Assert.Equal(enabled, enabledAgain);
    }

    [Fact]
    public void BuildPrintingLines_RespetaSeccionPrintersExistente()
    {
        var input = BaseConfig();
        input.Add("");
        input.Add("[printers]");
        input.Add("    printable = yes");
        input.Add("    path = /var/spool/samba");

        var output = SambaConfigWriter.BuildPrintingLines(input, true);

        Assert.Single(output, line => line.Equals("[printers]", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("    printing = cups", output);
    }

    [Fact]
    public void BuildPrintingLines_CompletaPrintersNoImprimible()
    {
        var input = BaseConfig();
        input.Add("");
        input.Add("[printers]");
        input.Add("    printable = no");
        input.Add("    path = /var/spool/samba");

        var output = SambaConfigWriter.BuildPrintingLines(input, true);
        var disabled = SambaConfigWriter.BuildPrintingLines(output, false);

        Assert.Contains("    printable = yes", output);
        Assert.Equal(
            input.Where(line => !string.IsNullOrWhiteSpace(line)),
            disabled.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    [Fact]
    public void BuildPrintingLines_AñadePathAPrintersSinRuta()
    {
        var input = BaseConfig();
        input.Add("");
        input.Add("[printers]");
        input.Add("    printable = yes");

        var output = SambaConfigWriter.BuildPrintingLines(input, true);

        Assert.Contains("    path = /var/spool/samba", output);
        Assert.Single(output, line => line.Equals("[printers]", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IsPrintingEnabled_DetectaSeccionImprimible()
    {
        var path = WriteTempConfig(
            "[global]",
            "    workgroup = WORKGROUP",
            "",
            "[printers]",
            "    printable = yes");

        try
        {
            Assert.True(SambaConfigWriter.IsPrintingEnabled(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsPrintingEnabled_IgnoraSeccionNoImprimible()
    {
        var path = WriteTempConfig(
            "[global]",
            "    workgroup = WORKGROUP",
            "",
            "[printers]",
            "    printable = no");

        try
        {
            Assert.False(SambaConfigWriter.IsPrintingEnabled(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsPrintingEnabled_RespetaLoadPrintersDesactivado()
    {
        var path = WriteTempConfig(
            "[global]",
            "    load printers = no",
            "",
            "[printers]",
            "    printable = yes");

        try
        {
            Assert.False(SambaConfigWriter.IsPrintingEnabled(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempConfig(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"samba-util-test-{Guid.NewGuid():N}.conf");
        File.WriteAllLines(path, lines);
        return path;
    }
}
