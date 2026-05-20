using Microsoft.UI.Reactor.Docking;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Tests for the Phase-2 cancellable lifecycle event-arg classes.
/// Spec 045 §5.3.5; tracking §2.12.
/// </summary>
public class LifecycleEventTests
{
    // ── Cancel propagation on every *ing event ──────────────────────────

    [Fact]
    public void DockLayoutChangingEventArgs_DefaultCancel_IsFalse()
    {
        var args = new DockLayoutChangingEventArgs();
        Assert.False(args.Cancel);
    }

    [Fact]
    public void DockLayoutChangingEventArgs_CancelCanBeFlipped()
    {
        var args = new DockLayoutChangingEventArgs();
        args.Cancel = true;
        Assert.True(args.Cancel);
    }

    [Fact]
    public void DockDocumentClosingEventArgs_CarriesDocument_AndCancel()
    {
        var doc = new Document { Title = "X", Key = "x" };
        var args = new DockDocumentClosingEventArgs { Document = doc };
        Assert.Same(doc, args.Document);
        Assert.False(args.Cancel);
    }

    [Fact]
    public void DockToolWindowHidingEventArgs_CarriesToolWindow()
    {
        var tw = new ToolWindow { Title = "Output", Key = "out" };
        var args = new DockToolWindowHidingEventArgs { ToolWindow = tw };
        Assert.Same(tw, args.ToolWindow);
    }

    [Fact]
    public void DockContentDockingEventArgs_CarriesContentAndTarget()
    {
        var pane = new DockableContent("X");
        var args = new DockContentDockingEventArgs { Content = pane, Target = DockTarget.SplitLeft };
        Assert.Same(pane, args.Content);
        Assert.Equal(DockTarget.SplitLeft, args.Target);
    }

    [Fact]
    public void DockActiveContentChangedEventArgs_AllowsNullPrevious()
    {
        var active = new Document { Title = "Z", Key = "z" };
        var args = new DockActiveContentChangedEventArgs { ActiveContent = active };
        Assert.Same(active, args.ActiveContent);
        Assert.Null(args.PreviousContent);
    }

    // ── Cancel only exists on *ing variants ─────────────────────────────

    [Fact]
    public void DockLayoutChangedEventArgs_HasNoCancel()
    {
        // Past-tense / observation-only event args do NOT inherit DockCancelEventArgs.
        Assert.False(typeof(DockLayoutChangedEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
        Assert.False(typeof(DockDocumentClosedEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
        Assert.False(typeof(DockToolWindowHiddenEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
        Assert.False(typeof(DockToolWindowClosedEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
        Assert.False(typeof(DockContentFloatedEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
        Assert.False(typeof(DockContentDockedEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
        Assert.False(typeof(DockActiveContentChangedEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
        Assert.False(typeof(DockFloatingWindowCreatedEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
        Assert.False(typeof(DockFloatingWindowClosedEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
    }

    [Fact]
    public void EveryIngEventArg_InheritsDockCancelEventArgs()
    {
        Assert.True(typeof(DockLayoutChangingEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
        Assert.True(typeof(DockDocumentClosingEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
        Assert.True(typeof(DockToolWindowHidingEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
        Assert.True(typeof(DockToolWindowClosingEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
        Assert.True(typeof(DockContentFloatingEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
        Assert.True(typeof(DockContentDockingEventArgs).IsAssignableTo(typeof(DockCancelEventArgs)));
    }

    // ── DockManager carries every On* prop with the matching arg ─────────

    [Fact]
    public void DockManager_CarriesAllPhase2EventProps()
    {
        // Validate that the DockManager record actually exposes the full set
        // of Phase-2 event hooks. Smoke check guards against accidental
        // removal during refactors.
        var props = typeof(DockManager).GetProperties()
            .Where(p => p.Name.StartsWith("On"))
            .Select(p => p.Name)
            .ToHashSet();

        var expected = new[]
        {
            nameof(DockManager.OnLayoutChanging),       nameof(DockManager.OnLayoutChanged),
            nameof(DockManager.OnDocumentClosing),      nameof(DockManager.OnDocumentClosed),
            nameof(DockManager.OnToolWindowHiding),     nameof(DockManager.OnToolWindowHidden),
            nameof(DockManager.OnToolWindowClosing),    nameof(DockManager.OnToolWindowClosed),
            nameof(DockManager.OnContentFloating),      nameof(DockManager.OnContentFloated),
            nameof(DockManager.OnContentDocking),       nameof(DockManager.OnContentDocked),
            nameof(DockManager.OnActiveContentChanged),
            nameof(DockManager.OnFloatingWindowCreated),nameof(DockManager.OnFloatingWindowClosed),
        };
        foreach (var name in expected)
            Assert.Contains(name, props);
    }

    [Fact]
    public void DockManager_EventProps_DefaultToNull()
    {
        var dm = new DockManager();
        Assert.Null(dm.OnLayoutChanging);
        Assert.Null(dm.OnLayoutChanged);
        Assert.Null(dm.OnDocumentClosing);
        Assert.Null(dm.OnDocumentClosed);
        Assert.Null(dm.OnToolWindowHiding);
        Assert.Null(dm.OnToolWindowHidden);
        Assert.Null(dm.OnToolWindowClosing);
        Assert.Null(dm.OnToolWindowClosed);
        Assert.Null(dm.OnContentFloating);
        Assert.Null(dm.OnContentFloated);
        Assert.Null(dm.OnContentDocking);
        Assert.Null(dm.OnContentDocked);
        Assert.Null(dm.OnActiveContentChanged);
        Assert.Null(dm.OnFloatingWindowCreated);
        Assert.Null(dm.OnFloatingWindowClosed);
    }
}
