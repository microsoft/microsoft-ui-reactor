using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.14 — drag-start gate test seam.
//
//  The DockHostNativeComponent's HandleTabDragStarting closure runs the
//  permission check (`pane.CanMove`) before calling DockDragSession.Begin.
//  In the headless self-test harness there's no programmatic surface for
//  the WinUI TabView.TabDragStarting event, so without this bridge the
//  only way to exercise the gate is to drive a real pointer drag — which
//  the harness can't do.
//
//  The host registers a TryStartDrag delegate on each render that mirrors
//  the same gate. Test fixtures look it up via TryGet and invoke it
//  against panes built with CanMove = true/false to assert the contract
//  (refuses pinned panes; accepts movable panes; produces an active
//  DockDragSession on accept).
// ════════════════════════════════════════════════════════════════════════

internal static class DockDragGateBridge
{
    /// <summary>
    /// Returns <c>true</c> when the host accepts a drag for the given
    /// pane (gate passed + session started); <c>false</c> when the gate
    /// refuses or no session could begin.
    /// </summary>
    public delegate bool TryStartDrag(DockableContent pane, int sourceTabIndex);

    private static readonly ConditionalWeakTable<object, TryStartDrag> _table = new();

    // Keyed by the host (DockHostIdentity) like the other per-host bridges.
    public static void Set(DockManager element, TryStartDrag handler) =>
        _table.AddOrUpdate(DockHostIdentity.KeyFor(element), handler);

    public static TryStartDrag? Get(DockManager? element)
    {
        if (element is null) return null;
        return _table.TryGetValue(DockHostIdentity.KeyFor(element), out var h) ? h : null;
    }

    public static void Clear(DockManager element) => _table.Remove(DockHostIdentity.KeyFor(element));

    public static void Clear(DockHostIdentity host) => _table.Remove(host);
}
