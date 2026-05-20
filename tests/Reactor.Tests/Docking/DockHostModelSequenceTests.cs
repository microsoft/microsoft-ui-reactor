using Microsoft.UI.Reactor.Docking;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Operation-sequence selftests for <see cref="DockHostModel"/> mutations
/// — the §2.27 fixture matrix (spec 045 §8.3 "Selftests under
/// tests/Reactor.AppTests.Host/SelfTest/Fixtures/Docking*.cs": layout-
/// model fixture — Dock/Float/Hide/Show/Close sequences; assert tree
/// + Descendants()").
/// </summary>
public class DockHostModelSequenceTests
{
    // ── Linear sequences preserve call order ────────────────────────────

    [Fact]
    public void DockFloatClose_OperationsRetainOrderInQueue()
    {
        var m = new DockHostModel();
        var doc = new Document { Title = "X", Key = "x" };
        var tw  = new ToolWindow { Title = "Y", Key = "y" };

        m.Dock(doc, DockTarget.Center);
        m.Float(tw);
        m.Close(doc);

        Assert.Equal(3, m.Pending.Count);
        var op0 = Assert.IsType<PendingMutation.DockOp>(m.Pending[0]);
        var op1 = Assert.IsType<PendingMutation.FloatOp>(m.Pending[1]);
        var op2 = Assert.IsType<PendingMutation.CloseOp>(m.Pending[2]);

        Assert.Same(doc, op0.Content);
        Assert.Equal(DockTarget.Center, op0.Target);
        Assert.Same(tw, op1.Content);
        Assert.Same(doc, op2.Content);
    }

    [Fact]
    public void HideShow_RecordTwoSeparateOps()
    {
        var m = new DockHostModel();
        var tw = new ToolWindow { Title = "Output", Key = "out" };

        m.Hide(tw);
        m.Show(tw);

        Assert.Equal(2, m.Pending.Count);
        Assert.IsType<PendingMutation.HideOp>(m.Pending[0]);
        Assert.IsType<PendingMutation.ShowOp>(m.Pending[1]);
    }

    [Fact]
    public void ActivateAfterDock_OrderingMatchesCallSite()
    {
        var m = new DockHostModel();
        var doc = new Document { Title = "X", Key = "x" };

        m.Dock(doc, DockTarget.DockRight);
        m.Activate(doc);

        var op0 = Assert.IsType<PendingMutation.DockOp>(m.Pending[0]);
        var op1 = Assert.IsType<PendingMutation.ActivateOp>(m.Pending[1]);
        Assert.Equal(DockTarget.DockRight, op0.Target);
        Assert.Same(doc, op1.Content);
    }

    // ── Mixed-source mutations all queue into a single ordered stream ───

    [Fact]
    public void MultiplePanes_CommingledOps_RetainOriginalOrder()
    {
        var m = new DockHostModel();
        var docA = new Document { Title = "A", Key = "a" };
        var docB = new Document { Title = "B", Key = "b" };
        var twC = new ToolWindow { Title = "C", Key = "c" };

        m.Dock(docA, DockTarget.SplitLeft);
        m.Dock(docB, DockTarget.SplitRight);
        m.PinToSide(twC, DockSide.Bottom);
        m.Activate(docA);
        m.Close(docB);

        Assert.Equal(5, m.Pending.Count);

        var ops = m.Pending.Select(p => p.GetType()).ToArray();
        Assert.Equal(typeof(PendingMutation.DockOp),       ops[0]);
        Assert.Equal(typeof(PendingMutation.DockOp),       ops[1]);
        Assert.Equal(typeof(PendingMutation.PinToSideOp),  ops[2]);
        Assert.Equal(typeof(PendingMutation.ActivateOp),   ops[3]);
        Assert.Equal(typeof(PendingMutation.CloseOp),      ops[4]);
    }

    // ── Read surface stays consistent across queued mutations ───────────

    [Fact]
    public void Pending_IsObservableSeparatelyFromReadSurface()
    {
        // The model's read surface (Root, sides, floating, ActiveContent)
        // reflects the *currently-rendered* state. Queued mutations only
        // affect future renders — they don't pre-mutate the read surface.
        var doc = new Document { Title = "X", Key = "x" };
        var m = new DockHostModel { Root = doc };

        m.Close(doc); // queue a close

        // Root still reads the same — only when the renderer drains the
        // queue and produces a new state does it appear.
        Assert.Same(doc, m.Root);
        Assert.Single(m.Pending);
    }

    // ── Strategy + model: spec §5.3.6 example end-to-end ────────────────

    [Fact]
    public void ErrorPaneStrategy_RoutesViaModel_QueuesPinToSide()
    {
        // The §5.3.6 example: a strategy short-circuits insertion to pin
        // any error-named tool window to the bottom side. We simulate the
        // manager's dispatch by directly calling the interface method.
        var strategy = new ErrorPanePinStrategy();
        var m = new DockHostModel();
        var errorPane = new ToolWindow { Title = "Error List", Key = "err" };

        var handled = ((IDockLayoutStrategy)strategy).BeforeInsertToolWindow(m, errorPane);

        Assert.True(handled);
        var op = Assert.IsType<PendingMutation.PinToSideOp>(Assert.Single(m.Pending));
        Assert.Equal(DockSide.Bottom, op.Side);
        Assert.Same(errorPane, op.ToolWindow);
    }

    private sealed class ErrorPanePinStrategy : IDockLayoutStrategy
    {
        public bool BeforeInsertToolWindow(DockHostModel model, ToolWindow tw)
        {
            if (tw.Title.StartsWith("Error", global::System.StringComparison.Ordinal))
            {
                model.PinToSide(tw, DockSide.Bottom);
                return true;
            }
            return false;
        }
    }

    // ── PreviousContainer integration: hide→show preserves identity ─────

    [Fact]
    public void HideShow_WithPreviousContainerTracker_RoundTripsContainerIdentity()
    {
        // Combines §2.15 + §2.16 — model records the hide; tracker remembers
        // the container; on show, the tracker yields the same container.
        var pane = new ToolWindow { Title = "Output", Key = "out" };
        var group = new DockTabGroup(new DockableContent[] { pane });
        var m = new DockHostModel { Root = group };

        // Host would call this just before Hide queues the mutation.
        PreviousContainerTracker.Set(pane, group);
        m.Hide(pane);

        // Later, on Show, the host queries the tracker for the destination.
        m.Show(pane);
        var remembered = PreviousContainerTracker.GetPrevious(pane);

        Assert.Same(group, remembered);
        Assert.Equal(2, m.Pending.Count);
    }
}
