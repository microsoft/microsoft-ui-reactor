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
}
