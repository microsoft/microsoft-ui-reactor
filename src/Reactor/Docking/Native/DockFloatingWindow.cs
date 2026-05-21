using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.6 — floating windows are real Reactor Windows.
//
//  Opens a top-level Reactor `ReactorWindow` with the pane mounted as its
//  root (per spec §2.6: "Do not build a mini-window primitive"). The pane
//  Content is rendered inside the floating window with the same
//  DockContext envelope (PaneState = Floating) so hooks resolve the same
//  way inside a floating pane as inside a docked one.
//
//  Items implemented:
//    • Multi-display clamp via DockFloatingClamp.Clamp — saved off-screen
//      bounds (e.g. (10000, 10000) on a single-display rig) recenter on
//      primary. Spec §2.25 reliability.
//    • Per-host tracking via DockFloatingTracker.RegisterFor — the host's
//      unmount handler closes floating windows associated with its
//      DockManager element so they don't outlive the host. Spec §2.25.
//    • Owner forwarding to WindowSpec.Owner — orphan-on-shell-close
//      respects spec 036 §9 ownership. Spec §2.25.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Opens a real Reactor window hosting a single dock pane. The returned
/// <see cref="ReactorWindow"/> closes naturally on user dismissal; when
/// closed it removes itself from the manager's tracking list (see
/// <see cref="DockFloatingTracker"/>).
/// </summary>
public static class DockFloatingWindow
{
    /// <summary>
    /// Open a floating window containing the given pane. Must be called on
    /// the UI thread.
    /// </summary>
    /// <param name="pane">The pane to host. Required.</param>
    /// <param name="title">Window title; defaults to <c>pane.Title</c> or "Floating Window".</param>
    /// <param name="width">Initial width (DIPs). Defaults to 480.</param>
    /// <param name="height">Initial height (DIPs). Defaults to 320.</param>
    /// <param name="owner">Optional owner window for owned-window semantics (spec 036 §9).</param>
    /// <param name="savedBounds">Optional restored (x, y, w, h) from layout JSON;
    /// clamped against <paramref name="displays"/> if provided.</param>
    /// <param name="displays">Optional display rectangles for the clamp.
    /// When null, no clamp is applied (current behavior); callers wiring
    /// against <c>DisplayArea.FindAll</c> pass the enumerated set.</param>
    /// <param name="manager">Optional DockManager that owns this floating
    /// window. When supplied, the window is associated with that manager's
    /// tracking set; the host unmount handler closes it. When null,
    /// the window is tracked only in the global set.</param>
    /// <returns>The opened <see cref="ReactorWindow"/>.</returns>
    public static ReactorWindow Open(
        DockableContent pane,
        string? title = null,
        double width = 480,
        double height = 320,
        ReactorWindow? owner = null,
        DockFloatingBounds? savedBounds = null,
        IReadOnlyList<DockDisplay>? displays = null,
        DockManager? manager = null)
    {
        ArgumentNullException.ThrowIfNull(pane);

        var bounds = savedBounds;
        if (bounds is { } b && displays is { Count: > 0 } ds)
            bounds = DockFloatingClamp.Clamp(b, ds);

        var spec = new WindowSpec
        {
            Title = title ?? (string.IsNullOrEmpty(pane.Title)
                ? DockingStrings.Get(DockingStringKeys.FloatingWindowDefaultTitle)
                : pane.Title),
            Width = bounds?.Width ?? width,
            Height = bounds?.Height ?? height,
            Owner = owner,
        };

        // Wrap the pane content with the same DockContext envelope used
        // by the docked tree, but flag PaneState as Floating so
        // UseDockState resolves correctly inside the floating window.
        var window = ReactorApp.OpenWindow(spec, ctx => BuildFloatingRoot(pane));
        DockFloatingTracker.Register(window);
        if (manager is not null) DockFloatingTracker.RegisterFor(manager, window);
        window.Closed += (_, _) =>
        {
            DockFloatingTracker.Unregister(window);
            if (manager is not null) DockFloatingTracker.UnregisterFor(manager, window);
        };
        return window;
    }

    private static Element BuildFloatingRoot(DockableContent pane)
    {
        var content = pane.Content ?? (Element)new BorderElement(null);
        var info = new DockPaneInfo(pane.Key, pane.Title ?? string.Empty, pane);
        return content
            .Padding(16)
            .Provide(DockContexts.Pane, (DockPaneInfo?)info)
            .Provide(DockContexts.PaneState, DockPaneState.Floating);
    }
}

/// <summary>
/// Tracks the set of floating windows opened by the docking subsystem so
/// the manager can enumerate / close-on-unmount them. Holds both a global
/// set (for diagnostic enumeration) and a per-<see cref="DockManager"/>
/// set (so each host can close its own floating windows on unmount
/// without affecting other hosts in the process).
/// </summary>
internal static class DockFloatingTracker
{
    private static readonly object _lock = new();
    private static readonly HashSet<ReactorWindow> _open = new();
    private static readonly ConditionalWeakTable<DockManager, HashSet<ReactorWindow>> _byManager = new();

    public static void Register(ReactorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_lock) { _open.Add(window); }
    }

    public static void Unregister(ReactorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_lock) { _open.Remove(window); }
    }

    public static void RegisterFor(DockManager manager, ReactorWindow window)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(window);
        lock (_lock)
        {
            if (!_byManager.TryGetValue(manager, out var set))
            {
                set = new HashSet<ReactorWindow>();
                _byManager.Add(manager, set);
            }
            set.Add(window);
        }
    }

    public static void UnregisterFor(DockManager manager, ReactorWindow window)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(window);
        lock (_lock)
        {
            if (_byManager.TryGetValue(manager, out var set)) set.Remove(window);
        }
    }

    public static IReadOnlyList<ReactorWindow> SnapshotFor(DockManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        lock (_lock)
        {
            return _byManager.TryGetValue(manager, out var set) ? set.ToArray() : Array.Empty<ReactorWindow>();
        }
    }

    public static int Count
    {
        get { lock (_lock) return _open.Count; }
    }

    public static IReadOnlyList<ReactorWindow> Snapshot()
    {
        lock (_lock) return _open.ToArray();
    }
}
