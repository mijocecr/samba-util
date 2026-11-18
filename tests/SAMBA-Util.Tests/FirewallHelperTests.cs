using SAMBA_Util.Helpers;
using Xunit;

namespace SAMBA_Util.Tests;

public class FirewallHelperTests
{
    [Theory]
    [InlineData("Status: active\n", true)]
    [InlineData("Status: inactive\n", false)]
    [InlineData("ERROR: not installed", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsUfwActive_DetectaElEstado(string? output, bool expected)
    {
        Assert.Equal(expected, FirewallHelper.IsUfwActive(output));
    }

    [Theory]
    [InlineData("running", true)]
    [InlineData("running\n", true)]
    [InlineData("not running", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsFirewalldRunning_DetectaElEstado(string? output, bool expected)
    {
        Assert.Equal(expected, FirewallHelper.IsFirewalldRunning(output));
    }

    [Fact]
    public void ParseActiveZones_ExtraeSoloLosNombresDeZona()
    {
        var output = "public\n  interfaces: eth0\nwork\n  interfaces: eth1\n";

        var zones = FirewallHelper.ParseActiveZones(output);

        Assert.Equal(new[] { "public", "work" }, zones);
    }

    [Fact]
    public void ParseActiveZones_IgnoraEntradasSangradas()
    {
        var output = "public\n  interfaces: wlan0\n";

        var zones = FirewallHelper.ParseActiveZones(output);

        Assert.Equal(new[] { "public" }, zones);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n")]
    public void ParseActiveZones_DevuelveVacioSinSalida(string? output)
    {
        Assert.Empty(FirewallHelper.ParseActiveZones(output));
    }

    [Theory]
    [InlineData("-P INPUT DROP\n", true)]
    [InlineData("-P INPUT REJECT\n", true)]
    [InlineData("-P INPUT ACCEPT\n-A INPUT -p tcp --dport 445 -j ACCEPT\n", false)]
    [InlineData(null, false)]
    public void HasRestrictiveInputPolicy_DetectaPoliticaRestrictiva(string? output, bool expected)
    {
        Assert.Equal(expected, FirewallHelper.HasRestrictiveInputPolicy(output));
    }
}
