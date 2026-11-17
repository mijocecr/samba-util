using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAMBA_Util.Helpers;
using Xunit;

namespace SAMBA_Util.Tests;

public class MapToGuestTests
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
    public void BuildMapToGuestLines_ActivaBadUser()
    {
        var output = SambaConfigWriter.BuildMapToGuestLines(BaseConfig(), true);

        Assert.Contains("    map to guest = Bad User", output);
    }

    [Fact]
    public void BuildMapToGuestLines_DesactivaConNever()
    {
        var output = SambaConfigWriter.BuildMapToGuestLines(BaseConfig(), false);

        Assert.Contains("    map to guest = Never", output);
        Assert.DoesNotContain(output, line => line.Contains("Bad User", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildMapToGuestLines_ReemplazaValorExistenteYQuitaDuplicados()
    {
        var input = new List<string>
        {
            "[global]",
            "    map to guest = Never",
            "    map to guest = Bad Password",
            "",
            "[docs]",
            "    path = /srv/docs"
        };

        var output = SambaConfigWriter.BuildMapToGuestLines(input, true);

        Assert.Equal(1, output.Count(line => line.Contains("map to guest", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("    map to guest = Bad User", output);
    }

    [Fact]
    public void BuildMapToGuestLines_EsIdempotente()
    {
        var once = SambaConfigWriter.BuildMapToGuestLines(BaseConfig(), true);
        var twice = SambaConfigWriter.BuildMapToGuestLines(once, true);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void BuildMapToGuestLines_CreaGlobalSiNoExiste()
    {
        var output = SambaConfigWriter.BuildMapToGuestLines(new List<string>(), true);

        Assert.Contains("[global]", output);
        Assert.Contains("    map to guest = Bad User", output);
    }

    [Fact]
    public void IsMapToGuestBadUser_LeeElValorDeSmbConf()
    {
        var badUser = WriteTemp("[global]\n    map to guest = Bad User\n");
        try
        {
            Assert.True(SambaConfigWriter.IsMapToGuestBadUser(badUser));
        }
        finally
        {
            File.Delete(badUser);
        }

        var never = WriteTemp("[global]\n    map to guest = Never\n");
        try
        {
            Assert.False(SambaConfigWriter.IsMapToGuestBadUser(never));
        }
        finally
        {
            File.Delete(never);
        }

        var absent = WriteTemp("[global]\n    workgroup = WORKGROUP\n");
        try
        {
            Assert.False(SambaConfigWriter.IsMapToGuestBadUser(absent));
        }
        finally
        {
            File.Delete(absent);
        }
    }

    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"samba-util-test-{Guid.NewGuid():N}.conf");
        File.WriteAllText(path, content);
        return path;
    }
}
