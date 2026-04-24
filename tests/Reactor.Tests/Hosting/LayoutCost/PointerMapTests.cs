using Microsoft.UI.Reactor.Hosting.LayoutCost;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting.LayoutCost;

public class PointerMapTests
{
    [Fact]
    public void RegisterElementId_Then_TryGetComponent_ReturnsIt()
    {
        var pm = new PointerMap();
        var cid = new ComponentIdentity(77);
        pm.RegisterElementId(0xABCD, cid);

        Assert.True(pm.TryGetComponent(0xABCD, out var got));
        Assert.Equal(cid, got);
    }

    [Fact]
    public void TryGetComponent_UnknownId_ReturnsFalse()
    {
        var pm = new PointerMap();
        Assert.False(pm.TryGetComponent(0xDEADBEEF, out _));
    }

    [Fact]
    public void Clear_WipesAllBindings()
    {
        var pm = new PointerMap();
        pm.RegisterElementId(1, new ComponentIdentity(1));
        pm.RegisterElementId(2, new ComponentIdentity(2));
        pm.Clear();
        Assert.False(pm.TryGetComponent(1, out _));
        Assert.False(pm.TryGetComponent(2, out _));
    }

    [Fact]
    public void Register_SameId_Overwrites()
    {
        var pm = new PointerMap();
        pm.RegisterElementId(0x10, new ComponentIdentity(1));
        pm.RegisterElementId(0x10, new ComponentIdentity(2));
        pm.TryGetComponent(0x10, out var got);
        Assert.Equal(2, got.Value);
    }
}
