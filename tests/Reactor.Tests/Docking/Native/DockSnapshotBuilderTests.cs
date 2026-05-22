using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Spec 045 §2.26 — snapshot builder pure-math + the host registry.
/// Surface used by the <c>docking.snapshot</c> MCP tool to surface a
/// live layout tree to headless clients.
/// </summary>
[Collection("DockingGlobals")]
public sealed class DockSnapshotBuilderTests : IDisposable
{
    public DockSnapshotBuilderTests()
    {
        DockHostRegistry.ResetForTest();
    }

    public void Dispose()
    {
        DockHostRegistry.ResetForTest();
    }

    // ── Snapshot shape from a DockManager ──────────────────────────────────

    [Fact]
    public void FromManager_EmptyLayout_ReturnsNullRoot()
    {
        var mgr = new DockManager { Layout = null };
        var snap = DockSnapshotBuilder.FromManager(mgr);
        Assert.Null(snap.Root);
        Assert.Empty(snap.LeftSide);
        Assert.Null(snap.ActiveKey);
    }

    [Fact]
    public void FromManager_BareLeaf_ReturnsLeafSnapshot()
    {
        var mgr = new DockManager
        {
            Layout = new DockableContent("Solo", null, Key: "solo"),
        };
        var snap = DockSnapshotBuilder.FromManager(mgr);
        var leaf = Assert.IsType<DockSnapshotLeaf>(snap.Root);
        Assert.Equal("solo", leaf.Pane.Key);
        Assert.Equal("Solo", leaf.Pane.Title);
        Assert.Equal("content", leaf.Pane.Role);
    }

    [Fact]
    public void FromManager_DocumentLeaf_CarriesDocumentRole()
    {
        var mgr = new DockManager
        {
            Layout = new DockTabGroup(new DockableContent[]
            {
                new Document { Title = "Editor", Key = "editor" },
            }),
        };
        var snap = DockSnapshotBuilder.FromManager(mgr);
        var group = Assert.IsType<DockSnapshotTabGroup>(snap.Root);
        Assert.Equal("document", group.Documents[0].Role);
        Assert.True(group.Documents[0].CanClose); // Document default
    }

    [Fact]
    public void FromManager_ToolWindowLeaf_CarriesToolWindowRole()
    {
        var mgr = new DockManager
        {
            Layout = new DockTabGroup(new DockableContent[]
            {
                new ToolWindow { Title = "Properties", Key = "props" },
            }),
        };
        var snap = DockSnapshotBuilder.FromManager(mgr);
        var group = Assert.IsType<DockSnapshotTabGroup>(snap.Root);
        Assert.Equal("toolwindow", group.Documents[0].Role);
        // ToolWindow default for CanClose is false.
        Assert.False(group.Documents[0].CanClose);
    }

    [Fact]
    public void FromManager_Split_SerializesOrientationAndChildren()
    {
        var mgr = new DockManager
        {
            Layout = new DockSplit(Orientation.Vertical, new DockNode[]
            {
                new DockTabGroup(new[] { new DockableContent("A", null, Key: "a") }),
                new DockTabGroup(new[] { new DockableContent("B", null, Key: "b") }),
            }),
        };
        var snap = DockSnapshotBuilder.FromManager(mgr);
        var split = Assert.IsType<DockSnapshotSplit>(snap.Root);
        Assert.Equal("Vertical", split.Orientation);
        Assert.Equal(2, split.Children.Count);
        Assert.IsType<DockSnapshotTabGroup>(split.Children[0]);
        Assert.IsType<DockSnapshotTabGroup>(split.Children[1]);
    }

    [Fact]
    public void FromManager_ActiveDocument_SurfacesStringifiedKey()
    {
        var doc = new Document { Title = "Editor", Key = "editor" };
        var mgr = new DockManager
        {
            Layout = new DockTabGroup(new DockableContent[] { doc }),
            ActiveDocument = doc,
        };
        var snap = DockSnapshotBuilder.FromManager(mgr);
        Assert.Equal("editor", snap.ActiveKey);
    }

    [Fact]
    public void FromManager_SideStrips_FlattenToSnapshotPanes()
    {
        var tw = new ToolWindow { Title = "Output", Key = "output" };
        var mgr = new DockManager
        {
            Layout = null,
            BottomSide = new ToolWindow[] { tw },
        };
        var snap = DockSnapshotBuilder.FromManager(mgr);
        Assert.Single(snap.BottomSide);
        Assert.Equal("output", snap.BottomSide[0].Key);
        Assert.Equal("toolwindow", snap.BottomSide[0].Role);
    }

    // ── Host registry semantics ────────────────────────────────────────────

    [Fact]
    public void Registry_Register_AssignsStableId()
    {
        var mgr = new DockManager();
        var rec = DockHostRegistry.Register(mgr);
        Assert.Equal("dh:1", rec.Id);
        Assert.Same(mgr, rec.Manager);
    }

    [Fact]
    public void Registry_Register_IsIdempotentForSameManager()
    {
        var mgr = new DockManager();
        var a = DockHostRegistry.Register(mgr);
        var b = DockHostRegistry.Register(mgr);
        Assert.Same(a, b);
    }

    [Fact]
    public void Registry_Unregister_RemovesRecord()
    {
        var mgr = new DockManager();
        DockHostRegistry.Register(mgr);
        DockHostRegistry.Unregister(mgr);
        Assert.Empty(DockHostRegistry.Snapshot());
    }

    [Fact]
    public void Registry_Snapshot_ReturnsLiveRecordsOnly()
    {
        var m1 = new DockManager();
        var m2 = new DockManager();
        DockHostRegistry.Register(m1);
        DockHostRegistry.Register(m2);
        var snap = DockHostRegistry.Snapshot();
        Assert.Equal(2, snap.Count);
        Assert.Contains(snap, r => ReferenceEquals(r.Manager, m1));
        Assert.Contains(snap, r => ReferenceEquals(r.Manager, m2));
    }

    [Fact]
    public void Registry_Get_ResolvesByIdOrReturnsNull()
    {
        var mgr = new DockManager();
        var rec = DockHostRegistry.Register(mgr);
        Assert.Same(rec, DockHostRegistry.Get(rec.Id));
        Assert.Null(DockHostRegistry.Get("dh:nope"));
        Assert.Null(DockHostRegistry.Get(""));
    }

    [Fact]
    public void FromRecord_LiveManager_PopulatesHostId()
    {
        var mgr = new DockManager
        {
            Layout = new DockableContent("Pane", null, Key: "p"),
        };
        var rec = DockHostRegistry.Register(mgr);
        var snap = DockSnapshotBuilder.FromRecord(rec);
        Assert.NotNull(snap);
        Assert.Equal(rec.Id, snap!.HostId);
        Assert.NotNull(snap.Root);
    }
}
