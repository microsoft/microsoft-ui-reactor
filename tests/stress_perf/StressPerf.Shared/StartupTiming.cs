using System.Diagnostics;
using System.Threading;

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
/// <remarks>
/// The anchors are written on one thread (entry on the managed-entry thread, window-open
/// on the UI thread) and may be read on another (metrics emit). Each anchor uses a
/// release/acquire <see cref="Volatile"/> pair on its "marked" flag: the writer publishes
/// the timestamp first and then <see cref="Volatile.Write(ref bool, bool)"/>s the flag, and
/// every reader <see cref="Volatile.Read(ref bool)"/>s the flag before touching the
/// timestamp. So a reader that observes the flag set is guaranteed to also see the published
/// timestamp (never a marked flag paired with a default-0 timestamp), and an unmarked anchor
/// reads as 0 / null rather than a torn value. 64-bit timestamp reads are atomic on the x64 /
/// arm64 runners the harness targets.
/// </remarks>
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
        if (Volatile.Read(ref _entryMarked)) return;
        _entryTimestamp = Stopwatch.GetTimestamp();
        Volatile.Write(ref _entryMarked, true); // release: publish the timestamp before the flag
    }

    /// <summary>
    /// Record the first window <c>Activated</c>. Call from
    /// <c>ReactorApp.PrimaryWindow.Activated</c>. Idempotent (first wins). In the current
    /// <c>ReactorWindow</c> lifecycle the host mounts (completing the first reconcile) BEFORE
    /// it activates the window, so <c>Activated</c> fires after the first reconcile and the
    /// consumer's monotonic n/a-guard rejects this anchor — it is retained for any future host
    /// or launch ordering where <c>Activated</c> can precede the mount, and is n/a-guarded by
    /// the consumer rather than assumed monotonic.
    /// </summary>
    public static void MarkWindowOpen()
    {
        if (Volatile.Read(ref _windowOpenMarked)) return;
        _windowOpenTimestamp = Stopwatch.GetTimestamp();
        Volatile.Write(ref _windowOpenMarked, true); // release: publish the timestamp before the flag
    }

    /// <summary>True once <see cref="MarkEntry"/> has run.</summary>
    public static bool EntryMarked => Volatile.Read(ref _entryMarked);

    /// <summary>True once <see cref="MarkWindowOpen"/> has run.</summary>
    public static bool WindowOpenMarked => Volatile.Read(ref _windowOpenMarked);

    /// <summary>
    /// Milliseconds from managed entry to now, or 0 when entry was never marked.
    /// </summary>
    public static double MsSinceEntry()
    {
        if (!Volatile.Read(ref _entryMarked)) return 0.0; // acquire: pairs with MarkEntry's release
        return (Stopwatch.GetTimestamp() - _entryTimestamp) * 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>
    /// Milliseconds from managed entry to the window-open mark, or null when either
    /// anchor is unset (entry not marked, or the window never reported Activated).
    /// </summary>
    public static double? WindowOpenMsSinceEntry()
    {
        // acquire both flags before reading either timestamp
        if (!Volatile.Read(ref _entryMarked) || !Volatile.Read(ref _windowOpenMarked)) return null;
        return (_windowOpenTimestamp - _entryTimestamp) * 1000.0 / Stopwatch.Frequency;
    }
}
