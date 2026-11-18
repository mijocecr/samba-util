using SAMBA_Util.Helpers;
using Xunit;

namespace SAMBA_Util.Tests;

public class SambaEnvironmentTests
{
    [Theory]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, false, true, false, true)]
    [InlineData(false, false, false, true, true)]
    [InlineData(false, false, false, false, false)]
    public void IsInstalled_CombinaLosComponentes(
        bool smbdOnPath, bool smbdAtKnownPath, bool systemdUnit, bool testparm, bool expected)
    {
        var probe = new SambaEnvironment.Probe(smbdOnPath, smbdAtKnownPath, systemdUnit, testparm);

        Assert.Equal(expected, SambaEnvironment.IsInstalled(probe));
    }

    [Theory]
    [InlineData("smbd.service enabled\nsmb.service enabled\n", true)]
    [InlineData("smb.service enabled\n", true)]
    [InlineData("nmbd.service enabled\n", false)]
    [InlineData("0 unit files listed.\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasSambaUnit_DetectaUnidades(string? output, bool expected)
    {
        Assert.Equal(expected, SambaEnvironment.HasSambaUnit(output));
    }
}
