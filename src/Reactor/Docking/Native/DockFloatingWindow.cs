using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

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
            // Spec 045 §4.2 — floating dockable windows extend their
            // content into the title-bar zone so the docking TabView
            // (rendered as the window's root by BuildChrome) acts as
            // the window's visible chrome. The drag region is a
            // transparent BorderElement placed in the TabView's
            // TabStripFooter; its OnMount calls Window.SetTitleBar so
            // the OS reserves the caption-button inset and treats the
            // footer area as a window drag-move surface (§4.4).
            // We do NOT use Microsoft.UI.Xaml.Controls.TitleBar /
            // TitleBarElement here — its Content slot is an in-row
            // chrome slot (Edge address-bar style) and cannot host a
            // full TabView with bodies.
            ExtendsContentIntoTitleBar = true,
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
        var window = ReactorApp.OpenWindow(spec, _ =>
            new Microsoft.UI.Reactor.Core.ComponentElement<DockFloatingWindowProps>(
                typeof(DockFloatingWindowComponent),
                new DockFloatingWindowProps(pane, windowHolder, manager)));
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

    // Internal so the unit tests can exercise the floating-window
    // visual-tree shape (TitleBar wrap + tab chrome) without spinning
    // up a real WinUI window. The render-time host wiring still goes
    // through `Open()` for full coverage; unit tests assert tree
    // structure only.
    internal static Element BuildFloatingRoot(DockableContent pane, ReactorWindow?[] windowHolder, DockManager? manager)
    {
        var chrome = BuildChrome(
            new[] { pane },
            windowHolder,
            manager,
            onTabClosing: _ =>
            {
                // Closing the last tab closes the window. Single-pane
                // legacy path keeps this 1:1; the multi-pane component
                // overrides this with state-driven removal logic.
                windowHolder[0]?.Close();
            },
            onTabDragCompleted: (_, _) =>
            {
                if (DockDragSession.Consumed)
                {
                    windowHolder[0]?.Close();
                    return;
                }
                DockDragSession.Current?.Cancel();
            });

        var info = new DockPaneInfo(pane.Key, pane.Title ?? string.Empty, pane);
        return chrome
            .Provide(DockContexts.Pane, (DockPaneInfo?)info)
            .Provide(DockContexts.PaneState, DockPaneState.Floating);
    }

    // Shared TabViewElement builder. Constructs the DockTabGroup-rendered
    // tab strip plus the TabStripFooter drag-region element that gets
    // registered with the window via `SetTitleBar` on Loaded. Caller
    // controls what happens on tab-close / tab-drag-complete so the
    // multi-pane Component can drive state mutations while the
    // single-pane legacy path closes the window directly.
    //
    // Spec 045 §2.4 cross-window dock-back. The floating window's
    // content is a `DockTabGroup`-rendered `TabView`. The tabs carry
    // CanDragTabs=true so the user can drag one OUT — when WinUI
    // signals wasOutside / tear-out, the caller's
    // `onTabDragCompleted` runs and decides whether to close the
    // window or restore the drag.
    //
    // Spec 045 §4.2 / §4.3 — Edge / Files / VS Code "tabs in the
    // title bar" pattern. The TabView lives at the root of the
    // floating window. Combined with `WindowSpec.ExtendsContent
    // IntoTitleBar = true`, the tab strip occupies the title-bar
    // zone at y=0 (flush against caption buttons) and the tab
    // body fills the client area below.
    //
    // Why not the WinUI 3 `Microsoft.UI.Xaml.Controls.TitleBar`
    // control? Its `Content` slot is a small in-row chrome slot
    // (intended for things like Edge's address bar) and cannot
    // host a TabView with bodies — putting a TabView there
    // collapses the body to zero height. The Edge pattern instead
    // uses `Window.SetTitleBar(dragRegion)` directly against a
    // strip-footer element (between last tab and caption buttons)
    // so the OS knows where dragging is enabled while caption
    // buttons float at the right via `ExtendsContentIntoTitleBar`.
    internal static TabViewElement BuildChrome(
        IReadOnlyList<DockableContent> panes,
        ReactorWindow?[] windowHolder,
        DockManager? manager,
        Action<DockableContent> onTabClosing,
        Action<DockableContent, bool> onTabDragCompleted)
    {
        var tabGroup = new DockTabGroup(
            panes is DockableContent[] arr ? arr : panes.ToArray(),
            // §4.6 + §7.1.4 — the tab strip background uses
            // `TitleBarBackgroundFillBrush` so it visually merges into
            // the OS title-bar zone (single continuous chrome row).
            TabChrome: TabChrome.TitleBar);
        var rendered = (TabViewElement)DockTabGroupRenderer.Render(
            tabGroup,
            renderLeafContent: doc => doc.Content ?? (Element)new BorderElement(null),
            onSelectedIndexChanged: null,
            onTabClosing: onTabClosing,
            onTabDragStarting: (doc, idx) =>
            {
                // Begin a cross-window session. The `manager` reference
                // is the host that originally owned this pane —
                // SourceManager is non-null per the session contract.
                if (DockDragSession.Current is { IsActive: true }) return;
                if (manager is null) return;
                DockDragSession.Begin(doc, manager, idx);
            },
            onTabDragCompleted: (pane, _, wasOutside) => onTabDragCompleted(pane, wasOutside));

        // Spec 045 §4.2 / §4.4 — drag-region element in TabStripFooter.
        // The footer lays out in TabView's template column with
        // `Width="*"`, so a `HAlign(Stretch)` Border with a transparent
        // background fills the entire empty area between the last tab
        // and the OS caption-button cluster. On Loaded we hand this
        // element to `Window.SetTitleBar(...)` so the OS treats it as
        // the window-drag surface and reserves caption-button inset on
        // its right (the new WinUI 3 caption inset handling).
        //
        // The Background brush must be set (even Transparent) for the
        // OS to recognize the element as occupying space — a Border
        // with `Background = null` is treated as zero-area for drag
        // hit-testing and dragging the empty space won't move the
        // window. We assign the brush inside OnMount rather than via
        // `.Background("Transparent")` because that helper calls
        // `BrushHelper.Parse` eagerly at element-tree-build time,
        // constructing a WinRT `SolidColorBrush` — which throws a
        // bare COMException in headless unit tests where no WinUI
        // runtime is loaded. OnMount only fires at reconcile time
        // under a real WinUI host, so brush creation is deferred to
        // a safe context.
        //
        // `SetTitleBar` is deferred to FrameworkElement.Loaded because
        // OnMount fires during reconcile, BEFORE `_window.Content` is
        // assigned at the end of the reconcile pass — so at OnMount
        // time the Border lives in a detached subtree and SetTitleBar
        // silently no-ops.
        var dragRegion = new BorderElement(null)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch)
            .MinWidth(180)
            .OnMount(fe =>
            {
                if (fe is global::Microsoft.UI.Xaml.Controls.Border border)
                {
                    border.Background = BrushHelper.Parse("Transparent");
                }
                void Apply()
                {
                    var win = windowHolder[0]?.NativeWindow;
                    win?.SetTitleBar(fe);
                }
                if (fe.IsLoaded)
                {
                    Apply();
                }
                else
                {
                    global::Microsoft.UI.Xaml.RoutedEventHandler? handler = null;
                    handler = (_, _) =>
                    {
                        // One-shot: SetTitleBar only needs to be wired
                        // once, and WinUI Loaded can re-fire on
                        // reparent/reload. Unsubscribe ourselves so we
                        // don't leak the delegate or re-call SetTitleBar.
                        if (handler is not null) fe.Loaded -= handler;
                        Apply();
                    };
                    fe.Loaded += handler;
                }
            });

        return rendered with { TabStripFooter = dragRegion };
    }
}

