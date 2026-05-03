using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Cross-cutting tests for the bucketed <see cref="ElementModifiers"/>
/// (spec 034 §A): shim properties round-trip through <see cref="LayoutModifiers"/>
/// and <see cref="VisualModifiers"/>; equality and merge respect bucket
/// identity. Brush-typed fields require a XAML Application context and are
/// covered via ElementExtensions tests instead.
/// </summary>
public class ElementModifiersBucketTests
{
    [Fact]
    public void Shim_Init_And_Direct_Bucket_Init_Compare_Equal()
    {
        var viaShim = new ElementModifiers
        {
            Padding = new Thickness(2),
            Width = 10,
        };
        var viaBucket = new ElementModifiers
        {
            Layout = new LayoutModifiers { Padding = new Thickness(2), Width = 10 },
        };
        Assert.Equal(viaShim, viaBucket);
        Assert.Equal(viaShim.GetHashCode(), viaBucket.GetHashCode());
    }

    [Fact]
    public void Shim_Read_Reflects_Direct_Bucket_Write()
    {
        var em = new ElementModifiers
        {
            Layout = new LayoutModifiers { Padding = new Thickness(4), Width = 20 },
            Visual = new VisualModifiers { Opacity = 0.5 },
        };
        Assert.Equal(new Thickness(4), em.Padding);
        Assert.Equal(20, em.Width);
        Assert.Equal(0.5, em.Opacity);
    }

    [Fact]
    public void Shim_Write_Updates_Underlying_Bucket()
    {
        var em = new ElementModifiers { Padding = new Thickness(8), Margin = new Thickness(1) };
        Assert.NotNull(em.Layout);
        Assert.Equal(new Thickness(8), em.Layout!.Padding);
        Assert.Equal(new Thickness(1), em.Layout!.Margin);
    }

    [Fact]
    public void Merge_Visual_Only_Other_Preserves_Layout_Reference()
    {
        var em = new ElementModifiers { Padding = new Thickness(8) };
        var layoutBefore = em.Layout;

        var other = new ElementModifiers { Opacity = 0.9 };
        var merged = em.Merge(other);

        Assert.Same(layoutBefore, merged.Layout);
        Assert.NotNull(merged.Visual);
        Assert.Equal(new Thickness(8), merged.Padding);
        Assert.Equal(0.9, merged.Opacity);
    }

    [Fact]
    public void Merge_Layout_Only_Other_Preserves_Visual_Reference()
    {
        var em = new ElementModifiers { Opacity = 0.5 };
        var visualBefore = em.Visual;

        var other = new ElementModifiers { Width = 100 };
        var merged = em.Merge(other);

        Assert.Same(visualBefore, merged.Visual);
        Assert.NotNull(merged.Layout);
        Assert.Equal(100, merged.Width);
    }

    [Fact]
    public void Merge_Layout_Buckets_Combines_Distinct_Fields()
    {
        var a = new ElementModifiers { Padding = new Thickness(2) };
        var b = new ElementModifiers { Margin = new Thickness(1) };
        var merged = a.Merge(b);

        Assert.Equal(new Thickness(2), merged.Padding);
        Assert.Equal(new Thickness(1), merged.Margin);
    }

    [Fact]
    public void Merge_Visual_Buckets_Combines_Distinct_Fields()
    {
        var a = new ElementModifiers { Opacity = 0.5 };
        var b = new ElementModifiers { Rotation = 45f };
        var merged = a.Merge(b);

        Assert.Equal(0.5, merged.Opacity);
        Assert.Equal(45f, merged.Rotation);
    }

    [Fact]
    public void Empty_Modifiers_Has_Null_Buckets()
    {
        var em = new ElementModifiers();
        Assert.Null(em.Layout);
        Assert.Null(em.Visual);
        Assert.Null(em.Padding);
        Assert.Null(em.Opacity);
    }

    [Fact]
    public void Roundtrip_Shim_To_Bucket_Padding()
    {
        var em = new ElementModifiers { Padding = new Thickness(7) };
        Assert.Equal(new Thickness(7), em.Layout?.Padding);
        Assert.Equal(new Thickness(7), em.Padding);
    }

    [Fact]
    public void Multiple_Shim_Init_Builds_Bucket_With_All_Fields()
    {
        var em = new ElementModifiers
        {
            Padding = new Thickness(2),
            Margin = new Thickness(3),
            Width = 100,
            Height = 50,
            IsVisible = true,
        };
        Assert.NotNull(em.Layout);
        Assert.Equal(new Thickness(2), em.Layout!.Padding);
        Assert.Equal(new Thickness(3), em.Layout!.Margin);
        Assert.Equal(100, em.Layout!.Width);
        Assert.Equal(50, em.Layout!.Height);
        Assert.True(em.Layout!.IsVisible);
    }
}
