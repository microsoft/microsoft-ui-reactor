using Microsoft.UI.Reactor.Docking;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 046 §6.2 — <see cref="DockSides"/> flag enum, the
/// <see cref="ToolWindow.AllowedSides"/> default, and
/// <see cref="DockSideExtensions.ToFlag(DockSide)"/> mapping.
/// </summary>
public class DockSidesTests
{
    [Fact]
    public void Default_AllowedSides_IsAll()
    {
        var tw = new ToolWindow { Title = "Errors", Key = "errors" };
        Assert.Equal(DockSides.All, tw.AllowedSides);
    }

    [Fact]
    public void All_Is_BitwiseOrOfFourSides()
    {
        Assert.Equal(
            DockSides.Left | DockSides.Top | DockSides.Right | DockSides.Bottom,
            DockSides.All);
    }

    [Fact]
    public void None_HasNoFlags()
    {
        Assert.Equal(0, (int)DockSides.None);
        Assert.False(DockSides.None.HasFlag(DockSides.Left));
        Assert.False(DockSides.None.HasFlag(DockSides.Top));
        Assert.False(DockSides.None.HasFlag(DockSides.Right));
        Assert.False(DockSides.None.HasFlag(DockSides.Bottom));
    }

    [Fact]
    public void Combined_LeftOrRight_HasFlagSemantics()
    {
        var mask = DockSides.Left | DockSides.Right;
        Assert.True(mask.HasFlag(DockSides.Left));
        Assert.True(mask.HasFlag(DockSides.Right));
        Assert.False(mask.HasFlag(DockSides.Top));
        Assert.False(mask.HasFlag(DockSides.Bottom));
    }

    [Theory]
    [InlineData(DockSide.Left, DockSides.Left)]
    [InlineData(DockSide.Top, DockSides.Top)]
    [InlineData(DockSide.Right, DockSides.Right)]
    [InlineData(DockSide.Bottom, DockSides.Bottom)]
    public void ToFlag_RoundTrips(DockSide side, DockSides expected)
    {
        Assert.Equal(expected, side.ToFlag());
    }

    [Fact]
    public void AllowedSides_Bottom_RoundTripsThroughWith()
    {
        var tw = new ToolWindow { Title = "Errors", Key = "errors", AllowedSides = DockSides.Bottom };
        Assert.Equal(DockSides.Bottom, tw.AllowedSides);

        var mutated = tw with { AllowedSides = DockSides.Bottom | DockSides.Right };
        Assert.True(mutated.AllowedSides.HasFlag(DockSides.Bottom));
        Assert.True(mutated.AllowedSides.HasFlag(DockSides.Right));
        Assert.False(mutated.AllowedSides.HasFlag(DockSides.Left));
    }

    [Fact]
    public void AllowedSides_None_IsAllowed()
    {
        // §8.10 / §9 Q4: AllowedSides = None is valid — means float-only.
        var tw = new ToolWindow { Title = "Floater", Key = "f", AllowedSides = DockSides.None };
        Assert.Equal(DockSides.None, tw.AllowedSides);
    }
}
