using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the bucketed <see cref="LayoutModifiers"/> sub-record introduced
/// by spec 034 §A. Mirrors <see cref="VisualModifiersTests"/>.
/// </summary>
public class LayoutModifiersTests
{
    [Fact]
    public void Merge_With_Empty_Other_Returns_This_Equivalent()
    {
        var a = new LayoutModifiers { Padding = new Thickness(2) };
        var merged = a.Merge(new LayoutModifiers());
        Assert.Equal(a, merged);
    }

    [Fact]
    public void Merge_Partial_Other_Takes_Other_Where_Set()
    {
        var a = new LayoutModifiers { Padding = new Thickness(2), Width = 10 };
        var b = new LayoutModifiers { Padding = new Thickness(8) };
        var merged = a.Merge(b);
        Assert.Equal(new Thickness(8), merged.Padding);
        Assert.Equal(10, merged.Width);
    }

    [Fact]
    public void Merge_Full_Other_Returns_Other_Equivalent()
    {
        var a = new LayoutModifiers { Padding = new Thickness(2) };
        var b = new LayoutModifiers
        {
            Margin = new Thickness(1),
            Padding = new Thickness(3),
            Width = 5,
            Height = 6,
            MinWidth = 1,
            MinHeight = 2,
            MaxWidth = 100,
            MaxHeight = 200,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = true,
            MarginInlineStart = 1,
            MarginInlineEnd = 2,
            PaddingInlineStart = 3,
            PaddingInlineEnd = 4,
            BorderInlineStart = new Thickness(0.5),
            RequestedTheme = ElementTheme.Dark,
        };
        var merged = a.Merge(b);
        Assert.Equal(b, merged);
    }

    [Fact]
    public void Equal_Records_Have_Equal_HashCode()
    {
        var a = new LayoutModifiers { Padding = new Thickness(2), Width = 10 };
        var b = new LayoutModifiers { Padding = new Thickness(2), Width = 10 };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Different_Records_Are_Not_Equal()
    {
        var a = new LayoutModifiers { Padding = new Thickness(2) };
        var b = new LayoutModifiers { Padding = new Thickness(3) };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Merge_With_Both_Empty_Is_Empty()
    {
        var merged = new LayoutModifiers().Merge(new LayoutModifiers());
        Assert.Equal(new LayoutModifiers(), merged);
    }
}
