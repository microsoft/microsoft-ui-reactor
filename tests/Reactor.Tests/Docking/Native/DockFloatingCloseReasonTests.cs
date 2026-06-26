using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Issue #417 — a floating window's <c>Window.Closed</c> fires for BOTH a
/// genuine user close and the synthetic close that follows a cross-window
/// dock-back. These tests lock the reason-threading seam:
///   • <see cref="DockDragSession"/> records WHICH pane was consumed and
///     whether it migrated to a host or another float.
///   • <see cref="DockFloatingWindow.MigratedReasonFor"/> maps that state to
///     a <see cref="DockFloatingCloseReason"/>, scoped to the specific pane
///     so a multi-pane float that lost an earlier tab does NOT report its
///     own later user close as a migration.
///   • <see cref="DockFloatingTracker"/> stashes the reason for the
///     <c>Closed</c> handler to read once.
/// </summary>
[Xunit.Collection("DockingGlobals")]
public class DockFloatingCloseReasonTests
{
    private static DockableContent Pane(string key) => new(key, Key: key);

    private static DockManager Mgr(DockableContent pane) => new() { Layout = pane };

    // ── DockDragSession consumed-pane tracking ─────────────────────────

    [Fact]
    public void MarkConsumed_NoArg_CapturesCurrentSourceAsHostMigration()
    {
        DockDragSession.ResetForTest();
        var pane = Pane("doc:a");
        DockDragSession.Begin(pane, Mgr(pane), 0);

        DockDragSession.MarkConsumed();

        Assert.True(DockDragSession.Consumed);
        Assert.Same(pane, DockDragSession.LastConsumedPane);
        Assert.False(DockDragSession.LastConsumedToFloat);

        DockDragSession.ResetForTest();
    }

    [Fact]
    public void MarkConsumed_PaneScoped_RecordsPaneAndFloatFlag()
    {
        DockDragSession.ResetForTest();
        var pane = Pane("doc:a");
        DockDragSession.Begin(pane, Mgr(pane), 0);

        DockDragSession.MarkConsumed(pane, toFloat: true);

        Assert.True(DockDragSession.Consumed);
        Assert.Same(pane, DockDragSession.LastConsumedPane);
        Assert.True(DockDragSession.LastConsumedToFloat);

        DockDragSession.ResetForTest();
    }

    [Fact]
    public void Begin_ResetsConsumedState()
    {
        DockDragSession.ResetForTest();
        var a = Pane("doc:a");
        DockDragSession.Begin(a, Mgr(a), 0);
        DockDragSession.MarkConsumed(a, toFloat: true);
        DockDragSession.Current!.End();

        var b = Pane("doc:b");
        DockDragSession.Begin(b, Mgr(b), 0);

        Assert.False(DockDragSession.Consumed);
        Assert.Null(DockDragSession.LastConsumedPane);
        Assert.False(DockDragSession.LastConsumedToFloat);

        DockDragSession.ResetForTest();
    }

    [Fact]
    public void End_PreservesConsumedState()
    {
        // The floating window's TabDragCompleted handler observes Consumed
        // AFTER the host's OnConfirm called End() — so End must not clear it.
        DockDragSession.ResetForTest();
        var pane = Pane("doc:a");
        DockDragSession.Begin(pane, Mgr(pane), 0);
        DockDragSession.MarkConsumed();
        DockDragSession.Current!.End();

        Assert.True(DockDragSession.Consumed);
        Assert.Same(pane, DockDragSession.LastConsumedPane);

        DockDragSession.ResetForTest();
    }

    // ── MigratedReasonFor: the reason-decision seam ────────────────────

    [Fact]
    public void MigratedReasonFor_NotConsumed_IsContentClosed()
    {
        DockDragSession.ResetForTest();
        var pane = Pane("doc:a");

        Assert.Equal(DockFloatingCloseReason.ContentClosed, DockFloatingWindow.MigratedReasonFor(pane));

        DockDragSession.ResetForTest();
    }

    [Fact]
    public void MigratedReasonFor_ConsumedToHost_IsMigratedToHost()
    {
        DockDragSession.ResetForTest();
        var pane = Pane("doc:a");
        DockDragSession.Begin(pane, Mgr(pane), 0);
        DockDragSession.MarkConsumed(); // host dock-back

        Assert.Equal(DockFloatingCloseReason.MigratedToHost, DockFloatingWindow.MigratedReasonFor(pane));

        DockDragSession.ResetForTest();
    }

