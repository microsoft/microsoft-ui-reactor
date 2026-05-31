using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Pure-value tests over the public API surface in
/// <c>Microsoft.UI.Reactor.Docking</c>. These don't need a UI thread or
/// the reconciler — they exercise the record types, defaults, equality,
/// and immutability invariants of the spec 045 §4.3 committed surface.
/// </summary>
public class DockApiShapeTests
{
    // ─── Record defaults ────────────────────────────────────────────────

    [Fact]
    public void DockManager_DefaultConstruction_HasExpectedDefaults()
    {
        var dm = new DockManager();
        Assert.Null(dm.Layout);
        Assert.Null(dm.LeftSide);
        Assert.Null(dm.TopSide);
        Assert.Null(dm.RightSide);
        Assert.Null(dm.BottomSide);
        Assert.Null(dm.ActiveDocument);
        Assert.Null(dm.Adapter);
#pragma warning disable CS0618 // intentional coverage of obsolete Behavior surface
        Assert.Null(dm.Behavior);
#pragma warning restore CS0618
        Assert.Null(dm.PersistenceId);
        Assert.Equal(1, dm.LayoutSchemaVersion);
    }

    [Fact]
    public void DockableContent_DefaultPermissions_AreFalse()
    {
        var pane = new DockableContent("My Pane");
        Assert.Equal("My Pane", pane.Title);
        Assert.Null(pane.Content);
        Assert.Null(pane.Key);
        Assert.False(pane.CanClose);
        Assert.False(pane.CanPin);
        Assert.Null(pane.Width);
        Assert.Null(pane.Height);
        Assert.Null(pane.PersistenceState);
    }

    [Fact]
    public void DockTabGroup_DefaultTabPosition_IsTop()
    {
        var grp = new DockTabGroup(
            Documents: new[] { new DockableContent("A"), new DockableContent("B") });
        Assert.Equal(TabPosition.Top, grp.TabPosition);
        Assert.False(grp.CompactTabs);
        Assert.False(grp.ShowWhenEmpty);
        Assert.Equal(-1, grp.SelectedIndex);
    }

    // ─── Record equality / "with" mutation ──────────────────────────────

    [Fact]
    public void DockableContent_With_ChangesProducesEquatableRecord()
    {
        var a = new DockableContent("Pane", Key: "k1", CanClose: true);
        var b = a with { Title = "Pane" };
        var c = a with { Title = "Renamed" };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void DockSplit_StructuralEquality_DoesNotHoldAcrossDistinctArrayInstances()
    {
        var leaf1a = new DockableContent("L1", Key: "k1");
        var leaf1b = new DockableContent("L1", Key: "k1");

        var split1 = new DockSplit(Orientation.Horizontal, new DockNode[] { leaf1a });
        var split2 = new DockSplit(Orientation.Horizontal, new DockNode[] { leaf1b });

        // IReadOnlyList comparison on a record is reference-based by default
        // (record equality calls EqualityComparer<>.Default which uses the
        // collection's own Equals). Different array instances → not equal,
        // even when their element values match. This is the documented
        // Reactor convention; we verify it holds.
        Assert.NotEqual(split1, split2);
    }

    // ─── Algebra ────────────────────────────────────────────────────────

    [Fact]
    public void DockableContent_IsADockNode()
    {
        DockNode node = new DockableContent("X");
        Assert.IsType<DockableContent>(node);
    }

    [Fact]
    public void DockTabGroup_IsADockNode()
    {
        DockNode node = new DockTabGroup(new[] { new DockableContent("X") });
        Assert.IsType<DockTabGroup>(node);
    }

    [Fact]
    public void DockSplit_IsADockNode()
    {
        DockNode node = new DockSplit(Orientation.Vertical, new DockNode[]
        {
            new DockableContent("A"),
            new DockableContent("B"),
        });
        Assert.IsType<DockSplit>(node);
    }

    // ─── Enum values ────────────────────────────────────────────────────

    [Fact]
    public void DockTarget_AllNineValuesPresent()
    {
        var values = global::System.Enum.GetValues<DockTarget>();
        Assert.Equal(9, values.Length);
        Assert.Contains(DockTarget.Center,      values);
        Assert.Contains(DockTarget.SplitLeft,   values);
        Assert.Contains(DockTarget.SplitTop,    values);
        Assert.Contains(DockTarget.SplitRight,  values);
        Assert.Contains(DockTarget.SplitBottom, values);
        Assert.Contains(DockTarget.DockLeft,    values);
        Assert.Contains(DockTarget.DockTop,     values);
        Assert.Contains(DockTarget.DockRight,   values);
        Assert.Contains(DockTarget.DockBottom,  values);
    }

    [Fact]
    public void TabPosition_HasTopAndBottom()
    {
        var values = global::System.Enum.GetValues<TabPosition>();
        Assert.Equal(2, values.Length);
        Assert.Contains(TabPosition.Top,    values);
        Assert.Contains(TabPosition.Bottom, values);
    }

    // ─── Element inheritance ────────────────────────────────────────────

    [Fact]
    public void DockManager_InheritsElement()
    {
        Element element = new DockManager();
        Assert.NotNull(element);
        Assert.IsType<DockManager>(element);
    }

    // ─── Key requirement for reconciliation ─────────────────────────────

    [Fact]
    public void DockableContent_Key_IsObjectType_AllowingAnyHashable()
    {
        // Per spec 045 §4.3, Key is `object?`. Apps can supply any hashable
        // value: strings, GUIDs, enums, domain identifiers. We exercise the
        // common shapes to verify the type accepts each.
        var stringKey   = new DockableContent("a", Key: "string-key");
        var guidKey     = new DockableContent("b", Key: global::System.Guid.NewGuid());
        var intKey      = new DockableContent("c", Key: 42);
        var enumKey     = new DockableContent("d", Key: DockTarget.Center);

        Assert.NotNull(stringKey.Key);
        Assert.NotNull(guidKey.Key);
        Assert.NotNull(intKey.Key);
        Assert.NotNull(enumKey.Key);
    }

    // ─── DockManager 'with' carries Adapter/Behavior through ───────────

    [Fact]
    public void DockManager_With_PreservesAdapterAndBehavior()
    {
        var adapter = new TestAdapter();
#pragma warning disable CS0618 // intentional coverage of obsolete Behavior/IDockBehavior surface
        var behavior = new TestBehavior();
        var dm = new DockManager { Adapter = adapter, Behavior = behavior };

        var renamed = dm with { PersistenceId = "main" };

        Assert.Same(adapter, renamed.Adapter);
        Assert.Same(behavior, renamed.Behavior);
#pragma warning restore CS0618
        Assert.Equal("main", renamed.PersistenceId);
    }

    private sealed class TestAdapter : IDockAdapter
    {
        public Element? OnContentCreated(DockableContent content) => null;
        public void OnGroupCreated(DockTabGroupContext group) { }
        public Element? GetFloatingWindowTitleBar(DockableContent? draggedSource) => null;
    }

#pragma warning disable CS0618 // intentional implementation of obsolete IDockBehavior for coverage
    private sealed class TestBehavior : IDockBehavior
    {
        public void OnDocked(DockableContent src, DockTarget target) { }
        public void OnFloating(DockableContent content) { }
    }
#pragma warning restore CS0618
}
