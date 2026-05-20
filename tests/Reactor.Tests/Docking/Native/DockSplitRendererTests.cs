using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Structural tests over the FlexElement tree the §2.1 split renderer
/// produces. No UI thread — just inspect the returned Element shape.
/// </summary>
public class DockSplitRendererTests
{
    private static DockableContent Leaf(string title) => new(title, Key: title);

    private static Element RenderChild(DockNode node) =>
        node switch
        {
            DockableContent leaf => new TextBlockElement(leaf.Title),
            _ => new TextBlockElement("?"),
        };

    [Fact]
    public void Render_HorizontalSplitTwoChildren_ProducesFlexRowWithOneSplitter()
    {
        var split = new DockSplit(
            Orientation.Horizontal,
            new DockNode[] { Leaf("a"), Leaf("b") });

        var flex = (FlexElement)DockSplitRenderer.Render(
            split,
            new[] { 0.6, 0.4 },
            RenderChild,
            onSplitterDelta: (_, _, _, _) => { });

        Assert.Equal(FlexDirection.Row, flex.Direction);
        Assert.Equal(3, flex.Children.Length);
        Assert.IsType<TextBlockElement>(flex.Children[0]);
        Assert.IsType<DockSplitterElement>(flex.Children[1]);
        Assert.IsType<TextBlockElement>(flex.Children[2]);

        var splitter = (DockSplitterElement)flex.Children[1];
        Assert.Equal(DockSplitterDirection.Columns, splitter.Direction);
    }

    [Fact]
    public void Render_VerticalSplitThreeChildren_ProducesFlexColumnWithTwoSplitters()
    {
        var split = new DockSplit(
            Orientation.Vertical,
            new DockNode[] { Leaf("a"), Leaf("b"), Leaf("c") });

        var flex = (FlexElement)DockSplitRenderer.Render(
            split,
            new[] { 0.5, 0.25, 0.25 },
            RenderChild,
            onSplitterDelta: (_, _, _, _) => { });

        Assert.Equal(FlexDirection.Column, flex.Direction);
        Assert.Equal(5, flex.Children.Length);
        Assert.IsType<TextBlockElement>(flex.Children[0]);
        Assert.IsType<DockSplitterElement>(flex.Children[1]);
        Assert.IsType<TextBlockElement>(flex.Children[2]);
        Assert.IsType<DockSplitterElement>(flex.Children[3]);
        Assert.IsType<TextBlockElement>(flex.Children[4]);

        Assert.Equal(DockSplitterDirection.Rows, ((DockSplitterElement)flex.Children[1]).Direction);
        Assert.Equal(DockSplitterDirection.Rows, ((DockSplitterElement)flex.Children[3]).Direction);
    }

    [Fact]
    public void Render_RatiosAppliedAsFlexGrow_OnChildElements()
    {
        var split = new DockSplit(
            Orientation.Horizontal,
            new DockNode[] { Leaf("a"), Leaf("b") });

        var flex = (FlexElement)DockSplitRenderer.Render(
            split,
            new[] { 0.7, 0.3 },
            RenderChild,
            onSplitterDelta: (_, _, _, _) => { });

        var firstAttached = flex.Children[0].GetAttached<FlexAttached>();
        var lastAttached = flex.Children[2].GetAttached<FlexAttached>();
        Assert.NotNull(firstAttached);
        Assert.NotNull(lastAttached);
        Assert.Equal(0.7, firstAttached!.Grow, 6);
        Assert.Equal(0.3, lastAttached!.Grow, 6);
    }

    [Fact]
    public void Render_SplitterDelta_InvokesCallbackWithIndexAndDelta()
    {
        int seenIndex = -1;
        double seenDelta = 0;
        double seenHostExtent = -1;
        bool seenFinal = false;

        var split = new DockSplit(
            Orientation.Horizontal,
            new DockNode[] { Leaf("a"), Leaf("b"), Leaf("c") });

        var flex = (FlexElement)DockSplitRenderer.Render(
            split,
            new[] { 0.33, 0.34, 0.33 },
            RenderChild,
            onSplitterDelta: (idx, delta, hostExtent, final) =>
            {
                seenIndex = idx;
                seenDelta = delta;
                seenHostExtent = hostExtent;
                seenFinal = final;
            });

        var secondSplitter = (DockSplitterElement)flex.Children[3];
        secondSplitter.OnDelta(42, 1024, true);
        Assert.Equal(1, seenIndex);
        Assert.Equal(42, seenDelta);
        Assert.Equal(1024, seenHostExtent);
        Assert.True(seenFinal);
    }

    [Fact]
    public void Render_RatioMismatch_Throws()
    {
        var split = new DockSplit(
            Orientation.Horizontal,
            new DockNode[] { Leaf("a"), Leaf("b") });

        Assert.Throws<ArgumentException>(
            () => DockSplitRenderer.Render(
                split,
                new[] { 0.5 },           // only one ratio
                RenderChild,
                onSplitterDelta: (_, _, _, _) => { }));
    }

    [Fact]
    public void Render_EmptySplit_ProducesEmptyFlexElement()
    {
        var split = new DockSplit(Orientation.Horizontal, Array.Empty<DockNode>());
        var flex = (FlexElement)DockSplitRenderer.Render(
            split,
            Array.Empty<double>(),
            RenderChild,
            onSplitterDelta: (_, _, _, _) => { });
        Assert.Empty(flex.Children);
    }
}