    [Fact]
    public void MigratedReasonFor_ConsumedToFloat_IsMigratedToFloat()
    {
        DockDragSession.ResetForTest();
        var pane = Pane("doc:a");
        DockDragSession.Begin(pane, Mgr(pane), 0);
        DockDragSession.MarkConsumed(pane, toFloat: true);

        Assert.Equal(DockFloatingCloseReason.MigratedToFloat, DockFloatingWindow.MigratedReasonFor(pane));

        DockDragSession.ResetForTest();
    }

    [Fact]
    public void MigratedReasonFor_DifferentPaneConsumed_IsContentClosed()
    {
        // The multi-pane subtlety (issue #417): a float lost tab A to a
        // dock-back (Consumed stays true), then the user genuinely closes
        // the surviving tab B. B's close must NOT inherit A's migration.
        DockDragSession.ResetForTest();
        var a = Pane("doc:a");
        var b = Pane("doc:b");
        DockDragSession.Begin(a, Mgr(a), 0);
        DockDragSession.MarkConsumed(); // A migrated; Consumed == true

        Assert.Equal(DockFloatingCloseReason.ContentClosed, DockFloatingWindow.MigratedReasonFor(b));
        // ...but A itself is still correctly reported as migrated.
        Assert.Equal(DockFloatingCloseReason.MigratedToHost, DockFloatingWindow.MigratedReasonFor(a));

        DockDragSession.ResetForTest();
    }

    [Fact]
    public void MarkConsumed_NoActiveSession_DoesNotPersistConsumedState()
    {
        // Review hardening (issue #417): a host drop-confirm path can invoke
        // the no-arg MarkConsumed() after the drag session was already
        // ended/cancelled (the call sites guard session?.End() as nullable).
        // With no active session there is no concrete pane, so we must NOT
        // persist Consumed=true with a null LastConsumedPane — otherwise the
        // next unrelated floating-window close would misreport as a migration
        // until the following Begin.
        DockDragSession.ResetForTest(); // Current == null, no session
        var pane = Pane("doc:a");

        DockDragSession.MarkConsumed(); // no session, no explicit pane

        Assert.False(DockDragSession.Consumed);
        Assert.Null(DockDragSession.LastConsumedPane);
        Assert.Equal(DockFloatingCloseReason.ContentClosed, DockFloatingWindow.MigratedReasonFor(pane));

        DockDragSession.ResetForTest();
    }

    // ── DockFloatingTracker pending-close stash round-trip ─────────────

    [Fact]
    public void PendingClose_DefaultsToContentClosed_WhenNothingStashed()
    {
        var key = new object();

        var pending = DockFloatingTracker.TakePendingCloseCore(key);

        Assert.Equal(DockFloatingCloseReason.ContentClosed, pending.Reason);
        Assert.Null(pending.Content);
    }

    [Fact]
    public void PendingClose_RoundTripsReasonAndContent()
    {
        var key = new object();
        var pane = Pane("doc:a");

        DockFloatingTracker.SetPendingCloseCore(key, DockFloatingCloseReason.MigratedToHost, pane);
        var pending = DockFloatingTracker.TakePendingCloseCore(key);

        Assert.Equal(DockFloatingCloseReason.MigratedToHost, pending.Reason);
        Assert.Same(pane, pending.Content);
    }

    [Fact]
    public void PendingClose_IsClearedAfterTake()
    {
        var key = new object();
        var pane = Pane("doc:a");
        DockFloatingTracker.SetPendingCloseCore(key, DockFloatingCloseReason.MigratedToFloat, pane);

        _ = DockFloatingTracker.TakePendingCloseCore(key);
        var second = DockFloatingTracker.TakePendingCloseCore(key);

        // Second read sees no stash → genuine close semantics.
        Assert.Equal(DockFloatingCloseReason.ContentClosed, second.Reason);
        Assert.Null(second.Content);
    }

    [Fact]
    public void PendingClose_ClearedOnUnregister_LeavesNoStaleStash()
    {
        // A window can unregister (host unmount, disposal) WITHOUT ever
        // routing through the Closed handler that calls TakePendingClose.
        // If the stash survived, a later window reusing the reference would
        // wrongly inherit a migration reason. Production Unregister drops it
        // directly via _pendingClose.Remove(window); ClearPendingCloseCore is
        // the headless seam exercising that same removal so the next read is a
        // genuine close.
        var key = new object();
        DockFloatingTracker.SetPendingCloseCore(key, DockFloatingCloseReason.MigratedToHost, Pane("doc:a"));

        DockFloatingTracker.ClearPendingCloseCore(key);
        var pending = DockFloatingTracker.TakePendingCloseCore(key);

        Assert.Equal(DockFloatingCloseReason.ContentClosed, pending.Reason);
        Assert.Null(pending.Content);
    }
}
