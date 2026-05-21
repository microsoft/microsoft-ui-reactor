using Microsoft.UI.Reactor.Docking;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Tests for <see cref="IDockLayoutStrategy"/> — insertion-policy hook for
/// programmatic adds. Spec 045 §5.3.6; tracking §2.13.
/// </summary>
public class LayoutStrategyTests
{
    [Fact]
    public void DefaultImplementation_BeforeInsertDocument_ReturnsFalse()
    {
        IDockLayoutStrategy s = new NoOpStrategy();
        var m = new DockHostModel();
        var doc = new Document { Title = "X", Key = "x" };

        Assert.False(s.BeforeInsertDocument(m, doc));
    }

    [Fact]
    public void DefaultImplementation_BeforeInsertToolWindow_ReturnsFalse()
    {
        IDockLayoutStrategy s = new NoOpStrategy();
        var m = new DockHostModel();
        var tw = new ToolWindow { Title = "Y", Key = "y" };

        Assert.False(s.BeforeInsertToolWindow(m, tw));
    }

    [Fact]
    public void DefaultImplementation_AfterInsert_IsNoOp()
    {
        IDockLayoutStrategy s = new NoOpStrategy();
        var m = new DockHostModel();
        s.AfterInsertDocument(m, new Document { Title = "X" });
        s.AfterInsertToolWindow(m, new ToolWindow { Title = "Y" });
        Assert.Empty(m.Pending);
    }

    [Fact]
    public void Strategy_CanShortCircuitInsertionByReturningTrue()
    {
        // Spec §5.3.6: "Strategies receive DockHostModel (mutable handle).
        // Example fixture: route any tool window with Title.StartsWith('Error')
        // to bottom side, height 180."
        IDockLayoutStrategy s = new ErrorPaneStrategy();
        var m = new DockHostModel();

        var errorPane = new ToolWindow { Title = "Error List", Key = "err" };
        var normalPane = new ToolWindow { Title = "Solution Explorer", Key = "se" };

        // Error-prefixed pane: strategy short-circuits + pins to bottom.
        Assert.True(s.BeforeInsertToolWindow(m, errorPane));
        var op = Assert.IsType<PendingMutation.PinToSideOp>(Assert.Single(m.Pending));
        Assert.Equal(DockSide.Bottom, op.Side);
        Assert.Same(errorPane, op.ToolWindow);

        // Non-error: strategy lets manager proceed.
        Assert.False(s.BeforeInsertToolWindow(m, normalPane));
    }

    [Fact]
    public void DockManager_AcceptsStrategyAssignment()
    {
        var s = new NoOpStrategy();
        var dm = new DockManager { LayoutStrategy = s };
        Assert.Same(s, dm.LayoutStrategy);
    }

    [Fact]
    public void DockManager_DefaultStrategy_IsNull()
    {
        var dm = new DockManager();
        Assert.Null(dm.LayoutStrategy);
    }

    // ── Spec §5.3.6 / §2.13: model-side dispatch ────────────────────────

    [Fact]
    public void ModelDock_NoStrategy_QueuesDefaultDockOp()
    {
        var m = new DockHostModel { LayoutStrategy = null };
        var doc = new Document { Title = "X", Key = "x" };

        m.Dock(doc, DockTarget.Center);

        var op = Assert.IsType<PendingMutation.DockOp>(Assert.Single(m.Pending));
        Assert.Same(doc, op.Content);
        Assert.Equal(DockTarget.Center, op.Target);
    }

    [Fact]
    public void ModelDock_StrategyShortCircuitsForDocument_SkipsDefaultDockOp()
    {
        var strategy = new ShortCircuitDocumentStrategy();
        var m = new DockHostModel { LayoutStrategy = strategy };
        var doc = new Document { Title = "X", Key = "x" };

        m.Dock(doc, DockTarget.Center);

        Assert.Equal(1, strategy.BeforeCount);
        Assert.Equal(0, strategy.AfterCount);
        // No DockOp queued — strategy claimed it.
        Assert.DoesNotContain(m.Pending, p => p is PendingMutation.DockOp);
    }

    [Fact]
    public void ModelDock_StrategyLetsThrough_QueuesDockOpThenFiresAfter()
    {
        var strategy = new PassThroughDocumentStrategy();
        var m = new DockHostModel { LayoutStrategy = strategy };
        var doc = new Document { Title = "X", Key = "x" };

        m.Dock(doc, DockTarget.SplitRight);

        Assert.Equal(1, strategy.BeforeCount);
        Assert.Equal(1, strategy.AfterCount);
        var dockOp = Assert.IsType<PendingMutation.DockOp>(
            Assert.Single(m.Pending, p => p is PendingMutation.DockOp));
        Assert.Same(doc, dockOp.Content);
        Assert.Equal(DockTarget.SplitRight, dockOp.Target);
    }

