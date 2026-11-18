using SAMBA_Util.Helpers;
using Xunit;

namespace SAMBA_Util.Tests;

public class ServiceInspectorTests
{
    [Fact]
    public void ParseShow_LeeLosEstados()
    {
        var state = ServiceInspector.ParseShow(
            "LoadState=loaded\nActiveState=active\nUnitFileState=enabled\n");

        Assert.NotNull(state);
        Assert.Equal("loaded", state!.Value.LoadState);
        Assert.Equal("active", state.Value.ActiveState);
        Assert.Equal("enabled", state.Value.UnitFileState);
    }

    [Theory]
    [InlineData("LoadState=not-found\nActiveState=inactive\nUnitFileState=\n")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("texto sin claves=valor\n")]
    public void ParseShow_DevuelveNullSiNoHayUnidad(string? output)
    {
        Assert.Null(ServiceInspector.ParseShow(output));
    }

    [Fact]
    public void ParseShow_ToleraEspaciosYMayusculas()
    {
        var state = ServiceInspector.ParseShow("  LoadState = loaded \n ActiveState = failed \n");

        Assert.NotNull(state);
        Assert.Equal("failed", state!.Value.ActiveState);
    }

    [Theory]
    [InlineData("active", "Active (running)")]
    [InlineData("reloading", "Active (reloading)")]
    [InlineData("activating", "Activating")]
    [InlineData("deactivating", "Deactivating")]
    [InlineData("failed", "Failed")]
    [InlineData("inactive", "Inactive")]
    [InlineData("", "Inactive")]
    [InlineData("desconocido", "desconocido")]
    public void Describe_MapeaElEstadoActivo(string activeState, string expected)
    {
        var state = new ServiceInspector.UnitState("loaded", activeState, "enabled");

        Assert.Equal(expected, ServiceInspector.Describe(state));
    }
}
