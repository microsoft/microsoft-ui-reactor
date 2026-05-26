namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.4 — Reactor-native drag session.
//
//  Replaces upstream WinUI.Dock's static GUID→DockManager / GUID→Document
//  dictionaries (Helpers/DragDropHelpers.cs). Spec §8.9 calls those out as
//  a security/reliability anti-pattern (process-wide string-keyed payload
//  surface; cross-window string trust); the Reactor port uses object refs
//  via a single in-flight session.
//
//  Contract:
//   • At most one active session per process — matches upstream's
//     single-drag interaction model and spec §4.6 (drag-out restricted
//     to a single manager in P1, retained in P2 until cross-window
//     pipeline lands separately).
//   • UI-thread-affined: Begin / Confirm / Cancel run on the dispatcher
//     thread that owns the originating DockManager. Off-thread access is
//     undefined (matches DockHostModel contract per spec §8.10).
//   • No GC retention of completed sessions — End / Cancel null out
//     references so a closed pane / unmounted manager can be collected
//     immediately.
//
//  The session is purely a state holder. Layout mutation on confirm is
//  the host's responsibility (DockHostNativeComponent applies it via the
//  immutable-Layout-rebuild pattern).
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// In-flight tab-drag bookkeeping. Holds object references — never
/// serializable identifiers — so the drag payload can't be spoofed by an
/// untrusted code path and so a closed pane / unmounted host can be
/// collected the moment the session ends.
/// </summary>
/// <remarks>Spec 045 §2.4.</remarks>
internal sealed class DockDragSession
{
    /// <summary>The currently-active session, or null when no drag is in flight.</summary>
    public static DockDragSession? Current { get; private set; }

    /// <summary>
    /// True when the most recently ended session was consumed by a dock
    /// surface (a host's drop-target overlay called <see cref="MarkConsumed"/>
    /// before <see cref="End"/>). Cleared on the next <see cref="Begin"/>.
    /// Distinguishes "drop succeeded somewhere else" (Consumed=true) from
    /// "drop was cancelled / went nowhere" (Consumed=false) — used by the
    /// floating-window dock-back path to decide whether to close its
    /// own window after a tab-drag completes. Cross-window contract:
    /// the host that confirms the drop owns this flag.
    /// </summary>
    public static bool Consumed { get; private set; }

    /// <summary>
    /// Called by the host's drop-target overlay confirm path immediately
    /// before <see cref="End"/>. Sets <see cref="Consumed"/> so other
    /// windows participating in this drag (e.g. the source floating
    /// window) can distinguish "consumed" from "cancelled".
    /// </summary>
    public static void MarkConsumed() => Consumed = true;

    /// <summary>
    /// Fired on any session state transition (Begin / End / Cancel). Lets
    /// any <see cref="DockManager"/> in the process subscribe and surface
    /// drop targets when a drag begins in a different window (the
    /// floating-window → main-host cross-window dock-back path).
    /// Subscribers must be UI-thread-affined.
    /// </summary>
    public static event Action? SessionChanged;

    private DockDragSession(DockableContent source, DockManager sourceManager, int sourceTabIndex)
    {
        Source = source;
        SourceManager = sourceManager;
        SourceTabIndex = sourceTabIndex;
        StartedAtUtc = DateTime.UtcNow;
    }

    /// <summary>The pane being dragged.</summary>
    public DockableContent Source { get; }

    /// <summary>The originating <see cref="DockManager"/>.</summary>
    public DockManager SourceManager { get; }

    /// <summary>
    /// Index of the dragged tab in its originating group. Used by the
    /// host's layout rebuild to remove the source pane before re-inserting
    /// it at the target slot.
    /// </summary>
    public int SourceTabIndex { get; }

    /// <summary>When the session started (UTC). Diagnostic / telemetry.</summary>
    public DateTime StartedAtUtc { get; }

    /// <summary>True from <see cref="Begin"/> until <see cref="End"/> / <see cref="Cancel"/>.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Opaque stable token identifying the host that owns this session.
    /// <see cref="DockManager"/> instances are rebuilt every parent render
    /// (records-by-value with closures that don't compare equal), so they
    /// can't serve as an identity for cleanup-time matching. The host
    /// supplies a per-instance stable handle (typically its
    /// <c>DockHostModel</c>, which is UseRef-backed) so its own UseEffect
    /// cleanup can identify "this is my session" and cancel it when the
    /// host unmounts — preventing the process-static
    /// <see cref="Current"/> slot from leaking across scene switches.
    /// Caught by SplitterMatrix_L12_HostUnmount_CancelsOwnedDragSession.
    /// </summary>
    public object? OwnerToken { get; set; }

    /// <summary>
    /// Begin a new session. Returns the session if one was started, or null
    /// if another session is already in flight (the spec's "single drag at
    /// a time" contract). Caller is responsible for raising
    /// <see cref="DockManager.OnContentFloating"/> etc.
    /// </summary>
    /// <param name="owner">
    /// Optional stable per-host token (see <see cref="OwnerToken"/>). The
    /// host's UseEffect cleanup compares this to identify its own
    /// sessions on unmount. Older callers (e.g. tests that don't model
    /// the cross-render lifecycle) may omit it.
    /// </param>
    public static DockDragSession? Begin(
        DockableContent source,
        DockManager sourceManager,
        int sourceTabIndex,
        object? owner = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceManager);
        if (Current is { IsActive: true }) return null;
        var session = new DockDragSession(source, sourceManager, sourceTabIndex)
        {
            OwnerToken = owner,
        };
        Current = session;
        Consumed = false; // reset Consumed for the new session
        RaiseSessionChanged();
        return session;
    }

    /// <summary>
    /// End the session normally (e.g. after confirming a drop target or
    /// completing a tear-out). Idempotent.
    /// </summary>
    public void End()
    {
        if (!IsActive) return;
        IsActive = false;
        if (ReferenceEquals(Current, this)) Current = null;
        RaiseSessionChanged();
    }

    private static void RaiseSessionChanged() => SessionChanged?.Invoke();

    /// <summary>
    /// Cancel the session (Esc, capture loss, manager unmounted). Idempotent.
    /// Functionally identical to <see cref="End"/> — the distinction lives
    /// in the caller's event surface (OnContentDocked vs no-op).
    /// </summary>
    public void Cancel() => End();

    /// <summary>Test hook — forcibly reset the static slot. Internal-only.</summary>
    internal static void ResetForTest()
    {
        if (Current is { IsActive: true }) Current.Cancel();
        Current = null;
    }
}
