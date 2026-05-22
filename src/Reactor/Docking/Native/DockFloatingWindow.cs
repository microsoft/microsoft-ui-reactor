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
        // A single-element holder lets BuildFloatingRoot find the
        // window (for tab-close → window.Close, etc.) even though we
        // can't reference `window` directly before OpenWindow returns.
        // The holder is captured by both the render closure and the
        // post-construction assignment; the render closure is invoked
        // by the host's dispatcher loop after OpenWindow returns, so
        // the holder is always populated by the time it's read.
        var windowHolder = new ReactorWindow?[] { null };
        var window = ReactorApp.OpenWindow(spec, _ => BuildFloatingRoot(pane, windowHolder, manager));
        windowHolder[0] = window;
        DockFloatingTracker.Register(window);
        DockFloatingTracker.RegisterEntry(window, pane, spec.Width, spec.Height);
        if (manager is not null) DockFloatingTracker.RegisterFor(manager, window);
        // Spec 045 §2.6 — fire OnFloatingWindowCreated so apps can
        // observe the new top-level for telemetry / persistence /
        // window-grouping bookkeeping.
        manager?.OnFloatingWindowCreated?.Invoke(new DockFloatingWindowCreatedEventArgs
        {
            DraggedSource = pane,
        });
        window.Closed += (_, _) =>
        {
            DockFloatingTracker.Unregister(window);
            if (manager is not null) DockFloatingTracker.UnregisterFor(manager, window);
            // Spec 045 §2.6 — paired OnFloatingWindowClosed event. Fires
            // for both user-initiated close and host-unmount close
            // (DockingNativeInterop iterates DockFloatingTracker.SnapshotFor
            // and calls Close() on each); the pane content reference may
            // be stale after a cross-window dock-back already migrated it
            // to another host.
            manager?.OnFloatingWindowClosed?.Invoke(new DockFloatingWindowClosedEventArgs
            {
                Content = pane,
            });
        };
        return window;
    }

    private static Element BuildFloatingRoot(DockableContent pane, ReactorWindow?[] windowHolder, DockManager? manager)
    {
        // Spec 045 §2.4 cross-window dock-back. The floating window's
        // content is a `DockTabGroup`-rendered `TabView` with the pane
        // as its single tab. The tab carries CanDragTabs=true so the
        // user can drag it OUT of the floating window — when WinUI
        // signals wasOutside=true, our handler:
        //   • If session.Current is null after the drag, the drop was
        //     consumed by another host's overlay (e.g. the main shell's
        //     per-group cluster). Close this floating window so the
        //     pane only lives in the destination.
        //   • Otherwise the drop landed nowhere (Esc, drop on desktop,
        //     unrecognised surface). End the session and keep the pane
        //     here — preserves the user's pane against an accidental
        //     drag.
        //
        // The chrome wrapper also gives the floating window a visible
        // tab header (Title + close-X), addressing the §2.6 "tab header
        // missing in floating window" gap that drove this slice.
        var tabGroup = new DockTabGroup(new DockableContent[] { pane });
        var info = new DockPaneInfo(pane.Key, pane.Title ?? string.Empty, pane);
        var rendered = DockTabGroupRenderer.Render(
            tabGroup,
            renderLeafContent: doc => doc.Content ?? (Element)new BorderElement(null),
            onSelectedIndexChanged: null,
            onTabClosing: _ =>
            {
                // Closing the last tab closes the window.
                windowHolder[0]?.Close();
            },
            onTabDragStarting: (doc, _) =>
            {
                // Begin a cross-window session. The `manager` reference
                // is the main host that originally owned this pane —
                // SourceManager is non-null per the session contract.
                if (DockDragSession.Current is { IsActive: true }) return;
                if (manager is null) return;
                DockDragSession.Begin(doc, manager, 0);
            },
            onTabDragCompleted: (_, _, _) =>
            {
                // Cross-window dock-back signal: a dock surface
                // (this app's main host or any other DockHost) set
                // `DockDragSession.Consumed = true` in its overlay
                // OnConfirm before ending the session. When we see
                // that here, the pane has been re-docked into another
                // host and this floating window should close.
                //
                // `wasOutside` is unreliable for this decision because
                // the receiving overlay accepts the drop with
                // `DataPackageOperation.Move` to suppress WinUI's
                // tear-out fallback — that flips wasOutside to false
                // even though the tab visually left every TabView.
                if (DockDragSession.Consumed)
                {
                    windowHolder[0]?.Close();
                    return;
                }
                // Drop landed outside any docking surface (or session
                // is still running). End any in-flight session so the
                // pane stays put in this floating window.
                DockDragSession.Current?.Cancel();
            });

        return rendered
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
    /// <summary>
    /// Per-window tracking record. <see cref="Pane"/> is the pane currently
    /// hosted in the floating window; the initial size is captured at Open
    /// time so <see cref="DockHostModel.Floating"/> snapshots can include
    /// at least the spec dimensions until live bounds tracking is wired.
    /// </summary>
    internal sealed record Entry(ReactorWindow Window, DockableContent Pane, double InitialWidth, double InitialHeight);

    private static readonly object _lock = new();
    private static readonly HashSet<ReactorWindow> _open = new();
    private static readonly Dictionary<ReactorWindow, Entry> _entries = new();
    private static readonly ConditionalWeakTable<DockManager, HashSet<ReactorWindow>> _byManager = new();

    public static void Register(ReactorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_lock) { _open.Add(window); }
    }

    /// <summary>
    /// Records pane + spec bounds for a freshly-opened floating window so
    /// snapshots (model.Floating, serialization) can surface what's
    /// currently floating. Bounds are the initial spec values — live
    /// position/size tracking is a future refinement (§2.6 follow-up).
    /// </summary>
    public static void RegisterEntry(ReactorWindow window, DockableContent pane, double initialWidth, double initialHeight)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(pane);
        lock (_lock)
        {
            _entries[window] = new Entry(window, pane, initialWidth, initialHeight);
        }
    }

    public static void Unregister(ReactorWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_lock)
        {
            _open.Remove(window);
            _entries.Remove(window);
        }
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

    /// <summary>
    /// Builds the read-only snapshot consumed by <see cref="DockHostModel.Floating"/>.
    /// Each entry carries the pane + best-effort bounds (initial spec
    /// dimensions today; live x/y/w/h tracking is the §2.6 follow-up).
    /// </summary>
    public static IReadOnlyList<FloatingDockWindow> SnapshotPanesFor(DockManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        lock (_lock)
        {
            if (!_byManager.TryGetValue(manager, out var set) || set.Count == 0)
                return Array.Empty<FloatingDockWindow>();
            var list = new List<FloatingDockWindow>(set.Count);
            foreach (var window in set)
            {
                if (!_entries.TryGetValue(window, out var entry)) continue;
                list.Add(new FloatingDockWindow
                {
                    // Window object identity is the stable id — survives
                    // re-render cycles within the host's lifetime.
                    Id = window,
                    Contents = new[] { entry.Pane },
                    Width = entry.InitialWidth,
                    Height = entry.InitialHeight,
                    // X/Y aren't tracked yet; snapshot reports 0 until the
                    // §2.6 live-bounds follow-up lands.
                });
            }
            return list;
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
