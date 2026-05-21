using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Unit coverage for the §2.4 drag session state machine. The session is
/// the object-ref payload that replaces upstream WinUI.Dock's static
/// GUID→object table.
/// </summary>
public class DockDragSessionTests
{
    private static (DockableContent pane, DockManager mgr) MakePair(string title = "p")
    {
        var pane = new DockableContent(title, Key: title);
        var mgr = new DockManager { Layout = pane };
        return (pane, mgr);
    }

    [Fact]
    public void Begin_FirstSession_SetsCurrent()
    {
        DockDragSession.ResetForTest();
        var (pane, mgr) = MakePair();

        var session = DockDragSession.Begin(pane, mgr, sourceTabIndex: 0);

        Assert.NotNull(session);
        Assert.True(session!.IsActive);
        Assert.Same(pane, session.Source);
        Assert.Same(mgr, session.SourceManager);
        Assert.Equal(0, session.SourceTabIndex);
        Assert.Same(session, DockDragSession.Current);

        DockDragSession.ResetForTest();
    }

    [Fact]
    public void Begin_WhileActive_ReturnsNull()
    {
        DockDragSession.ResetForTest();
        var (p1, m1) = MakePair("p1");
        var (p2, m2) = MakePair("p2");

        var first = DockDragSession.Begin(p1, m1, 0);
        Assert.NotNull(first);

        var second = DockDragSession.Begin(p2, m2, 0);
        Assert.Null(second);
        Assert.Same(first, DockDragSession.Current);

        DockDragSession.ResetForTest();
    }

    [Fact]
    public void End_DeactivatesAndClearsCurrent()
    {
        DockDragSession.ResetForTest();
        var (pane, mgr) = MakePair();
        var session = DockDragSession.Begin(pane, mgr, 0)!;

        session.End();

        Assert.False(session.IsActive);
        Assert.Null(DockDragSession.Current);
    }

    [Fact]
    public void Cancel_IsIdempotent_AndAliasOfEnd()
    {
        DockDragSession.ResetForTest();
        var (pane, mgr) = MakePair();
        var session = DockDragSession.Begin(pane, mgr, 0)!;

        session.Cancel();
        session.Cancel(); // second call should no-op

        Assert.False(session.IsActive);
        Assert.Null(DockDragSession.Current);
    }

    [Fact]
    public void Begin_AfterPriorEnd_CreatesNewSession()
    {
        DockDragSession.ResetForTest();
        var (p1, m1) = MakePair("p1");
        var first = DockDragSession.Begin(p1, m1, 0)!;
        first.End();

        var (p2, m2) = MakePair("p2");
        var second = DockDragSession.Begin(p2, m2, 1);

        Assert.NotNull(second);
        Assert.Same(p2, second!.Source);
        Assert.Equal(1, second.SourceTabIndex);
        Assert.Same(second, DockDragSession.Current);

        DockDragSession.ResetForTest();
    }

    [Fact]
    public void Begin_NullArgs_Throws()
    {
        DockDragSession.ResetForTest();
        var (pane, mgr) = MakePair();
        Assert.Throws<ArgumentNullException>(() => DockDragSession.Begin(null!, mgr, 0));
        Assert.Throws<ArgumentNullException>(() => DockDragSession.Begin(pane, null!, 0));
    }
}
