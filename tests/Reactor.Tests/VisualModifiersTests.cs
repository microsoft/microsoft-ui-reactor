using System.Numerics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the bucketed <see cref="VisualModifiers"/> sub-record introduced
/// by spec 034 §A. Brush-typed fields can't be exercised in unit tests
/// (SolidColorBrush ctor requires a XAML Application context), so coverage
/// here uses Opacity / transforms / CornerRadius / BorderThickness — Brush
/// merge behavior is exercised end-to-end via ElementExtensions tests.
/// </summary>
public class VisualModifiersTests
{
    [Fact]
    public void Merge_With_Empty_Other_Returns_This_Equivalent()
    {
        var a = new VisualModifiers { Opacity = 0.5 };
        var merged = a.Merge(new VisualModifiers());
        Assert.Equal(a, merged);
    }

    [Fact]
    public void Merge_Partial_Other_Takes_Other_Where_Set()
    {
        var a = new VisualModifiers { Opacity = 1.0, Rotation = 30f };
        var b = new VisualModifiers { Opacity = 0.25 };
        var merged = a.Merge(b);
        Assert.Equal(0.25, merged.Opacity);
        Assert.Equal(30f, merged.Rotation);
    }

    [Fact]
    public void Merge_Full_Other_Returns_Other_Equivalent()
    {
        var a = new VisualModifiers { Opacity = 0.1 };
        var b = new VisualModifiers
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Opacity = 0.5,
            Scale = new Vector3(1, 1, 1),
            Rotation = 90f,
            Translation = new Vector3(0, 0, 0),
            CenterPoint = new Vector3(0.5f, 0.5f, 0),
        };
        var merged = a.Merge(b);
        Assert.Equal(b, merged);
    }

    [Fact]
    public void Equal_Records_Have_Equal_HashCode()
    {
        var a = new VisualModifiers { Opacity = 0.5, Rotation = 45f };
        var b = new VisualModifiers { Opacity = 0.5, Rotation = 45f };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Different_Records_Are_Not_Equal()
    {
        var a = new VisualModifiers { Opacity = 0.5 };
        var b = new VisualModifiers { Opacity = 0.6 };
        Assert.NotEqual(a, b);
    }
}
