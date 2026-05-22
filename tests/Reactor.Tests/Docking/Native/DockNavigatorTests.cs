using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Spec 045 §2.10 — pure-math coverage for the Ctrl+Tab pane navigator.
/// The popup primitive itself can't be instantiated without a XamlRoot,
/// so behavior depending on the live Popup is covered by the host-mounted
/// selftest fixture; this file exercises the helpers that drive the
/// navigator + the entry/index math.
/// </summary>
public class DockNavigatorTests
{
    [Fact]
    public void EnumerateLeaves_NullRoot_ReturnsEmpty()
    {
        var leaves = DockHostKeyboard.EnumerateLeaves(null);
        Assert.Empty(leaves);
    }

    [Fact]
    public void EnumerateLeaves_BareLeaf_ReturnsSelf()
    {
        var pane = new DockableContent("A", Key: "a");
        var leaves = DockHostKeyboard.EnumerateLeaves(pane);
        Assert.Single(leaves);
        Assert.Same(pane, leaves[0]);
    }

    [Fact]
    public void EnumerateLeaves_TabGroup_ReturnsAllDocuments()
    {
        var a = new DockableContent("A", Key: "a");
        var b = new DockableContent("B", Key: "b");
        var c = new DockableContent("C", Key: "c");
        var grp = new DockTabGroup(new DockableContent[] { a, b, c });
        var leaves = DockHostKeyboard.EnumerateLeaves(grp);
        Assert.Equal(3, leaves.Count);
        Assert.Same(a, leaves[0]);
        Assert.Same(b, leaves[1]);
        Assert.Same(c, leaves[2]);
    }

    [Fact]
    public void EnumerateLeaves_Split_DepthFirstLeftToRight()
    {
        var a = new DockableContent("A", Key: "a");
        var b = new DockableContent("B", Key: "b");
        var c = new DockableContent("C", Key: "c");
        var d = new DockableContent("D", Key: "d");
        var layout = new DockSplit(Orientation.Vertical, new DockNode[]
        {
            new DockSplit(Orientation.Horizontal, new DockNode[]
            {
                new DockTabGroup(new[] { a, b }),
                new DockTabGroup(new[] { c }),
            }),
            new DockTabGroup(new[] { d }),
        });
        var leaves = DockHostKeyboard.EnumerateLeaves(layout);
        Assert.Equal(new[] { "a", "b", "c", "d" }, leaves.Select(l => l.Key).ToArray());
    }

    [Fact]
    public void IndexOfKey_FoundKey_ReturnsIndex()
    {
        var a = new DockableContent("A", Key: "a");
        var b = new DockableContent("B", Key: "b");
        var leaves = new[] { a, b };
        Assert.Equal(1, DockHostKeyboard.IndexOfKey(leaves, "b"));
    }

    [Fact]
    public void IndexOfKey_NullKey_ReturnsMinusOne()
    {
        var a = new DockableContent("A", Key: "a");
        var leaves = new[] { a };
        Assert.Equal(-1, DockHostKeyboard.IndexOfKey(leaves, null));
    }

    [Fact]
    public void IndexOfKey_MissingKey_ReturnsMinusOne()
    {
        var a = new DockableContent("A", Key: "a");
        var leaves = new[] { a };
        Assert.Equal(-1, DockHostKeyboard.IndexOfKey(leaves, "missing"));
    }

    // ── DockChordBridge.Handlers shape ─────────────────────────────────────

    [Fact]
    public void Handlers_OpenNavigator_Optional_DefaultsToNull()
    {
        var h = new DockChordBridge.Handlers(
            NextTab: () => { },
            PrevTab: () => { },
            CloseActive: () => { },
            EnterDropMode: () => { });
        Assert.Null(h.OpenNavigator);
        Assert.Null(h.OpenHiddenPicker);
    }

    [Fact]
    public void Handlers_OpenNavigator_RoundTripsDelegate()
    {
        int captured = 0;
        var h = new DockChordBridge.Handlers(
            NextTab: () => { },
            PrevTab: () => { },
            CloseActive: () => { },
            EnterDropMode: () => { },
            OpenNavigator: delta => captured = delta);
        h.OpenNavigator?.Invoke(+1);
        Assert.Equal(+1, captured);
        h.OpenNavigator?.Invoke(-1);
        Assert.Equal(-1, captured);
    }
}
