using Microsoft.UI.Reactor.Layout;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Pure-data tests for the internal LayoutResults / CachedMeasurement structs.
/// These types live behind the Yoga algorithm and are otherwise only exercised
/// by the layout fixtures, which never compare two LayoutResults instances or
/// hit the Reset/EqualTo paths directly.
/// </summary>
public class YogaLayoutResultsTests
{
    // ── CachedMeasurement.Equals ─────────────────────────────────────

    [Fact]
    public void CachedMeasurement_Default_Equals_Default()
    {
        var a = new CachedMeasurement();
        var b = new CachedMeasurement();
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void CachedMeasurement_Different_SizingMode_NotEqual()
    {
        var a = new CachedMeasurement { WidthSizingMode = SizingMode.StretchFit };
        var b = new CachedMeasurement { WidthSizingMode = SizingMode.MaxContent };
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void CachedMeasurement_DifferentAvailableSize_NotEqual()
    {
        var a = new CachedMeasurement { AvailableWidth = 100, AvailableHeight = 50 };
        var b = new CachedMeasurement { AvailableWidth = 200, AvailableHeight = 50 };
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void CachedMeasurement_DifferentComputedSize_NotEqual()
    {
        var a = new CachedMeasurement { ComputedWidth = 10, ComputedHeight = 20 };
        var b = new CachedMeasurement { ComputedWidth = 30, ComputedHeight = 20 };
        Assert.False(a.Equals(b));

        var c = new CachedMeasurement { ComputedHeight = 99 };
        Assert.False(a.Equals(c));
    }

    [Fact]
    public void CachedMeasurement_AllFieldsMatching_AreEqual()
    {
        var a = new CachedMeasurement
        {
            AvailableWidth = 100,
            AvailableHeight = 50,
            WidthSizingMode = SizingMode.StretchFit,
            HeightSizingMode = SizingMode.FitContent,
            ComputedWidth = 80,
            ComputedHeight = 40,
        };
        var b = new CachedMeasurement
        {
            AvailableWidth = 100,
            AvailableHeight = 50,
            WidthSizingMode = SizingMode.StretchFit,
            HeightSizingMode = SizingMode.FitContent,
            ComputedWidth = 80,
            ComputedHeight = 40,
        };
        Assert.True(a.Equals(b));
    }

    // ── LayoutResults dimension/edge accessors ───────────────────────

    [Fact]
    public void Get_And_Set_All_Dimension_Accessors()
    {
        var r = new LayoutResults();

        r.SetDimension(YogaDimension.Width, 100f);
        r.SetDimension(YogaDimension.Height, 50f);
        Assert.Equal(100f, r.GetDimension(YogaDimension.Width));
        Assert.Equal(50f, r.GetDimension(YogaDimension.Height));

        r.SetMeasuredDimension(YogaDimension.Width, 90f);
        r.SetMeasuredDimension(YogaDimension.Height, 45f);
        Assert.Equal(90f, r.GetMeasuredDimension(YogaDimension.Width));
        Assert.Equal(45f, r.GetMeasuredDimension(YogaDimension.Height));

        r.SetRawDimension(YogaDimension.Width, 200f);
        r.SetRawDimension(YogaDimension.Height, 100f);
        Assert.Equal(200f, r.GetRawDimension(YogaDimension.Width));
        Assert.Equal(100f, r.GetRawDimension(YogaDimension.Height));
    }

    [Fact]
    public void Get_And_Set_All_Edge_Accessors()
    {
        var r = new LayoutResults();

        foreach (var edge in new[] { YogaPhysicalEdge.Left, YogaPhysicalEdge.Top, YogaPhysicalEdge.Right, YogaPhysicalEdge.Bottom })
        {
            r.SetPosition(edge, 1f);
            r.SetMargin(edge, 2f);
            r.SetBorder(edge, 3f);
            r.SetPadding(edge, 4f);
        }

        foreach (var edge in new[] { YogaPhysicalEdge.Left, YogaPhysicalEdge.Top, YogaPhysicalEdge.Right, YogaPhysicalEdge.Bottom })
        {
            Assert.Equal(1f, r.GetPosition(edge));
            Assert.Equal(2f, r.GetMargin(edge));
            Assert.Equal(3f, r.GetBorder(edge));
            Assert.Equal(4f, r.GetPadding(edge));
        }
    }

    [Fact]
    public void Direction_And_HadOverflow_Setters()
    {
        var r = new LayoutResults();
        r.Direction = FlexLayoutDirection.RTL;
        r.HadOverflow = true;
        Assert.Equal(FlexLayoutDirection.RTL, r.Direction);
        Assert.True(r.HadOverflow);
    }

    // ── EqualTo() ───────────────────────────────────────────────────

    [Fact]
    public void EqualTo_Identical_Returns_True()
    {
        var a = new LayoutResults();
        var b = new LayoutResults();
        Assert.True(a.EqualTo(b));
    }

    [Fact]
    public void EqualTo_DifferentDirection_Returns_False()
    {
        var a = new LayoutResults { Direction = FlexLayoutDirection.LTR };
        var b = new LayoutResults { Direction = FlexLayoutDirection.RTL };
        Assert.False(a.EqualTo(b));
    }

    [Fact]
    public void EqualTo_DifferentHadOverflow_Returns_False()
    {
        var a = new LayoutResults { HadOverflow = true };
        var b = new LayoutResults { HadOverflow = false };
        Assert.False(a.EqualTo(b));
    }

    [Fact]
    public void EqualTo_DifferentDimensions_Returns_False()
    {
        var a = new LayoutResults();
        a.SetDimension(YogaDimension.Width, 100f);
        var b = new LayoutResults();
        b.SetDimension(YogaDimension.Width, 200f);
        Assert.False(a.EqualTo(b));
    }

    [Fact]
    public void EqualTo_DifferentMeasuredDimensions_Returns_False()
    {
        var a = new LayoutResults();
        a.SetMeasuredDimension(YogaDimension.Height, 50f);
        var b = new LayoutResults();
        b.SetMeasuredDimension(YogaDimension.Height, 75f);
        Assert.False(a.EqualTo(b));
    }

    [Fact]
    public void EqualTo_DifferentEdges_Each_Field_Returns_False()
    {
        // Position
        var a = new LayoutResults();
        a.SetPosition(YogaPhysicalEdge.Left, 10f);
        var b = new LayoutResults();
        Assert.False(a.EqualTo(b));

        // Margin
        a = new LayoutResults();
        a.SetMargin(YogaPhysicalEdge.Top, 10f);
        b = new LayoutResults();
        Assert.False(a.EqualTo(b));

        // Border
        a = new LayoutResults();
        a.SetBorder(YogaPhysicalEdge.Right, 10f);
        b = new LayoutResults();
        Assert.False(a.EqualTo(b));

        // Padding
        a = new LayoutResults();
        a.SetPadding(YogaPhysicalEdge.Bottom, 10f);
        b = new LayoutResults();
        Assert.False(a.EqualTo(b));
    }

    [Fact]
    public void EqualTo_All_Fields_Matching_Returns_True()
    {
        var a = new LayoutResults { Direction = FlexLayoutDirection.LTR, HadOverflow = true };
        a.SetDimension(YogaDimension.Width, 50f);
        a.SetDimension(YogaDimension.Height, 25f);
        a.SetMeasuredDimension(YogaDimension.Width, 50f);
        a.SetMeasuredDimension(YogaDimension.Height, 25f);
        a.SetPosition(YogaPhysicalEdge.Left, 1f);
        a.SetMargin(YogaPhysicalEdge.Top, 2f);
        a.SetBorder(YogaPhysicalEdge.Right, 3f);
        a.SetPadding(YogaPhysicalEdge.Bottom, 4f);

        var b = new LayoutResults { Direction = FlexLayoutDirection.LTR, HadOverflow = true };
        b.SetDimension(YogaDimension.Width, 50f);
        b.SetDimension(YogaDimension.Height, 25f);
        b.SetMeasuredDimension(YogaDimension.Width, 50f);
        b.SetMeasuredDimension(YogaDimension.Height, 25f);
        b.SetPosition(YogaPhysicalEdge.Left, 1f);
        b.SetMargin(YogaPhysicalEdge.Top, 2f);
        b.SetBorder(YogaPhysicalEdge.Right, 3f);
        b.SetPadding(YogaPhysicalEdge.Bottom, 4f);

        Assert.True(a.EqualTo(b));
    }
}
