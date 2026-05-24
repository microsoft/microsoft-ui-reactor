using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 045 §2.10 — pure helpers in <see cref="DockHostKeyboard"/>.
/// </summary>
public sealed class DockHostKeyboardTests
{
    private static Document Doc(string key) => new() { Title = $"T-{key}", Key = key };

    [Fact]
    public void FindGroupContainingKey_NullInputs_ReturnEmpty()
    {
        var (g, p, i) = DockHostKeyboard.FindGroupContainingKey(null, "k");
        Assert.Null(g); Assert.Null(p); Assert.Equal(-1, i);

        (g, p, i) = DockHostKeyboard.FindGroupContainingKey(Doc("a"), null);
        Assert.Null(g); Assert.Null(p); Assert.Equal(-1, i);
    }

    [Fact]
    public void FindGroupContainingKey_DirectGroup_ReturnsRootPath()
    {
        var grp = new DockTabGroup(new DockableContent[] { Doc("a"), Doc("b") });
        var (g, p, i) = DockHostKeyboard.FindGroupContainingKey(grp, "b");
        Assert.Same(grp, g);
        Assert.Equal("0", p);
        Assert.Equal(1, i);
    }

    [Fact]
    public void FindGroupContainingKey_NestedSplit_BuildsPath()
    {
        var inner = new DockTabGroup(new DockableContent[] { Doc("x") });
        var outer = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new DockableContent[] { Doc("a") }),
            new DockSplit(Orientation.Vertical, new DockNode[] { inner }),
        });
        var (g, p, i) = DockHostKeyboard.FindGroupContainingKey(outer, "x");
        Assert.Same(inner, g);
        Assert.Equal("0/1/0", p);
        Assert.Equal(0, i);
    }

    [Fact]
    public void FindGroupContainingKey_NotInTree_ReturnsEmpty()
    {
        var grp = new DockTabGroup(new DockableContent[] { Doc("a") });
        var (g, p, i) = DockHostKeyboard.FindGroupContainingKey(grp, "missing");
        Assert.Null(g);
        Assert.Null(p);
        Assert.Equal(-1, i);
    }

    [Fact]
    public void FindGroupContainingKey_BareLeafRoot_ReturnsEmpty()
    {
        // A bare DockableContent root isn't a group, so the helper returns
        // empty even when the key matches (caller would handle the bare-leaf
        // path separately).
        var (g, p, i) = DockHostKeyboard.FindGroupContainingKey(Doc("a"), "a");
        Assert.Null(g);
        Assert.Equal(-1, i);
        _ = p;
    }

    [Fact]
    public void FindFirstGroup_NullRoot_ReturnsEmpty()
    {
        var (g, p) = DockHostKeyboard.FindFirstGroup(null);
        Assert.Null(g);
        Assert.Null(p);
    }

    [Fact]
    public void FindFirstGroup_GroupRoot_ReturnsItAtPathZero()
    {
        var grp = new DockTabGroup(new DockableContent[] { Doc("a") });
        var (g, p) = DockHostKeyboard.FindFirstGroup(grp);
        Assert.Same(grp, g);
        Assert.Equal("0", p);
    }

    [Fact]
    public void FindFirstGroup_NestedFromSplit_TakesLeftmost()
    {
        var leftGroup = new DockTabGroup(new DockableContent[] { Doc("L") });
        var rightGroup = new DockTabGroup(new DockableContent[] { Doc("R") });
        var split = new DockSplit(Orientation.Horizontal,
            new DockNode[] { leftGroup, rightGroup });
        var (g, p) = DockHostKeyboard.FindFirstGroup(split);
        Assert.Same(leftGroup, g);
        Assert.Equal("0/0", p);
    }

    [Fact]
    public void FindFirstGroup_BareLeafRoot_ReturnsEmpty()
    {
        var (g, p) = DockHostKeyboard.FindFirstGroup(Doc("a"));
        Assert.Null(g);
        Assert.Null(p);
    }

    [Fact]
    public void EnumerateLeaves_NullRoot_ReturnsEmpty()
    {
        Assert.Empty(DockHostKeyboard.EnumerateLeaves(null));
    }

    [Fact]
    public void EnumerateLeaves_NestedTree_FlattensDepthFirst()
    {
        var a = Doc("a");
        var b = Doc("b");
        var c = Doc("c");
        var d = Doc("d");
        var tree = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            a,
            new DockTabGroup(new DockableContent[] { b, c }),
            new DockSplit(Orientation.Vertical, new DockNode[] { d }),
        });
        var leaves = DockHostKeyboard.EnumerateLeaves(tree);
        Assert.Equal(new[] { a, b, c, d }, leaves);
    }

    [Fact]
    public void IndexOfKey_Found_ReturnsIndex()
    {
        var a = Doc("a");
        var b = Doc("b");
        var leaves = new DockableContent[] { a, b };
        Assert.Equal(1, DockHostKeyboard.IndexOfKey(leaves, "b"));
    }

    [Fact]
    public void IndexOfKey_NotFound_ReturnsMinusOne()
    {
        var leaves = new DockableContent[] { Doc("a") };
        Assert.Equal(-1, DockHostKeyboard.IndexOfKey(leaves, "missing"));
    }

    [Fact]
    public void IndexOfKey_NullKey_ReturnsMinusOne()
    {
        Assert.Equal(-1, DockHostKeyboard.IndexOfKey(new DockableContent[] { Doc("a") }, null));
    }

    [Theory]
    [InlineData(0, 1, 3, 1)]
    [InlineData(2, 1, 3, 0)] // wrap forward
    [InlineData(0, -1, 3, 2)] // wrap backward
    [InlineData(1, -1, 3, 0)]
    [InlineData(5, 0, 0, 0)] // zero count clamps to zero
    [InlineData(3, 2, 5, 0)] // exact wrap
    public void CycleIndex_WrapsCorrectly(int current, int delta, int count, int expected)
    {
        Assert.Equal(expected, DockHostKeyboard.CycleIndex(current, delta, count));
    }

    [Fact]
    public void BuildChords_FiveCommands_WithExpectedAccelerators()
    {
        int n = 0, p = 0, c = 0, k = 0;
        var cmds = DockHostKeyboard.BuildChords(
            invokeNextTab: () => n++,
            invokePrevTab: () => p++,
            invokeCloseActive: () => c++,
            invokeKeyboardDropMode: () => k++);
        Assert.Equal(5, cmds.Length);
        // Execute each one; the close-active appears twice (F4 + W) so
        // its counter increments by 2.
        foreach (var cmd in cmds) cmd.Execute?.Invoke();
        Assert.Equal(1, n);
        Assert.Equal(1, p);
        Assert.Equal(2, c);
        Assert.Equal(1, k);
    }

    // ── DockHostNativeComponent.AutomationIdForPane ──────────────────────

    [Fact]
    public void AutomationIdForPane_NullKey_ReturnsNull()
    {
        var leaf = new DockableContent("no-key");
        Assert.Null(DockHostNativeComponent.AutomationIdForPane(leaf));
    }

    [Fact]
    public void AutomationIdForPane_EmptyStringKey_ReturnsNull()
    {
        var leaf = new DockableContent("e", Key: string.Empty);
        Assert.Null(DockHostNativeComponent.AutomationIdForPane(leaf));
    }

    [Fact]
    public void AutomationIdForPane_StringKey_PrefixesPane()
    {
        var leaf = new DockableContent("t", Key: "main");
        Assert.Equal("pane:main", DockHostNativeComponent.AutomationIdForPane(leaf));
    }

    [Fact]
    public void AutomationIdForPane_IntKey_StringifiesViaToString()
    {
        var leaf = new DockableContent("t", Key: 42);
        Assert.Equal("pane:42", DockHostNativeComponent.AutomationIdForPane(leaf));
    }
}
