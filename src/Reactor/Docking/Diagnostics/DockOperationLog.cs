using System.Diagnostics;
using Microsoft.UI.Reactor.Docking.Persistence;

namespace Microsoft.UI.Reactor.Docking.Diagnostics;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 — Docking operation log.
//
//  In-memory ring buffer recording every state-altering operation that
//  flows through a DockManager: drag begin / hover / confirm / cancel /
//  tear-out, splitter resize, programmatic layout mutation, and the
//  layout snapshot that resulted. Capped at MaxOperations entries (1K
//  by default) so it never grows unbounded.
//
//  Mirrored to Debug.WriteLine on each append so devs can scrub logs
//  with DebugView / VS Output without touching the in-memory buffer.
//
//  Replay model: each entry carries the POST-state (layout + ratios).
//  Replay = scrub the cursor to entry N, hand its Layout + Ratios back
//  to the app, the docking renderer applies them. Gesture-level replay
//  (synthesized pointer events) is intentionally out of scope — the
//  layout effects ARE the user-visible outcome of every gesture, so
//  replaying the post-states reproduces the visible journey.
//
//  Lifetime: the log instance is owned by the consumer (typically the
//  app's root component via UseRef so it survives renders). Kept in
//  place through P1-P4 per spec for ongoing debugging.
// ════════════════════════════════════════════════════════════════════════

/// <summary>Kind of operation recorded by <see cref="DockOperationLog"/>.</summary>
public enum DockOperationKind
{
    /// <summary>Initial layout captured at mount.</summary>
    Mount,
    /// <summary>Tab drag began — source pane captured into a DockDragSession.</summary>
    DragStart,
    /// <summary>Drop-target overlay hover changed during a drag.</summary>
    DragHover,
    /// <summary>User confirmed a drop target — layout mutated.</summary>
    DragConfirm,
    /// <summary>User cancelled the drag (Esc or dismiss).</summary>
    DragCancel,
    /// <summary>Drag ended outside any tab strip — pane torn out to a floating window.</summary>
    DragTearOut,
    /// <summary>Splitter drag completed — ratios shifted.</summary>
    SplitterResize,
    /// <summary>
    /// Splitter intermediate event during a drag (pressed / moved / released
    /// snapshots). Carries the math behind the in-flight inline-size
    /// mutation so cursor-tracking + jump-back regressions can be traced.
    /// </summary>
    SplitterTrace,
    /// <summary>Programmatic / external layout change observed (e.g. app set new Layout).</summary>
    LayoutChange,
    /// <summary>Free-form note appended by app code (debug breadcrumbs).</summary>
    Note,
}

/// <summary>
/// A single recorded operation. All fields are snapshots — the
/// log never holds references to mutable state.
/// </summary>
public sealed record DockOperation
{
    /// <summary>When this operation was recorded (UTC).</summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>What kind of operation this is.</summary>
    public DockOperationKind Kind { get; init; }

    /// <summary>Short human-readable summary, e.g. "drop SplitRight on path '0/0'".</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Pane key involved in the operation (drag source, dropped pane,
    /// etc.), or null when not applicable.
    /// </summary>
    public string? PaneKey { get; init; }

    /// <summary>
    /// Drop target chosen on confirm, or null when not applicable
    /// (splitter / mount / layout-change events).
    /// </summary>
    public DockTarget? Target { get; init; }

    /// <summary>
    /// Layout snapshot AFTER this operation applied. Replay scrubs to
    /// this snapshot.
    /// </summary>
    public DockNode? Layout { get; init; }

    /// <summary>
    /// Ratio snapshot AFTER this operation applied. Keyed by
    /// tree-position path (e.g. "0/0"); values per-child ratios.
    /// </summary>
    public IReadOnlyDictionary<string, double[]>? Ratios { get; init; }

    /// <summary>JSON of <see cref="Layout"/> via DockLayoutSerializer; computed lazily / on append.</summary>
    public string? LayoutJson { get; init; }
}

/// <summary>
/// Ring-buffer log of docking operations + a cursor-based replay API.
/// Thread-affined to the UI thread — appends from background threads
/// throw, matching the rest of the docking subsystem (spec §8.10).
/// </summary>
/// <remarks>Spec 045 — long-lived diagnostic infrastructure.</remarks>
public sealed class DockOperationLog
{
    /// <summary>Max entries retained in the ring buffer.</summary>
    public const int MaxOperations = 1000;

