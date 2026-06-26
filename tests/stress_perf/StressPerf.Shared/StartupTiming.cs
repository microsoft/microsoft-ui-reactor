using System.Diagnostics;

namespace StressPerf.Shared;

/// <summary>
/// Process-wide startup anchors for the first-frame / startup metric. The managed
/// entry timestamp must be captured at the very top of <c>Main</c> — BEFORE the
/// WinUI app is built and well before the per-render <see cref="PerfTracker"/>
/// instance exists — so it lives here as a process static rather than on the
/// tracker. Times are monotonic <see cref="Stopwatch"/> ticks; cost is a couple of
/// timestamp reads that all happen once at startup, so the steady-state render loop
/// is never perturbed. AOT/trim-safe (no reflection, no allocation).
/// </summary>
public static class StartupTiming
{
    private static long _entryTimestamp;
    private static bool _entryMarked;
    private static long _windowOpenTimestamp;
    private static bool _windowOpenMarked;

    /// <summary>
    /// Record managed entry. Call as the FIRST statement of <c>Main</c> (before any
    /// WinUI bootstrap) so the entry→first-reconcile / entry→first-frame segments
    /// measure the whole managed-startup cost. Idempotent — the first call wins, so a
    /// stray repeat (or a workload that calls it twice) cannot move the anchor.
    /// </summary>
    public static void MarkEntry()
    {
        if (_entryMarked) return;
        _entryTimestamp = Stopwatch.GetTimestamp();
        _entryMarked = true;
    }

    /// <summary>
    /// Record the first window <c>Activated</c>. Call from
    /// <c>ReactorApp.PrimaryWindow.Activated</c>. Idempotent (first wins). May never
    /// fire before the first reconcile — Activated-vs-mount ordering is
    /// non-deterministic across launches — which is why the window-open segment is
    /// n/a-guarded by the consumer rather than assumed monotonic.
    /// </summary>
    public static void MarkWindowOpen()
    {
        if (_windowOpenMarked) return;
        _windowOpenTimestamp = Stopwatch.GetTimestamp();
        _windowOpenMarked = true;
    }

    /// <summary>True once <see cref="MarkEntry"/> has run.</summary>
    public static bool EntryMarked => _entryMarked;

    /// <summary>True once <see cref="MarkWindowOpen"/> has run.</summary>
    public static bool WindowOpenMarked => _windowOpenMarked;

    /// <summary>
    /// Milliseconds from managed entry to now, or 0 when entry was never marked.
    /// </summary>
    public static double MsSinceEntry()
    {
        if (!_entryMarked) return 0.0;
        return (Stopwatch.GetTimestamp() - _entryTimestamp) * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>
    /// Milliseconds from managed entry to the window-open mark, or null when either
    /// anchor is unset (entry not marked, or the window never reported Activated).
    /// </summary>
    public static double? WindowOpenMsSinceEntry()
    {
        if (!_entryMarked || !_windowOpenMarked) return null;
        return (_windowOpenTimestamp - _entryTimestamp) * 1000.0 / Stopwatch.Frequency;
    }
}