/// <summary>
/// Props for <see cref="DockFloatingWindowComponent"/> — the per-window
/// boot tuple. Equality drives <see cref="Component{TProps}.ShouldUpdate"/>;
/// these references are stable for the lifetime of a floating window so
/// the component never re-mounts.
/// </summary>
internal sealed record DockFloatingWindowProps(
    DockableContent InitialPane,
    ReactorWindow?[] WindowHolder,
    DockManager? Manager);

/// <summary>
/// Stateful component for floating windows. Owns the per-window tab list
/// (so cross-window dock-in can add tabs without re-mounting), subscribes
/// to <see cref="DockDragSession.SessionChanged"/> to surface the
/// CenterOnly drop overlay (spec 045 §4.2 / §4.3) when a foreign drag is
/// in flight, and removes consumed tabs from local state on tab-drag-out.
/// </summary>
internal sealed class DockFloatingWindowComponent : Component<DockFloatingWindowProps>
{
    public override Element Render()
    {
        var (panes, updatePanes) = UseReducer<IReadOnlyList<DockableContent>>(new[] { Props.InitialPane });
        var (_, bumpTick) = UseReducer(0);

        // §2.4 cross-window — re-render whenever any drag session in
        // the process starts / ends. The session is global so we can't
        // depend on prop-equality; UseEffect attaches once and the
        // tick reducer triggers re-renders without inflating state.
        UseEffect(() =>
        {
            Action onChanged = () => bumpTick(t => t + 1);
            DockDragSession.SessionChanged += onChanged;
            return () => DockDragSession.SessionChanged -= onChanged;
        });

        var holder = Props.WindowHolder;
        var manager = Props.Manager;

        // Shared append-as-tab callback. Used by both the UseEffect
        // router registration AND the floating→floating drag-completed
        // gap reAppend below — keeping a single implementation
        // guarantees that any side-effect (notably the AppWindow title
        // update on first-tab append) stays consistent across both
        // registration paths.
        Action<DockableContent> append = src =>
        {
            updatePanes(current =>
            {
                for (int i = 0; i < current.Count; i++)
                    if (ReferenceEquals(current[i], src)) return current;
                var list = new List<DockableContent>(current.Count + 1);
                list.AddRange(current);
                list.Add(src);
                return list;
            });
            try
            {
                var win = holder[0];
                var native = win?.NativeWindow;
                if (native is not null) native.Title = src.Title ?? string.Empty;
            }
            catch { /* window may already be closing */ }
        };

        // Register an append-as-tab callback so the source host's
        // `HandleTabDragCompleted` can route a cross-window dock-in
        // (cursor over this floating window at drop time) to us
        // without needing a direct reference to this component
        // instance. See `DockFloatingPaneRouter` for the rationale
        // around drop-time hit-testing vs. WinUI drag events.
        //
        // The registration is deferred to the next dispatcher tick
        // because this UseEffect mount fires synchronously during
        // `ReactorApp.OpenWindow` → `MountAndActivate` — BEFORE
        // `DockFloatingWindow.Open` writes `windowHolder[0] = window`
        // after OpenWindow returns. Reading `holder[0]` directly here
        // would always see null and skip the registration, leaving
        // the floating window invisible to the cross-window router.
        UseEffect(() =>
        {
            bool alive = true;
            ReactorWindow? registered = null;
            var dispatcher = global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            void TryRegister()
            {
                if (!alive) return;
                var win = holder[0];
                if (win is null)
                {
                    // Holder still not populated — try again on the
                    // next dispatcher tick. OpenWindow's caller writes
                    // holder[0] immediately after the call returns;
                    // a single re-enqueue handles the common case.
                    dispatcher?.TryEnqueue(TryRegister);
                    return;
                }
                DockFloatingPaneRouter.Register(win, append);
                registered = win;
            }
            if (dispatcher is not null) dispatcher.TryEnqueue(TryRegister);
            else TryRegister();
            return () =>
            {
                alive = false;
                if (registered is not null) DockFloatingPaneRouter.Unregister(registered);
            };
        });

        var currentPanes = panes;

        void RemoveLocal(DockableContent pane)
        {
            // Filter by reference (Key equality is unreliable — apps
            // can ship multiple panes with the same Key). When the
            // pane list empties, close the window so the user isn't
            // left with an empty floating shell. The window-close
            // side-effect lives outside the reducer so it only fires
            // once even if React-strict-mode double-invokes; reading
            // current.Count==1 after the filter is the trigger.
            bool willEmpty = false;
            string? newTitle = null;
            updatePanes(current =>
            {
                var keep = new List<DockableContent>(current.Count);
                for (int i = 0; i < current.Count; i++)
                {
                    if (!ReferenceEquals(current[i], pane))
                        keep.Add(current[i]);
                }
                if (keep.Count == current.Count) return current; // no change
                if (keep.Count == 0) { willEmpty = true; return current; /* don't commit empty list */ }
                newTitle = keep[0].Title;
                return keep;
            });
            if (willEmpty)
            {
                holder[0]?.Close();
                return;
            }
            if (newTitle is not null)
            {
                try
                {
                    var native = holder[0]?.NativeWindow;
                    if (native is not null) native.Title = newTitle;
                }
                catch { /* window may already be closing */ }
            }
        }

        var chrome = DockFloatingWindow.BuildChrome(
            currentPanes,
            holder,
            manager,
            onTabClosing: RemoveLocal,
            onTabDragCompleted: (pane, wasOutside) =>
            {
                if (DockDragSession.Consumed)
                {
                    // The pane was docked into another surface (main
                    // host's per-group overlay, another floating
                    // window's CenterOnly overlay, etc.). Remove it
                    // from us; if it was the only tab, the window
                    // closes itself.
                    RemoveLocal(pane);
                    return;
                }
                // §4.2 cross-window dock-in (Center only): if the
                // user dragged a tab OUT of this floating window and
                // released the cursor over ANOTHER registered
                // floating window, route the pane to that window
                // instead of letting WinUI tear it into a new
                // floating window.
                if (wasOutside && DockFloatingPaneRouter.HasRegisteredWindows)
                {
                    // Don't append to ourselves.
                    var ownWindow = holder[0];
                    if (ownWindow is not null) DockFloatingPaneRouter.Unregister(ownWindow);
                    try
                    {
                        if (DockFloatingPaneRouter.TryAppendUnderCursor(pane))
                        {
                            RemoveLocal(pane);
                            DockDragSession.MarkConsumed();
                            DockDragSession.Current?.End();
                            return;
                        }
                    }
                    finally
                    {
                        // Re-register ourselves so subsequent drags
                        // can target us again. Reuse the shared
                        // `append` closure declared above so a pane
                        // appended via this gap-bridge still updates
                        // the AppWindow title — the UseEffect
                        // re-registration on the next render would
                        // restore that behavior eventually, but the
                        // window may be a drop target during the gap.
                        if (ownWindow is not null)
                        {
                            DockFloatingPaneRouter.Register(ownWindow, append);
                        }
                    }
                }
                // Drop landed nowhere (Esc, drop on desktop). End any
                // in-flight session so the pane stays put in this
                // floating window — the TabView re-renders with the
                // pane still in its `documents` list.
                DockDragSession.Current?.Cancel();
            });

        // Provide the FIRST pane's DockPaneInfo as the floating window's
        // pane context. Per-tab context wiring is a future refinement;
        // hook resolution inside an active pane's body works because
        // the active tab's WinUI subtree inherits this context.
        var primary = currentPanes[0];
        var info = new DockPaneInfo(primary.Key, primary.Title ?? string.Empty, primary);

        return chrome
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