    private readonly List<DockOperation> _ops = new(64);
    private int _cursor;
    private bool _emitToDebug = true;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    private void ThrowIfOffThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                "DockOperationLog must be called on the owning UI dispatcher. " +
                "Spec 045 §8.10 — docking subsystem is UI-thread-affined.");
    }

    /// <summary>All operations currently held, oldest first.</summary>
    public IReadOnlyList<DockOperation> Operations
    {
        get { ThrowIfOffThread(); return _ops; }
    }

    /// <summary>Current count of recorded operations (≤ <see cref="MaxOperations"/>).</summary>
    public int Count => _ops.Count;

    /// <summary>
    /// Replay cursor. 0 = before any operation has been applied (the
    /// state before the very first recorded op). Count = after the
    /// most-recent op. <see cref="Rewind"/> / <see cref="StepForward"/>
    /// move this; <see cref="Append"/> snaps it to Count.
    /// </summary>
    public int Cursor => _cursor;

    /// <summary>The operation at the cursor's last-applied position, or null when no ops or cursor at 0.</summary>
    public DockOperation? Current =>
        _cursor > 0 && _cursor <= _ops.Count ? _ops[_cursor - 1] : null;

    /// <summary>When true (default), each <see cref="Append"/> also writes to <see cref="Debug.WriteLine(string)"/>.</summary>
    public bool EmitToDebug
    {
        get => _emitToDebug;
        set => _emitToDebug = value;
    }

    /// <summary>
    /// Append a new operation. Ring-buffer trim keeps the most-recent
    /// <see cref="MaxOperations"/> entries. Cursor snaps to the end so
    /// the latest op is the live state.
    /// </summary>
    public void Append(DockOperation op)
    {
        ArgumentNullException.ThrowIfNull(op);
        ThrowIfOffThread();
        // Drop the oldest if we're at capacity.
        if (_ops.Count >= MaxOperations)
        {
            _ops.RemoveAt(0);
            if (_cursor > 0) _cursor--;
        }
        _ops.Add(op);
        _cursor = _ops.Count;
        if (_emitToDebug) Debug.WriteLine(FormatForDebug(op));
    }

    /// <summary>
    /// Convenience overload — captures TimestampUtc, computes JSON
    /// from <paramref name="layout"/> via <see cref="DockLayoutSerializer.Save"/>,
    /// clones <paramref name="ratios"/> into an immutable snapshot.
    /// </summary>
    public DockOperation Record(
        DockOperationKind kind,
        string description,
        DockNode? layout = null,
        IDictionary<string, double[]>? ratios = null,
        string? paneKey = null,
        DockTarget? target = null)
    {
        ThrowIfOffThread();
        var op = new DockOperation
        {
            TimestampUtc = DateTime.UtcNow,
            Kind = kind,
            Description = description,
            PaneKey = paneKey,
            Target = target,
            Layout = layout,
            Ratios = ratios is null ? null : CloneRatios(ratios),
            LayoutJson = layout is null ? null : DockLayoutSerializer.Save(layout),
        };
        Append(op);
        return op;
    }

    /// <summary>Clear the log and reset the cursor.</summary>
    public void Reset()
    {
        ThrowIfOffThread();
        _ops.Clear();
        _cursor = 0;
    }

    /// <summary>
    /// Move the cursor one step back. Returns the operation now at the
    /// cursor (the one to "show"), or null if already at the beginning.
    /// </summary>
    public DockOperation? Rewind()
    {
        ThrowIfOffThread();
        if (_cursor <= 0) return null;
        _cursor--;
        return Current;
    }

    /// <summary>
    /// Move the cursor one step forward. Returns the operation now at
    /// the cursor, or null if already at the end.
    /// </summary>
    public DockOperation? StepForward()
    {
        ThrowIfOffThread();
        if (_cursor >= _ops.Count) return null;
        _cursor++;
        return Current;
    }

    /// <summary>Jump the cursor to a specific index (clamped).</summary>
    public DockOperation? SeekTo(int index)
    {
        ThrowIfOffThread();
        _cursor = Math.Clamp(index, 0, _ops.Count);
        return Current;
    }

    /// <summary>Drop every entry after the cursor — used when re-recording from a replay point.</summary>
    public void TruncateAfterCursor()
    {
        ThrowIfOffThread();
        if (_cursor < _ops.Count) _ops.RemoveRange(_cursor, _ops.Count - _cursor);
    }

    private static Dictionary<string, double[]> CloneRatios(IDictionary<string, double[]> source)
    {
        var clone = new Dictionary<string, double[]>(source.Count);
        foreach (var kvp in source)
        {
            var arr = new double[kvp.Value.Length];
            Array.Copy(kvp.Value, arr, kvp.Value.Length);
            clone[kvp.Key] = arr;
        }
        return clone;
    }

    private static string FormatForDebug(DockOperation op)
    {
        var ts = op.TimestampUtc.ToString("HH:mm:ss.fff");
        var ratioCount = op.Ratios?.Count ?? 0;
        var paneSeg = op.PaneKey is null ? "" : $" pane={op.PaneKey}";
        var targetSeg = op.Target is null ? "" : $" target={op.Target}";
        return $"[DockOps {ts}] {op.Kind}{paneSeg}{targetSeg} ratios={ratioCount} :: {op.Description}";
    }
}
