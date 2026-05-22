using System.Threading;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Tests for <see cref="DockHostModel"/> — the internal source-of-truth
/// surface for Phase 2. Spec 045 §5.3.10; tracking §2.16.
/// </summary>
public class DockHostModelTests
{
    // ── Read surface ────────────────────────────────────────────────────

    [Fact]
    public void DefaultModel_HasEmptyDocumentEnumeration()
    {
        var m = new DockHostModel();
        Assert.Empty(m.Descendants());
        Assert.Empty(m.AllContent());
        Assert.Null(m.ActiveContent);
        Assert.Empty(m.LeftSide);
        Assert.Empty(m.TopSide);
        Assert.Empty(m.RightSide);
        Assert.Empty(m.BottomSide);
        Assert.Empty(m.Floating);
    }

    [Fact]
    public void Descendants_WalksDepthFirst()
    {
        var leaf1 = new DockableContent("A", Key: "a");
        var leaf2 = new DockableContent("B", Key: "b");
        var leaf3 = new DockableContent("C", Key: "c");
        var grp = new DockTabGroup(new[] { leaf2, leaf3 });
        var root = new DockSplit(Orientation.Horizontal, new DockNode[] { leaf1, grp });

        var m = new DockHostModel { Root = root };

        var seen = m.Descendants().ToList();
        Assert.Equal(3, seen.Count);
        Assert.Same(leaf1, seen[0]);
        Assert.Same(leaf2, seen[1]);
        Assert.Same(leaf3, seen[2]);
    }

    [Fact]
    public void AllContent_IncludesSideAndFloating()
    {
        var doc = new DockableContent("D", Key: "d");
        var side = new ToolWindow { Title = "Side", Key = "side" };
        var floating = new DockableContent("F", Key: "f");

        var m = new DockHostModel
        {
            Root = doc,
            RightSide = new[] { side },
            Floating = new[]
            {
                new FloatingDockWindow
                {
                    Id = "fw1",
                    Contents = new DockableContent[] { floating },
                },
            },
        };

        var all = m.AllContent().ToList();
        Assert.Contains(doc, all);
        Assert.Contains(side, all);
        Assert.Contains(floating, all);
    }

    // ── Mutation queue (UI-thread-affined) ─────────────────────────────

    [Fact]
    public void Dock_QueuesPendingMutation()
    {
        var m = new DockHostModel();
        var pane = new DockableContent("X", Key: "x");

        m.Dock(pane, DockTarget.SplitRight);

        Assert.Single(m.Pending);
        var op = Assert.IsType<PendingMutation.DockOp>(m.Pending[0]);
        Assert.Same(pane, op.Content);
        Assert.Equal(DockTarget.SplitRight, op.Target);
    }

    [Fact]
    public void Float_QueuesPendingMutation()
    {
        var m = new DockHostModel();
        var pane = new DockableContent("X", Key: "x");
        m.Float(pane);

        var op = Assert.IsType<PendingMutation.FloatOp>(Assert.Single(m.Pending));
        Assert.Same(pane, op.Content);
    }

    [Fact]
    public void Hide_QueuesPendingMutation()
    {
        var m = new DockHostModel();
        var tw = new ToolWindow { Title = "Output" };
        m.Hide(tw);

        var op = Assert.IsType<PendingMutation.HideOp>(Assert.Single(m.Pending));
        Assert.Same(tw, op.ToolWindow);
    }

    [Fact]
    public void Show_QueuesPendingMutation()
    {
        var m = new DockHostModel();
        var pane = new DockableContent("X", Key: "x");
        m.Show(pane);

        Assert.IsType<PendingMutation.ShowOp>(Assert.Single(m.Pending));
    }

    [Fact]
    public void Close_QueuesPendingMutation()
    {
        var m = new DockHostModel();
        var pane = new DockableContent("X", Key: "x");
        m.Close(pane);

        Assert.IsType<PendingMutation.CloseOp>(Assert.Single(m.Pending));
    }

    [Fact]
    public void Activate_QueuesPendingMutation()
    {
        var m = new DockHostModel();
        var pane = new DockableContent("X", Key: "x");
        m.Activate(pane);

        Assert.IsType<PendingMutation.ActivateOp>(Assert.Single(m.Pending));
    }

    [Fact]
    public void PinToSide_QueuesPendingMutation()
    {
        var m = new DockHostModel();
        var tw = new ToolWindow { Title = "Errors", Key = "err" };
        m.PinToSide(tw, DockSide.Bottom);

        var op = Assert.IsType<PendingMutation.PinToSideOp>(Assert.Single(m.Pending));
        Assert.Equal(DockSide.Bottom, op.Side);
        Assert.Same(tw, op.ToolWindow);
    }

    [Fact]
    public void Mutators_RejectNullContent()
    {
        var m = new DockHostModel();
        Assert.Throws<ArgumentNullException>(() => m.Dock(null!, DockTarget.Center));
        Assert.Throws<ArgumentNullException>(() => m.Float(null!));
        Assert.Throws<ArgumentNullException>(() => m.Hide(null!));
        Assert.Throws<ArgumentNullException>(() => m.Show(null!));
        Assert.Throws<ArgumentNullException>(() => m.Close(null!));
        Assert.Throws<ArgumentNullException>(() => m.Activate(null!));
        Assert.Throws<ArgumentNullException>(() => m.PinToSide(null!, DockSide.Left));
    }

    // ── Off-thread contract enforcement ────────────────────────────────

    [Fact]
    public void Mutations_OffOwnerThread_Throw()
    {
        var m = new DockHostModel(); // captures current thread id
        var pane = new DockableContent("X", Key: "x");

        // Force the mutation onto a guaranteed-different thread. Using
        // `Task.Run` here previously hit the test's thread-id check by
        // chance when xUnit reused a worker as the test thread; an
        // explicit `new Thread()` cannot share ManagedThreadId with the
        // constructing thread.
        Exception? caught = null;
        var worker = new Thread(() =>
        {
            try
            {
                Assert.Throws<InvalidOperationException>(() => m.Dock(pane, DockTarget.Center));
                Assert.Throws<InvalidOperationException>(() => m.Float(pane));
                Assert.Throws<InvalidOperationException>(() => m.Close(pane));
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        })
        { IsBackground = true };
        worker.Start();
        worker.Join();
        if (caught is not null) throw caught;
    }
}