    [Fact]
    public void ModelDock_StrategyDispatchByContentType_RoutesToolWindowHook()
    {
        var strategy = new TrackingStrategy();
        var m = new DockHostModel { LayoutStrategy = strategy };
        var tw = new ToolWindow { Title = "Output", Key = "out" };

        m.Dock(tw, DockTarget.DockBottom);

        Assert.Equal(0, strategy.BeforeDocCount);
        Assert.Equal(1, strategy.BeforeToolCount);
        Assert.Equal(1, strategy.AfterToolCount);
    }

    [Fact]
    public void ModelDock_BarePaneType_BypassesStrategy()
    {
        // The typed §2.8 contract is Document / ToolWindow. The P1
        // source-compat bare DockableContent has no clear dispatch and
        // skips the strategy entirely — the default DockOp queues.
        var strategy = new TrackingStrategy();
        var m = new DockHostModel { LayoutStrategy = strategy };
        var pane = new DockableContent("Legacy", null, Key: "legacy");

        m.Dock(pane, DockTarget.Center);

        Assert.Equal(0, strategy.BeforeDocCount);
        Assert.Equal(0, strategy.BeforeToolCount);
        Assert.Equal(0, strategy.AfterDocCount);
        Assert.Equal(0, strategy.AfterToolCount);
        Assert.IsType<PendingMutation.DockOp>(Assert.Single(m.Pending));
    }

    [Fact]
    public void ModelDock_StrategyShortCircuitsForToolWindow_DoesNotFireAfter()
    {
        var strategy = new ShortCircuitToolWindowStrategy();
        var m = new DockHostModel { LayoutStrategy = strategy };
        var tw = new ToolWindow { Title = "Output", Key = "out" };

        m.Dock(tw, DockTarget.DockBottom);

        Assert.Equal(1, strategy.BeforeCount);
        Assert.Equal(0, strategy.AfterCount);
    }

    // ── Test doubles ────────────────────────────────────────────────────

    private sealed class NoOpStrategy : IDockLayoutStrategy { }

    private sealed class ErrorPaneStrategy : IDockLayoutStrategy
    {
        public bool BeforeInsertToolWindow(DockHostModel model, ToolWindow toolWindow)
        {
            if (toolWindow.Title.StartsWith("Error", global::System.StringComparison.Ordinal))
            {
                model.PinToSide(toolWindow, DockSide.Bottom);
                return true;
            }
            return false;
        }
    }

    private sealed class ShortCircuitDocumentStrategy : IDockLayoutStrategy
    {
        public int BeforeCount;
        public int AfterCount;
        public bool BeforeInsertDocument(DockHostModel model, Document document)
        {
            BeforeCount++;
            return true; // claim placement
        }
        public void AfterInsertDocument(DockHostModel model, Document document)
        {
            AfterCount++;
        }
    }

    private sealed class ShortCircuitToolWindowStrategy : IDockLayoutStrategy
    {
        public int BeforeCount;
        public int AfterCount;
        public bool BeforeInsertToolWindow(DockHostModel model, ToolWindow toolWindow)
        {
            BeforeCount++;
            return true;
        }
        public void AfterInsertToolWindow(DockHostModel model, ToolWindow toolWindow)
        {
            AfterCount++;
        }
    }

    private sealed class PassThroughDocumentStrategy : IDockLayoutStrategy
    {
        public int BeforeCount;
        public int AfterCount;
        public bool BeforeInsertDocument(DockHostModel model, Document document)
        {
            BeforeCount++;
            return false;
        }
        public void AfterInsertDocument(DockHostModel model, Document document)
        {
            AfterCount++;
        }
    }

    private sealed class TrackingStrategy : IDockLayoutStrategy
    {
        public int BeforeDocCount;
        public int AfterDocCount;
        public int BeforeToolCount;
        public int AfterToolCount;
        public bool BeforeInsertDocument(DockHostModel model, Document document)
        {
            BeforeDocCount++;
            return false;
        }
        public void AfterInsertDocument(DockHostModel model, Document document)
        {
            AfterDocCount++;
        }
        public bool BeforeInsertToolWindow(DockHostModel model, ToolWindow toolWindow)
        {
            BeforeToolCount++;
            return false;
        }
        public void AfterInsertToolWindow(DockHostModel model, ToolWindow toolWindow)
        {
            AfterToolCount++;
        }
    }
}
