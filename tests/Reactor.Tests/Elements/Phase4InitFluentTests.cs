using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Spec 039 Phase 4 contract tests: each frequently-set init→fluent must
/// produce a record whose init property equals the passed value, and
/// chained fluents must preserve previously-set fluent values (the
/// <c>with</c>-expression semantics guarantee this — these tests pin it).
/// One fact per fluent.
/// </summary>
public class Phase4InitFluentTests
{
    // ── 4.1 Slider ────────────────────────────────────────────────────

    [Fact]
    public void Slider_Orientation_Sets()
    {
        var el = Slider(0).Orientation(Microsoft.UI.Xaml.Controls.Orientation.Vertical);
        Assert.Equal(Microsoft.UI.Xaml.Controls.Orientation.Vertical, el.Orientation);
    }

    [Fact]
    public void Slider_TickFrequency_Sets()
    {
        var el = Slider(0).TickFrequency(5.0);
        Assert.Equal(5.0, el.TickFrequency);
    }

    [Fact]
    public void Slider_TickPlacement_Sets()
    {
        var el = Slider(0).TickPlacement(TickPlacement.Outside);
        Assert.Equal(TickPlacement.Outside, el.TickPlacement);
    }

    [Fact]
    public void Slider_SnapsTo_Sets()
    {
        var el = Slider(0).SnapsTo(SliderSnapsTo.Ticks);
        Assert.Equal(SliderSnapsTo.Ticks, el.SnapsTo);
    }

    [Fact]
    public void Slider_ThumbToolTip_Sets()
    {
        var el = Slider(0).ThumbToolTip(false);
        Assert.False(el.IsThumbToolTipEnabled);
        Assert.True(Slider(0).ThumbToolTip().IsThumbToolTipEnabled); // default true
    }

    [Fact]
    public void Slider_Chaining_Preserves_Prior_Settings()
    {
        var el = Slider(0)
            .Orientation(Microsoft.UI.Xaml.Controls.Orientation.Vertical)
            .TickFrequency(2.0)
            .TickPlacement(TickPlacement.Outside)
            .SnapsTo(SliderSnapsTo.Ticks)
            .ThumbToolTip(false);
        Assert.Equal(Microsoft.UI.Xaml.Controls.Orientation.Vertical, el.Orientation);
        Assert.Equal(2.0, el.TickFrequency);
        Assert.Equal(TickPlacement.Outside, el.TickPlacement);
        Assert.Equal(SliderSnapsTo.Ticks, el.SnapsTo);
        Assert.False(el.IsThumbToolTipEnabled);
    }
}
