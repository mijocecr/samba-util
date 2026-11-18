using SAMBA_Util.Helpers;
using Xunit;

namespace SAMBA_Util.Tests;

public class AccessControlHelperTests
{
    [Theory]
    [InlineData("Enforcing", true)]
    [InlineData("Permissive", true)]
    [InlineData("Disabled", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSelinuxActive_DetectaElEstado(string? output, bool expected)
    {
        Assert.Equal(expected, AccessControlHelper.IsSelinuxActive(output));
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("yes\n", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAppArmorActive_DetectaElEstado(string? output, bool expected)
    {
        Assert.Equal(expected, AccessControlHelper.IsAppArmorActive(output));
    }

    [Theory]
    [InlineData("#include <local/usr.sbin.smbd>\n/usr/sbin/smbd { }", true)]
    [InlineData("/usr/sbin/smbd { }", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasLocalInclude_DetectaLaInclusion(string? profile, bool expected)
    {
        Assert.Equal(expected, AccessControlHelper.HasLocalInclude(profile));
    }

    [Fact]
    public void BuildLocalProfile_GeneraBloqueConLasReglasDeLaRuta()
    {
        var result = AccessControlHelper.BuildLocalProfile("", "/srv/data");

        Assert.Contains("# BEGIN SAMBA-Util", result);
        Assert.Contains("# END SAMBA-Util", result);
        Assert.Contains("\"/srv/data/\" r,", result);
        Assert.Contains("\"/srv/data/**\" rwk,", result);
    }

    [Fact]
    public void BuildLocalProfile_ConservaElContenidoExistente()
    {
        var existing = "/custom/path r,\n";

        var result = AccessControlHelper.BuildLocalProfile(existing, "/srv/data");

        Assert.Contains("/custom/path r,", result);
        Assert.Contains("\"/srv/data/**\" rwk,", result);
    }

    [Fact]
    public void BuildLocalProfile_EsIdempotente()
    {
        var once = AccessControlHelper.BuildLocalProfile("", "/srv/data");
        var twice = AccessControlHelper.BuildLocalProfile(once, "/srv/data");

        Assert.Equal(once, twice);
    }

    [Fact]
    public void BuildLocalProfile_ReemplazaElBloqueAnteriorSiCambiaLaRuta()
    {
        var first = AccessControlHelper.BuildLocalProfile("", "/srv/old");
        var second = AccessControlHelper.BuildLocalProfile(first, "/srv/new");

        Assert.DoesNotContain("/srv/old", second);
        Assert.Contains("/srv/new", second);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(second, "# BEGIN SAMBA-Util"));
    }

    [Fact]
    public void BuildLocalProfile_EscapaComillasEnLaRuta()
    {
        var result = AccessControlHelper.BuildLocalProfile("", "/srv/we\"ird");

        Assert.Contains("\\\"", result);
    }

    [Theory]
    [InlineData("/srv/data", "/srv/data")]
    [InlineData("/srv/we\"ird", "/srv/we\\\"ird")]
    [InlineData("/srv/back\\slash", "/srv/back\\\\slash")]
    public void EscapeAppArmorPath_EscapaCaracteresEspeciales(string input, string expected)
    {
        Assert.Equal(expected, AccessControlHelper.EscapeAppArmorPath(input));
    }
}
