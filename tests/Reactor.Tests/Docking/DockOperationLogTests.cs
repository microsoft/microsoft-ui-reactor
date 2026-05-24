using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 045 — ring-buffer + cursor-replay invariants for
/// <see cref="DockOperationLog"/>. The log is UI-thread-affined; all tests
/// here run on the xUnit invocation thread (the constructor thread), which
/// is the log's "owning" thread, so on-thread calls succeed. The off-thread
/// throw is exercised by hopping to a Task.Run and catching.
/// </summary>
public sealed class DockOperationLogTests
{
    private static DockOperation Op(
        DockOperationKind kind = DockOperationKind.Note,
        string description = "",
        DockNode? layout = null,
        string? paneKey = null) =>
        new()
        {
            TimestampUtc = DateTime.UtcNow,
            Kind = kind,
            Description = description,
            Layout = layout,
            PaneKey = paneKey,
        };

    [Fact]
    public void NewLog_IsEmpty_CursorAtZero()
    {
        var log = new DockOperationLog();
        Assert.Equal(0, log.Count);
        Assert.Equal(0, log.Cursor);
        Assert.Null(log.Current);
        Assert.Empty(log.Operations);
    }

    [Fact]
    public void Append_AddsEntry_CursorSnapsToEnd()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        var op = Op(DockOperationKind.Mount, "first");
        log.Append(op);
        Assert.Equal(1, log.Count);
        Assert.Equal(1, log.Cursor);
        Assert.Same(op, log.Current);
        Assert.Same(op, log.Operations[0]);
    }

    [Fact]
    public void Append_Null_Throws()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        Assert.Throws<ArgumentNullException>(() => log.Append(null!));
    }

    [Fact]
    public void Append_BeyondMaxOperations_RingBufferDropsOldest()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        // Fill to capacity.
        for (int i = 0; i < DockOperationLog.MaxOperations; i++)
            log.Append(Op(description: $"op{i}"));
        Assert.Equal(DockOperationLog.MaxOperations, log.Count);
        Assert.Equal("op0", log.Operations[0].Description);

        // One more — oldest must drop.
        log.Append(Op(description: "overflow"));
        Assert.Equal(DockOperationLog.MaxOperations, log.Count);
        Assert.Equal("op1", log.Operations[0].Description);
        Assert.Equal("overflow", log.Operations[^1].Description);
        Assert.Equal(DockOperationLog.MaxOperations, log.Cursor);
    }

    [Fact]
    public void Append_AlwaysSnapsCursorToEnd()
    {
        // Documented contract: Append "snaps it to Count" regardless of
        // prior cursor position. Even when the ring-buffer evicts the
        // oldest entry, the cursor lands on the just-appended op.
        var log = new DockOperationLog { EmitToDebug = false };
        for (int i = 0; i < DockOperationLog.MaxOperations; i++)
            log.Append(Op(description: $"op{i}"));
        log.SeekTo(500);
        Assert.Equal(500, log.Cursor);
        log.Append(Op(description: "after-mid"));
        Assert.Equal(DockOperationLog.MaxOperations, log.Cursor);
        Assert.Equal("after-mid", log.Current!.Description);
    }

    [Fact]
    public void Append_AtCapacity_FromCursorZero_SnapsToEnd()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        for (int i = 0; i < DockOperationLog.MaxOperations; i++)
            log.Append(Op(description: $"op{i}"));
        log.SeekTo(0);
        log.Append(Op(description: "overflow"));
        Assert.Equal(DockOperationLog.MaxOperations, log.Cursor);
    }

    [Fact]
    public void Record_PopulatesTimestampAndJson_FromLayout()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        var pane = new Document { Key = "k", Title = "T", Content = null! };
        var layout = new DockTabGroup(new DockableContent[] { pane });
        var before = DateTime.UtcNow;
        var op = log.Record(
            DockOperationKind.DragConfirm,
            "drop SplitRight on '0/0'",
            layout: layout,
            paneKey: "k",
            target: DockTarget.SplitRight);

        Assert.Same(op, log.Current);
        Assert.True(op.TimestampUtc >= before.AddSeconds(-1));
        Assert.Equal(DockOperationKind.DragConfirm, op.Kind);
        Assert.Equal("drop SplitRight on '0/0'", op.Description);
        Assert.Equal("k", op.PaneKey);
        Assert.Equal(DockTarget.SplitRight, op.Target);
        Assert.Same(layout, op.Layout);
        Assert.False(string.IsNullOrEmpty(op.LayoutJson));
        Assert.Contains("\"$schema\"", op.LayoutJson);
    }

    [Fact]
    public void Record_NullLayout_LayoutJsonStaysNull()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        var op = log.Record(DockOperationKind.Note, "no layout");
        Assert.Null(op.LayoutJson);
        Assert.Null(op.Layout);
        Assert.Null(op.Ratios);
    }

    [Fact]
    public void Record_ClonesRatios_DefensiveCopy()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        var src = new Dictionary<string, double[]>
        {
            ["0"] = new[] { 0.3, 0.7 },
            ["0/0"] = new[] { 0.5, 0.5 },
        };
        var op = log.Record(DockOperationKind.SplitterResize, "resize", ratios: src);

        Assert.NotNull(op.Ratios);
        // Mutating the source dictionary AND the source array must not leak into the snapshot.
        src["0"][0] = 99.0;
        src["new"] = new[] { 1.0 };
        Assert.Equal(0.3, op.Ratios["0"][0]);
        Assert.False(op.Ratios.ContainsKey("new"));
        Assert.Equal(2, op.Ratios.Count);
    }

    [Fact]
    public void Reset_ClearsAndRewindsCursor()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        log.Append(Op());
        log.Append(Op());
        log.Reset();
        Assert.Equal(0, log.Count);
        Assert.Equal(0, log.Cursor);
        Assert.Null(log.Current);
    }

    [Fact]
    public void Rewind_FromEnd_StepsBackOne()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        log.Append(Op(description: "a"));
        log.Append(Op(description: "b"));
        Assert.Equal(2, log.Cursor);
        var current = log.Rewind();
        Assert.Equal(1, log.Cursor);
        Assert.Equal("a", current!.Description);
    }

    [Fact]
    public void Rewind_AtZero_ReturnsNullAndStays()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        log.Append(Op());
        log.SeekTo(0);
        Assert.Null(log.Rewind());
        Assert.Equal(0, log.Cursor);
    }

    [Fact]
    public void StepForward_FromMiddle_AdvancesOne()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        log.Append(Op(description: "a"));
        log.Append(Op(description: "b"));
        log.SeekTo(1);
        var current = log.StepForward();
        Assert.Equal(2, log.Cursor);
        Assert.Equal("b", current!.Description);
    }

    [Fact]
    public void StepForward_AtEnd_ReturnsNullAndStays()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        log.Append(Op());
        Assert.Null(log.StepForward());
        Assert.Equal(1, log.Cursor);
    }

    [Fact]
    public void SeekTo_ClampsToValidRange()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        log.Append(Op());
        log.Append(Op());

        log.SeekTo(-5);
        Assert.Equal(0, log.Cursor);

        log.SeekTo(999);
        Assert.Equal(2, log.Cursor);

        log.SeekTo(1);
        Assert.Equal(1, log.Cursor);
    }

    [Fact]
    public void TruncateAfterCursor_DropsTail()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        log.Append(Op(description: "a"));
        log.Append(Op(description: "b"));
        log.Append(Op(description: "c"));
        log.SeekTo(1);
        log.TruncateAfterCursor();
        Assert.Equal(1, log.Count);
        Assert.Equal("a", log.Operations[0].Description);
        Assert.Equal(1, log.Cursor);
    }

    [Fact]
    public void TruncateAfterCursor_NoTail_IsNoOp()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        log.Append(Op());
        log.Append(Op());
        // Cursor at end; nothing to drop.
        log.TruncateAfterCursor();
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void EmitToDebug_DefaultsTrue_TogglesOff()
    {
        var log = new DockOperationLog();
        Assert.True(log.EmitToDebug);
        log.EmitToDebug = false;
        Assert.False(log.EmitToDebug);
    }

    [Fact]
    public void EmitToDebug_True_AppendStillSucceeds()
    {
        // Smoke: appending with debug emit enabled doesn't throw even if no
        // listener is attached. (Debug.WriteLine is a no-op in this config
        // but must not crash the append path.)
        var log = new DockOperationLog { EmitToDebug = true };
        log.Append(Op(description: "debug-on"));
        Assert.Equal(1, log.Count);
    }

    [Fact]
    public void Current_IndexedByLastAppliedOp()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        var a = Op(description: "a");
        var b = Op(description: "b");
        log.Append(a);
        log.Append(b);
        Assert.Same(b, log.Current); // cursor==2 → last-applied is index 1

        log.Rewind();                 // cursor==1 → last-applied is index 0
        Assert.Same(a, log.Current);

        log.SeekTo(0);                // cursor==0 → no op applied
        Assert.Null(log.Current);
    }

    [Fact]
    public void OffThread_Calls_Throw()
    {
        // Construct on this xUnit invocation thread. The log records this
        // thread's id as its owner; any call from a different OS thread
        // must throw InvalidOperationException.
        var log = new DockOperationLog { EmitToDebug = false };
        log.Append(Op());

        string? unexpected = null;
        // Use a dedicated Thread (not Task.Run) so the OS thread id is
        // guaranteed to differ from the test runner thread.
        var t = new Thread(() =>
        {
            try { _ = log.Operations; unexpected = "operations"; return; }
            catch (InvalidOperationException) { /* expected */ }
            try { log.Append(Op()); unexpected = "append"; return; }
            catch (InvalidOperationException) { /* expected */ }
            try { log.Record(DockOperationKind.Note, "x"); unexpected = "record"; return; }
            catch (InvalidOperationException) { /* expected */ }
            try { log.Reset(); unexpected = "reset"; return; }
            catch (InvalidOperationException) { /* expected */ }
            try { log.Rewind(); unexpected = "rewind"; return; }
            catch (InvalidOperationException) { /* expected */ }
            try { log.StepForward(); unexpected = "step"; return; }
            catch (InvalidOperationException) { /* expected */ }
            try { log.SeekTo(0); unexpected = "seek"; return; }
            catch (InvalidOperationException) { /* expected */ }
            try { log.TruncateAfterCursor(); unexpected = "truncate"; return; }
            catch (InvalidOperationException) { /* expected */ }
        });
        t.Start();
        t.Join();
        Assert.Null(unexpected);
    }

    [Fact]
    public void Operations_OnOwnerThread_Succeeds()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        log.Append(Op());
        // Just exercise that the property getter works on the owning thread.
        Assert.Single(log.Operations);
    }

    [Fact]
    public void Append_PostSnapshot_HoldsLayoutReferenceNotSnapshot()
    {
        // Spec contract: callers pass snapshots (immutable records); the log
        // never deep-clones the layout. Confirm reference equality so that
        // we know the log is a pointer-store, not a deep copy.
        var log = new DockOperationLog { EmitToDebug = false };
        var pane = new Document { Key = "k", Title = "T", Content = null! };
        var layout = new DockTabGroup(new DockableContent[] { pane });
        var op = log.Record(DockOperationKind.Mount, "init", layout: layout);
        Assert.Same(layout, op.Layout);
    }

    [Fact]
    public void Record_DefaultArgs_OnlyKindAndDescriptionRequired()
    {
        var log = new DockOperationLog { EmitToDebug = false };
        var op = log.Record(DockOperationKind.LayoutChange, "manual");
        Assert.Equal(DockOperationKind.LayoutChange, op.Kind);
        Assert.Equal("manual", op.Description);
        Assert.Null(op.PaneKey);
        Assert.Null(op.Target);
        Assert.Null(op.Layout);
        Assert.Null(op.LayoutJson);
        Assert.Null(op.Ratios);
    }

    [Fact]
    public void Operation_RecordEquality_ByValue()
    {
        // DockOperation is a record — equality should be structural. This
        // matters because tests / replay UIs may diff operations.
        var ts = DateTime.UtcNow;
        var a = new DockOperation
        {
            TimestampUtc = ts,
            Kind = DockOperationKind.Mount,
            Description = "x",
            PaneKey = "k",
        };
        var b = new DockOperation
        {
            TimestampUtc = ts,
            Kind = DockOperationKind.Mount,
            Description = "x",
            PaneKey = "k",
        };
        Assert.Equal(a, b);
    }

    [Fact]
    public void DockOperationKind_IntegralValues_PinnedForWireCompat()
    {
        // Triage tooling that pretty-prints a serialized DockOperationLog
        // dump expects each kind's integer value to be stable across
        // releases. Reordering or renumbering would silently misclassify
        // historical traces — pin the underlying numerics.
        Assert.Equal(0, (int)DockOperationKind.Mount);
        Assert.Equal(1, (int)DockOperationKind.DragStart);
        Assert.Equal(2, (int)DockOperationKind.DragHover);
        Assert.Equal(3, (int)DockOperationKind.DragConfirm);
        Assert.Equal(4, (int)DockOperationKind.DragCancel);
        Assert.Equal(5, (int)DockOperationKind.DragTearOut);
        Assert.Equal(6, (int)DockOperationKind.SplitterResize);
        Assert.Equal(7, (int)DockOperationKind.SplitterTrace);
        Assert.Equal(8, (int)DockOperationKind.LayoutChange);
        Assert.Equal(9, (int)DockOperationKind.Note);
        // No unexpected values added without updating this pin.
        Assert.Equal(10, Enum.GetValues<DockOperationKind>().Length);
    }
}
