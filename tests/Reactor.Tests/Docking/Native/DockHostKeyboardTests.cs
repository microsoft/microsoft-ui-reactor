using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Unit coverage for the pure helpers behind spec 045 §2.10 keyboard
/// chords. The live chord wiring (KeyboardAccelerators attached to the
/// dock host Border) is exercised by selftest fixtures under the host
/// harness; here we lock down the search + cycle math so chord logic
/// has stable building blocks.
/// </summary>
public class DockHostKeyboardTests
{
    private static DockableContent Pane(string key, string title = "T") =>
        new(title, Content: null, Key: key);

    [Fact]
    public void FindGroupContainingKey_FlatTabGroup_FindsPane()
    {
        var a = Pane("a");
        var b = Pane("b");
        var grp = new DockTabGroup(new[] { a, b }, SelectedIndex: 0);

        var (group, path, idx) = DockHostKeyboard.FindGroupContainingKey(grp, "b");

        Assert.Same(grp, group);
        Assert.Equal("0", path);
        Assert.Equal(1, idx);
    }

    [Fact]
    public void FindGroupContainingKey_NestedSplit_FindsPaneAndPath()
    {
        var a = Pane("a");
        var b = Pane("b");
        var c = Pane("c");
        var d = Pane("d");
        var top = new DockTabGroup(new[] { a, b }, SelectedIndex: 0);
        var bot = new DockTabGroup(new[] { c, d }, SelectedIndex: 1);
        var split = new DockSplit(Orientation.Vertical, new DockNode[] { top, bot });

        var (group, path, idx) = DockHostKeyboard.FindGroupContainingKey(split, "d");

        Assert.Same(bot, group);
        Assert.Equal("0/1", path);
        Assert.Equal(1, idx);
    }

    [Fact]
    public void FindGroupContainingKey_NullKey_ReturnsNull()
    {
        var grp = new DockTabGroup(new[] { Pane("a") });
        var (group, path, idx) = DockHostKeyboard.FindGroupContainingKey(grp, key: null);
        Assert.Null(group);
        Assert.Null(path);
        Assert.Equal(-1, idx);
    }

    [Fact]
    public void FindGroupContainingKey_UnknownKey_ReturnsNull()
    {
        var grp = new DockTabGroup(new[] { Pane("a") });
        var (group, _, idx) = DockHostKeyboard.FindGroupContainingKey(grp, "missing");
        Assert.Null(group);
        Assert.Equal(-1, idx);
    }

    [Fact]
    public void FindFirstGroup_NestedSplit_ReturnsLeftmostLeafGroup()
    {
        var left = new DockTabGroup(new[] { Pane("l") });
        var right = new DockTabGroup(new[] { Pane("r") });
        var split = new DockSplit(Orientation.Horizontal, new DockNode[] { left, right });

        var (group, path) = DockHostKeyboard.FindFirstGroup(split);

        Assert.Same(left, group);
        Assert.Equal("0/0", path);
    }

    [Fact]
    public void FindFirstGroup_NullRoot_ReturnsNull()
    {
        var (group, path) = DockHostKeyboard.FindFirstGroup(root: null);
        Assert.Null(group);
        Assert.Null(path);
    }

    [Theory]
    [InlineData(0, +1, 3, 1)]
    [InlineData(1, +1, 3, 2)]
    [InlineData(2, +1, 3, 0)]  // wraps past last
    [InlineData(0, -1, 3, 2)]  // wraps past first
    [InlineData(2, -1, 3, 1)]
    [InlineData(0, +1, 1, 0)]  // single tab — no-op
    [InlineData(0, -1, 1, 0)]
    public void CycleIndex_WrapsAndStaysInRange(int current, int delta, int count, int expected)
    {
        Assert.Equal(expected, DockHostKeyboard.CycleIndex(current, delta, count));
    }

    [Fact]
    public void CycleIndex_ZeroCount_ReturnsZero()
    {
        Assert.Equal(0, DockHostKeyboard.CycleIndex(0, +1, 0));
    }

    [Fact]
    public void BuildChords_ReturnsExpectedChordKit()
    {
        // The bridge is wired by the host component; here we just lock
        // down the BuildChords helper shape so future refactors don't
        // accidentally drop a chord. 5 chords expected: PageDown, PageUp,
        // F4, W, M.
        var chords = DockHostKeyboard.BuildChords(
            invokeNextTab: () => { },
            invokePrevTab: () => { },
            invokeCloseActive: () => { },
            invokeKeyboardDropMode: () => { });

        Assert.Equal(5, chords.Length);
        Assert.Contains(chords, c => c.Accelerator?.Key == global::Windows.System.VirtualKey.PageDown);
        Assert.Contains(chords, c => c.Accelerator?.Key == global::Windows.System.VirtualKey.PageUp);
        Assert.Contains(chords, c => c.Accelerator?.Key == global::Windows.System.VirtualKey.F4);
        Assert.Contains(chords, c => c.Accelerator?.Key == global::Windows.System.VirtualKey.W);
        Assert.Contains(chords, c => c.Accelerator?.Key == global::Windows.System.VirtualKey.M
            && c.Accelerator?.Modifiers == (global::Windows.System.VirtualKeyModifiers.Control | global::Windows.System.VirtualKeyModifiers.Shift));
    }

    [Fact]
    public void DockChordBridge_RoundTripsHandlers()
    {
        var dm = new DockManager();
        int n = 0, p = 0, c = 0, k = 0;
        var handlers = new DockChordBridge.Handlers(
            NextTab: () => n++,
            PrevTab: () => p++,
            CloseActive: () => c++,
            EnterDropMode: () => k++);

        DockChordBridge.Set(dm, handlers);
        var got = DockChordBridge.Get(dm);
        Assert.NotNull(got);
        got!.NextTab(); got.PrevTab(); got.CloseActive(); got.EnterDropMode();
        Assert.Equal((1, 1, 1, 1), (n, p, c, k));

        DockChordBridge.Clear(dm);
        Assert.Null(DockChordBridge.Get(dm));
    }
}
