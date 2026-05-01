using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Markdown;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Pure-function tests for the small Yoga value-resolution helpers and
/// MarkdownAttribute factory used by the markdown layer.
/// </summary>
public class YogaValueExtensionsTests
{
    [Fact]
    public void ResolveValue_Point_Returns_Raw_Value()
    {
        var v = new YogaValue(50, YogaUnit.Point);
        Assert.Equal(50f, v.ResolveValue(referenceLength: 100));
    }

    [Fact]
    public void ResolveValue_Percent_Computes_Against_Reference()
    {
        var v = new YogaValue(25, YogaUnit.Percent);
        Assert.Equal(25f, v.ResolveValue(referenceLength: 100));
        Assert.Equal(50f, v.ResolveValue(referenceLength: 200));
    }

    [Fact]
    public void ResolveValue_Auto_Returns_NaN()
    {
        var v = new YogaValue(0, YogaUnit.Auto);
        Assert.True(float.IsNaN(v.ResolveValue(100)));
    }

    [Fact]
    public void ResolveValue_Undefined_Returns_NaN()
    {
        var v = new YogaValue(0, YogaUnit.Undefined);
        Assert.True(float.IsNaN(v.ResolveValue(100)));
    }

    [Fact]
    public void IsMaxContent_FitContent_Stretch_Predicates()
    {
        Assert.True(new YogaValue(0, YogaUnit.MaxContent).IsMaxContent());
        Assert.True(new YogaValue(0, YogaUnit.FitContent).IsFitContent());
        Assert.True(new YogaValue(0, YogaUnit.Stretch).IsStretch());
        Assert.False(new YogaValue(0, YogaUnit.Point).IsMaxContent());
        Assert.False(new YogaValue(0, YogaUnit.Point).IsFitContent());
        Assert.False(new YogaValue(0, YogaUnit.Point).IsStretch());
    }

    // ── MarkdownAttribute.Simple ─────────────────────────────────────────

    [Fact]
    public void MdAttribute_Simple_With_Text_Wraps_As_Normal()
    {
        var attr = MarkdownAttribute.Simple("hello");
        Assert.Equal("hello", attr.Text);
        Assert.Single(attr.SubstrTypes);
        Assert.Equal(MarkdownTextType.Normal, attr.SubstrTypes[0]);
    }

    [Fact]
    public void MdAttribute_Simple_With_Null_Returns_Default()
    {
        var attr = MarkdownAttribute.Simple(null);
        Assert.Null(attr.Text);
    }
}
