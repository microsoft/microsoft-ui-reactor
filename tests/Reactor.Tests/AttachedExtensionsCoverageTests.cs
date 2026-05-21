using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Covers the small attached-property fluent helpers
/// (RelativePanelExtensions, FlexExtensions argument validation).
/// </summary>
public class AttachedExtensionsCoverageTests
{
    [Fact]
    public void RelativePanel_Sets_All_Attached_Fields()
    {
        var rect = Rectangle().RelativePanel(
            name: "R1",
            rightOf: "R2",
            below: "R3",
            leftOf: "R4",
            above: "R5",
            alignLeftWith: "R6",
            alignRightWith: "R7",
            alignTopWith: "R8",
            alignBottomWith: "R9",
            alignHorizontalCenterWith: "R10",
            alignVerticalCenterWith: "R11",
            alignLeftWithPanel: true,
            alignRightWithPanel: true,
            alignTopWithPanel: true,
            alignBottomWithPanel: true,
            alignHorizontalCenterWithPanel: true,
            alignVerticalCenterWithPanel: true);

        var attached = ((Element)rect).Attached;
        Assert.NotNull(attached);
        Assert.True(attached!.ContainsKey(typeof(RelativePanelAttached)));
        var rp = (RelativePanelAttached)attached[typeof(RelativePanelAttached)];
        Assert.Equal("R1", rp.Name);
        Assert.Equal("R2", rp.RightOf);
        Assert.Equal("R3", rp.Below);
        Assert.Equal("R4", rp.LeftOf);
        Assert.Equal("R5", rp.Above);
        Assert.True(rp.AlignVerticalCenterWithPanel);
    }

    // ── Flex<T>(...) input validation ─────────────────────────────────

    [Fact]
    public void Flex_Negative_Grow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextBlock("x").Flex(grow: -1));
    }

    [Fact]
    public void Flex_NaN_Grow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextBlock("x").Flex(grow: double.NaN));
    }

    [Fact]
    public void Flex_Infinite_Grow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextBlock("x").Flex(grow: double.PositiveInfinity));
    }

    [Fact]
    public void Flex_Negative_Shrink_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextBlock("x").Flex(shrink: -1));
    }

    [Fact]
    public void Flex_NaN_Shrink_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextBlock("x").Flex(shrink: double.NaN));
    }

    [Fact]
    public void Flex_Infinite_Shrink_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextBlock("x").Flex(shrink: double.PositiveInfinity));
    }

    [Fact]
    public void Flex_Valid_Args_Sets_Attached()
    {
        var el = TextBlock("x").Flex(
            grow: 1,
            shrink: 2,
            basis: 50,
            alignSelf: FlexAlign.Stretch,
            position: FlexPositionType.Absolute,
            left: 1, top: 2, right: 3, bottom: 4);
        var attached = ((Element)el).Attached;
        Assert.NotNull(attached);
        Assert.True(attached!.ContainsKey(typeof(FlexAttached)));
    }

    // ── min-width / min-height validation ─────────────────────────────

    [Fact]
    public void Flex_Negative_MinWidth_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextBlock("x").Flex(minWidth: -1));
    }

    [Fact]
    public void Flex_NaN_MinWidth_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextBlock("x").Flex(minWidth: double.NaN));
    }

    [Fact]
    public void Flex_Infinite_MinWidth_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextBlock("x").Flex(minWidth: double.PositiveInfinity));
    }

    [Fact]
    public void Flex_Negative_MinHeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextBlock("x").Flex(minHeight: -1));
    }

    [Fact]
    public void Flex_NaN_MinHeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextBlock("x").Flex(minHeight: double.NaN));
    }

    [Fact]
    public void Flex_Infinite_MinHeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextBlock("x").Flex(minHeight: double.PositiveInfinity));
    }

    [Fact]
    public void Flex_MinWidth_Zero_Is_Valid()
    {
        var el = TextBlock("x").Flex(minWidth: 0);
        var attached = ((Element)el).Attached;
        Assert.NotNull(attached);
        var fa = (FlexAttached)attached![typeof(FlexAttached)];
        Assert.Equal(0, fa.MinWidth);
    }

    [Fact]
    public void Flex_MinWidth_And_MinHeight_Round_Trip()
    {
        var el = TextBlock("x").Flex(minWidth: 50, minHeight: 25);
        var attached = ((Element)el).Attached;
        Assert.NotNull(attached);
        var fa = (FlexAttached)attached![typeof(FlexAttached)];
        Assert.Equal(50, fa.MinWidth);
        Assert.Equal(25, fa.MinHeight);
    }

    [Fact]
    public void Flex_Default_MinWidth_And_MinHeight_Are_Null()
    {
        var el = TextBlock("x").Flex(grow: 1);
        var attached = ((Element)el).Attached;
        Assert.NotNull(attached);
        var fa = (FlexAttached)attached![typeof(FlexAttached)];
        Assert.Null(fa.MinWidth);
        Assert.Null(fa.MinHeight);
    }
}
