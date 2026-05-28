using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.6 — VS-style immediate tab tear-off.
//
//  WinUI's TabView CanDragTabs is disabled on host and floating tab
//  views so its OLE drag pipeline never competes with this one.
//
//  Flow:
//    1. PointerPressed on a tab header — record candidate (tab item, pane,
//       start cursor pos). PressCore.
//    2. PointerMoved past the system drag threshold (4 DIP) — invoke the
//       host-supplied tear-off callback (RemovePane + Open floating
//       window at 0.5 opacity + NoActivate + IgnorePointerInput), then
//       start a cursor-poll tracker. MoveCore.
//    3. DockTabTearOffTracker.OnTick reads GetCursorPos + GetAsyncKeyState
//       each 16 ms — positions the floating window, ends the drag on
//       LBUTTON-up or Esc.
//    4. End — find the drop-target overlay whose hover state was driven
//       by the cursor (the floating window's WS_EX_TRANSPARENT lets
//       pointer events fall through to the overlay's PointerEntered
//       handlers naturally). Fire the overlay's confirm event or strip
//       the drag styles and keep the floating window.
//
//  Floating windows route through the same press hook via
//  DockFloatingWindow.BeginFloatingTearOff (the symmetric entry point),
//  so dragging a tab out of a floating window uses the cursor-poll
//  pipeline rather than WinUI's OLE drag.
//
//  Testability: every state transition runs through *Core methods that
//  the real PointerPressed/Moved handlers wrap. Tests call
//  SimulatePressForTest / SimulateMoveForTest / DockTabTearOffTracker.
//  SimulateReleaseForTest to drive the pipeline without real pointer
//  events. The diagnostic sink mirrors the splitter's TraceSink so
//  fixtures (and the operation log) can record every transition.
// ════════════════════════════════════════════════════════════════════════

internal static class DockTabTearOff
{
    /// <summary>
    /// Pixel distance the cursor must travel past press-down before the
    /// candidate commits to a tear-off. Matches WinUI's default tap-vs-drag
    /// threshold (System.Drag*Threshold). Tests can shrink this via
    /// <see cref="ThresholdDipForTest"/>.
    /// </summary>
    internal const double DefaultTearOffThresholdDip = 4.0;

    /// <summary>Test-only override of <see cref="DefaultTearOffThresholdDip"/>.
    /// Null in production. Tests set it so a 1-DIP simulated move trips
    /// the threshold without the harness having to math out 5+ DIP coords.</summary>
    internal static double? ThresholdDipForTest;

    /// <summary>Optional sink for state-transition traces. Wired by
    /// <see cref="DockHostNativeComponent"/> when an operation log is
    /// attached; null in standalone test rigs. Matches splitter
    /// diagnostic sink shape.</summary>
    internal static Action<string>? DiagnosticSink;

    private static void Trace(string msg) => DiagnosticSink?.Invoke(msg);

    /// <summary>Information passed to the host-side <c>beginTearOff</c>
    /// callback once the threshold is crossed. All position fields are
    /// in <b>physical screen pixels</b>, not DIPs — the tracker drives
    /// window position via <c>AppWindow.Move(PointInt32)</c> directly,
    /// bypassing the DIP roundtrip that bit us on mixed-DPI / no-DPI-yet
    /// scenarios (a freshly-opened NoActivate window doesn't get its
    /// WM_DPICHANGED until it shows, so its <c>_dpi</c> stays 96 and
    /// <c>SetPosition</c>'s internal <c>DipToPhysicalPoint</c> converts
    /// with the wrong scale).</summary>
    internal sealed class TearOffRequest
    {
        public required DockableContent Pane { get; init; }
        public required int TabIndex { get; init; }
        /// <summary>Cursor position in absolute screen pixels at the
        /// moment threshold was crossed (from <c>GetCursorPos</c>).</summary>
        public required (int X, int Y) CursorScreenPhys { get; init; }
        /// <summary>Cursor position within the source TabView's local
        /// coordinate space at PRESS time, converted to physical
        /// pixels using the source XamlRoot's RasterizationScale.
        /// This is the constant offset between cursor and the dragged
        /// tab — held by the tracker so each tick's window position is
        /// <c>cursorScreen_phys - pressOffset_phys</c>.</summary>
        public required (int X, int Y) PressOffsetPhys { get; init; }
        /// <summary>Original press point in source-local DIPs (for
        /// diagnostics; tracker math uses the physical fields).</summary>
        public required (double X, double Y) PressLocalDip { get; init; }
        public required double SourceScale { get; init; }
        public required XamlRoot XamlRoot { get; init; }
    }

    /// <summary>Result the host returns after committing the tear-off.
    /// Null = host refused (e.g. CanFloat=false). The tracker consumes
    /// this; tests can construct it manually for finalize-path coverage.</summary>
    internal sealed class TearOffActive
    {
        public required ReactorWindow FloatingWindow { get; init; }
        public required DockableContent Pane { get; init; }
        public required Action ConfirmDropAtCursor { get; init; }
        public required Action CancelDrop { get; init; }
        public required XamlRoot SourceXamlRoot { get; init; }
        /// <summary>The cursor → window-top-left offset to maintain
        /// throughout the drag, in <b>physical pixels</b>.</summary>
        public required (int X, int Y) OffsetPhys { get; init; }
    }

    // ─── Per-TabView hook registration ────────────────────────────────────

    private static readonly ConditionalWeakTable<TabView, Hook> s_hooks = new();

    /// <summary>Attach the pointer-press handler to a TabView. Idempotent
    /// (ConditionalWeakTable-keyed); successive calls refresh the closures
    /// so re-renders pick up new pane-list snapshots.</summary>
    public static void AttachPressHook(
        TabView tabView,
        Func<TabViewItem, (DockableContent? Pane, int Index)> resolveTab,
        Func<TearOffRequest, TearOffActive?> beginTearOff)
    {
        ArgumentNullException.ThrowIfNull(tabView);
        ArgumentNullException.ThrowIfNull(resolveTab);
        ArgumentNullException.ThrowIfNull(beginTearOff);
        if (s_hooks.TryGetValue(tabView, out var existing))
        {
            existing.ResolveTab = resolveTab;
            existing.BeginTearOff = beginTearOff;
            return;
        }
        var hook = new Hook(tabView) { ResolveTab = resolveTab, BeginTearOff = beginTearOff };
        s_hooks.Add(tabView, hook);
        tabView.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(hook.OnPressed), handledEventsToo: true);
        tabView.AddHandler(UIElement.PointerMovedEvent,
            new PointerEventHandler(hook.OnMoved), handledEventsToo: true);
        tabView.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler(hook.OnReleased), handledEventsToo: true);
        tabView.AddHandler(UIElement.PointerCanceledEvent,
            new PointerEventHandler(hook.OnReleased), handledEventsToo: true);
        tabView.AddHandler(UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(hook.OnCaptureLost), handledEventsToo: true);
    }

    // ─── Per-TabView state machine ─────────────────────────────────────────

    internal sealed class Hook
    {
        public Hook(TabView tabView) { TabView = tabView; }

        public TabView TabView { get; }
        public Func<TabViewItem, (DockableContent? Pane, int Index)> ResolveTab { get; set; } = null!;
        public Func<TearOffRequest, TearOffActive?> BeginTearOff { get; set; } = null!;

        // Press → candidate. PointerMoved compares against StartCursorDip;
        // once distance > threshold (default 4 DIP) we trip the tear-off.
        internal DockableContent? CandidatePane;
        internal int CandidateIndex = -1;
        internal (double X, double Y)? CandidateStart;
        internal TabViewItem? CandidateItem;

        public void OnPressed(object sender, PointerRoutedEventArgs e)
        {
            var pt = e.GetCurrentPoint(TabView);
            if (!pt.Properties.IsLeftButtonPressed) return;
            var src = e.OriginalSource as DependencyObject;
            // Ignore presses on the close button — its press shouldn't
            // start a drag (this also avoids fighting WinUI's close button).
            if (src is Microsoft.UI.Xaml.Controls.Button btn
                && string.Equals(btn.Name, "CloseButton", StringComparison.Ordinal))
                return;
            var item = FindAncestor<TabViewItem>(src);
            if (item is null) return;
            var (pane, idx) = ResolveTab(item);
            if (pane is null || idx < 0) return;
            PressCore(item, pane, idx, pt.Position.X, pt.Position.Y);
            // Capture the pointer so subsequent PointerMoved events keep
            // firing on the TabView even after the cursor leaves the
            // tab-strip bounds. Without this, a fast drag (or a
            // WinAppDriver-synthesized drag that jumps the cursor past
            // the strip in one MoveByOffset) gets no MoveCore calls
            // because PointerMoved is routing to whatever element is
            // under the cursor. We release the capture at threshold-
            // crossing in MoveCore (so the cursor-poll tracker takes
            // over cleanly and the source XAML island stops absorbing
            // events that should reach the host's overlays).
            try { TabView.CapturePointer(e.Pointer); }
            catch { /* capture can fail if pointer state is stale — best-effort */ }
        }

        /// <summary>Real-press inner method; also called by test hook.</summary>
        internal void PressCore(TabViewItem item, DockableContent pane, int tabIndex, double localX, double localY)
        {
            CandidatePane = pane;
            CandidateIndex = tabIndex;
            CandidateItem = item;
            CandidateStart = (localX, localY);
            Trace($"Press pane='{pane.Key}' idx={tabIndex} at=({localX:F1},{localY:F1})");
        }

        public void OnMoved(object sender, PointerRoutedEventArgs e)
        {
            if (CandidatePane is null || CandidateStart is null) return;
            var pt = e.GetCurrentPoint(TabView);
            if (!pt.Properties.IsLeftButtonPressed)
            {
                Trace("Move/button-not-down → clear candidate");
                ClearCandidate();
                return;
            }
            MoveCore(pt.Position.X, pt.Position.Y);
        }

        /// <summary>Real-move inner method; also called by test hook.</summary>
        internal void MoveCore(double curX, double curY)
        {
            if (CandidatePane is null || CandidateStart is null) return;
            var s = CandidateStart.Value;
            var dx = curX - s.X;
            var dy = curY - s.Y;
            var threshold = ThresholdDipForTest ?? DefaultTearOffThresholdDip;
            if (dx * dx + dy * dy < threshold * threshold)
            {
                Trace($"Move below-threshold dx={dx:F1} dy={dy:F1}");
                return;
            }
            // Threshold crossed — commit tear-off. Capture all candidate
            // state first, then clear before invoking BeginTearOff (which
            // may synchronously mount a floating window that re-renders
            // us; we don't want the candidate to leak across that).
            var xamlRoot = TabView.XamlRoot;
            if (xamlRoot is null)
            {
                Trace("Move threshold-crossed but XamlRoot=null → abort");
                ClearCandidate();
                return;
            }
            var pane = CandidatePane;
            var idx = CandidateIndex;
            var pressLocalDip = CandidateStart.Value;
            ClearCandidate();
            // Spec 045 §2.6 — release any pointer capture WinUI's TabView
            // template grabbed on press. Without this, pointer events
            // stay captured inside the source XAML island for the entire
            // gesture — even though the floating preview is
            // WS_EX_TRANSPARENT, capture overrides the OS-level fall-through
            // routing, so the source host's overlays never receive
            // PointerEntered. Symptoms when omitted: docked-tab drag works
            // (source XAML island IS the host, capture stays-in-source
            // still lights the overlays), floating-tab drag doesn't (the
            // floating window's XAML island isn't the host).
            try { TabView.ReleasePointerCaptures(); }
            catch { /* no captures held → no-op */ }
            // Convert press-local DIPs → physical using the SOURCE scale
            // (this is the only scale we trust — the new floating preview
            // window may not have its WM_DPICHANGED settled yet, so its
            // own _dpi could still be 96). The tracker uses absolute
            // screen-physical cursor positions and these physical offsets
            // — no further DIP conversion happens after this point.
            var sourceScale = xamlRoot.RasterizationScale;
            if (sourceScale <= 0) sourceScale = 1.0;
            var pressOffsetPhys = (
                (int)Math.Round(pressLocalDip.X * sourceScale),
                (int)Math.Round(pressLocalDip.Y * sourceScale));
            var cursorPhys = TryGetCursorPhys();
            Trace($"Move threshold-crossed pane='{pane.Key}' → BeginTearOff " +
                  $"pressLocal=({pressLocalDip.X:F1},{pressLocalDip.Y:F1}) " +
                  $"scale={sourceScale:F2} offsetPhys=({pressOffsetPhys.Item1},{pressOffsetPhys.Item2}) " +
                  $"cursorPhys=({cursorPhys.X},{cursorPhys.Y})");
            var req = new TearOffRequest
            {
                Pane = pane,
                TabIndex = idx,
                CursorScreenPhys = cursorPhys,
                PressOffsetPhys = pressOffsetPhys,
                PressLocalDip = pressLocalDip,
                SourceScale = sourceScale,
                XamlRoot = xamlRoot,
            };
            var active = BeginTearOff(req);
            if (active is null)
            {
                Trace("BeginTearOff refused (CanMove/CanFloat=false or session active)");
                return;
            }
            DockTabTearOffTracker.Start(active);
        }

        public void OnReleased(object sender, PointerRoutedEventArgs e)
        {
            // A surviving candidate at release time means the press never
            // crossed the tear-off threshold — this was a plain click, not a
            // drag (MoveCore clears the candidate the instant it tears off).
            // The pointer capture we grabbed in OnPressed (to keep tracking a
            // possible drag) routes the release to the TabView instead of the
            // pressed TabViewItem, so WinUI never commits the tab selection.
            // Commit it ourselves so click-to-switch works in multi-tab
            // groups. Latent until now because single-tab groups never
            // exercised tab selection. Setting SelectedIndex to the value it
            // already holds is a no-op, so this is safe even when WinUI did
            // manage to select.
            if (CandidatePane is not null && CandidateItem is not null)
            {
                var sel = TabView.TabItems.IndexOf(CandidateItem);
                Trace($"Release w/ candidate idx={CandidateIndex} → click-select {sel}");
                if (sel >= 0 && TabView.SelectedIndex != sel) TabView.SelectedIndex = sel;
            }
            ClearCandidate();
        }

        public void OnCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (CandidatePane is not null) Trace("CaptureLost w/ candidate → clear");
            ClearCandidate();
        }

        internal void ClearCandidate()
        {
            CandidatePane = null;
            CandidateIndex = -1;
            CandidateStart = null;
            CandidateItem = null;
        }

        private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d is not null)
            {
                if (d is T t) return t;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }
    }

    // ─── Test-only API (InternalsVisibleTo Reactor.Tests / Reactor.AppTests.Host) ─

    /// <summary>Clears every TabView's candidate state. Call at fixture
    /// start/end so a leaked candidate from a prior test can't poison the
    /// next one.</summary>
    internal static void ResetAllCandidatesForTest()
    {
        // ConditionalWeakTable doesn't expose enumeration in older
        // frameworks, but .NET 8+ does via the IEnumerable<KVP>
        // implementation. Use that to reach every live hook.
        foreach (var kvp in s_hooks) kvp.Value.ClearCandidate();
        ThresholdDipForTest = null;
    }

    /// <summary>Synthesize a press on the given tab item. Bypasses
    /// WinUI's PointerPressed routing — useful in the selftest harness
    /// where real pointer events aren't delivered.</summary>
    internal static void SimulatePressForTest(TabView tabView, TabViewItem item, DockableContent pane, int tabIndex,
        double localX = 0, double localY = 0)
    {
        if (!s_hooks.TryGetValue(tabView, out var hook))
            throw new InvalidOperationException("AttachPressHook has not been called on this TabView.");
        hook.PressCore(item, pane, tabIndex, localX, localY);
    }

    /// <summary>Synthesize a pointer-move while pressed. If the cumulative
    /// distance from the press exceeds the threshold, BeginTearOff fires
    /// — and the tracker becomes active.</summary>
    internal static void SimulateMoveForTest(TabView tabView, double curX, double curY)
    {
        if (!s_hooks.TryGetValue(tabView, out var hook))
            throw new InvalidOperationException("AttachPressHook has not been called on this TabView.");
        hook.MoveCore(curX, curY);
    }

    /// <summary>Inspect the press candidate for this TabView (test only).
    /// Null = no candidate / threshold already crossed / cleared.</summary>
    internal static (DockableContent? Pane, int Index, (double X, double Y)? Start)
        InspectCandidateForTest(TabView tabView)
    {
        if (!s_hooks.TryGetValue(tabView, out var hook)) return (null, -1, null);
        return (hook.CandidatePane, hook.CandidateIndex, hook.CandidateStart);
    }

    /// <summary>True if AttachPressHook has registered this TabView.
    /// Useful in fixtures that need to assert wire-up before driving
    /// synthetic events.</summary>
    internal static bool IsHookAttachedForTest(TabView tabView)
        => s_hooks.TryGetValue(tabView, out _);

    // ─── Cursor sampling helpers (Win32) ──────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    internal const int VK_LBUTTON = 0x01;
    internal const int VK_ESCAPE = 0x1B;

    internal static (double X, double Y) TryGetCursorScreenDip(XamlRoot xamlRoot)
    {
        if (!GetCursorPos(out var p)) return (0, 0);
        var scale = xamlRoot.RasterizationScale;
        if (scale <= 0) scale = 1.0;
        return (p.X / scale, p.Y / scale);
    }

    /// <summary>Raw cursor position in absolute screen pixels. The
    /// tracker uses this exclusively — no DIP conversion in the
    /// hot path means no chance of a stale-DPI bug.</summary>
    internal static (int X, int Y) TryGetCursorPhys()
    {
        if (!GetCursorPos(out var p)) return (0, 0);
        return (p.X, p.Y);
    }

    // ─── Drop confirmation (shared between host + floating tear-offs) ─────
    //
    // The pipeline relies on the overlay's PointerEntered/Exited handlers
    // driving _hoveredTarget naturally as the cursor moves over the buttons
    // (the floating preview window is WS_EX_TRANSPARENT so pointer events
    // fall through). At finalize time we just ask each visible overlay
    // for its latched hover state and trigger the confirm on the first
    // hit. The overlay's TargetConfirmed event runs the host's OnConfirm
    // closure (already wired to perform the layout mutation).

    /// <summary>Walk every visible <see cref="DockDropTargetOverlayControl"/>
    /// reachable from the supplied <paramref name="manager"/>'s host
    /// element. The first one with a latched hover fires its confirm and
    /// returns the target. Null = cursor was over empty space (drop-outside).
    /// </summary>
    internal static DockTarget? TryConfirmHoveredTargetFor(DockManager? manager)
    {
        if (manager is null) return null;
        var hostEl = DockHostLiveAnnouncer.GetHost(manager);
        if (hostEl is null) return null;
        foreach (var overlay in EnumerateOverlays(hostEl))
        {
            if (overlay.Visibility != Visibility.Visible) continue;
            if (overlay.CurrentHoveredTarget is null) continue;
            var target = overlay.TryConfirmCurrentHover();
            if (target is not null) return target;
        }
        return null;
    }

    /// <summary>Walk every <see cref="DockDropTargetOverlayControl"/>
    /// reachable from <paramref name="root"/> via the visual tree.</summary>
    internal static IEnumerable<DockDropTargetOverlayControl> EnumerateOverlays(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (cur is DockDropTargetOverlayControl c) yield return c;
            var n = VisualTreeHelper.GetChildrenCount(cur);
            for (int i = 0; i < n; i++) stack.Push(VisualTreeHelper.GetChild(cur, i));
        }
    }
}

/// <summary>Per-process tracker for the currently-in-flight tab tear-off
/// drag. Owns the cursor-poll timer + mouse-up watch. One drag at a time.</summary>
internal static class DockTabTearOffTracker
{
    private static Microsoft.UI.Xaml.DispatcherTimer? s_timer;
    private static DockTabTearOff.TearOffActive? s_active;
    private static bool s_autoStartTimer = true;

    public static void Start(DockTabTearOff.TearOffActive active)
    {
        Stop();
        s_active = active;
        s_tickCount = 0;
        s_lastLoggedCursor = (0, 0);
        PositionFloating();
        DockTabTearOff.DiagnosticSink?.Invoke($"Tracker.Start pane='{active.Pane.Key}' offsetPhys=({active.OffsetPhys.X},{active.OffsetPhys.Y})");
        if (!s_autoStartTimer) return;
        s_timer = new Microsoft.UI.Xaml.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        s_timer.Tick += OnTick;
        s_timer.Start();
    }

    public static void Stop()
    {
        if (s_timer is not null)
        {
            s_timer.Stop();
            s_timer.Tick -= OnTick;
            s_timer = null;
        }
        if (s_active is not null)
            DockTabTearOff.DiagnosticSink?.Invoke("Tracker.Stop");
        s_active = null;
    }

    /// <summary>Stop the tracker AND run the active record's CancelDrop
    /// callback so the floating window's drag styles get stripped (it
    /// becomes a normal interactive floating window). Used by the host's
    /// defensive cleanup in BeginImmediateTearOff: if a previous drag
    /// left a stale tracker, we don't want its floating window stuck in
    /// click-through / no-activate mode. No-op when no drag is in flight.</summary>
    public static void ForceCancel()
    {
        var active = s_active;
        if (active is null) { Stop(); return; }
        DockTabTearOff.DiagnosticSink?.Invoke("Tracker.ForceCancel");
        Stop();
        try { active.CancelDrop(); }
        catch { /* CancelDrop must not crash the next drag */ }
    }

    private static int s_tickCount;
    private static (int X, int Y) s_lastLoggedCursor;

    private static void OnTick(object? sender, object e)
    {
        var active = s_active;
        if (active is null) { Stop(); return; }
        PositionFloating();
        var lbut = DockTabTearOff.GetAsyncKeyState(DockTabTearOff.VK_LBUTTON);
        bool down = (lbut & 0x8000) != 0;
        if (!down)
        {
            FinalizeInternal(commit: true);
            return;
        }
        var esc = DockTabTearOff.GetAsyncKeyState(DockTabTearOff.VK_ESCAPE);
        if ((esc & 0x8000) != 0)
        {
            FinalizeInternal(commit: false);
            return;
        }
        // Sample cursor position once every ~16 ticks (≈250 ms) so the
        // op log captures the drag path without flooding. Logs at the
        // start of every drag and every quarter-second after.
        s_tickCount++;
        if (s_tickCount == 1 || s_tickCount % 16 == 0)
        {
            var cur = DockTabTearOff.TryGetCursorPhys();
            if (cur.X != s_lastLoggedCursor.X || cur.Y != s_lastLoggedCursor.Y)
            {
                DockTabTearOff.DiagnosticSink?.Invoke(
                    $"Tracker.Tick #{s_tickCount} cursorPhys=({cur.X},{cur.Y})");
                s_lastLoggedCursor = cur;
            }
        }
    }

    private static void PositionFloating()
    {
        var active = s_active;
        if (active is null) return;
        var cursorPhys = DockTabTearOff.TryGetCursorPhys();
        var x = cursorPhys.X - active.OffsetPhys.X;
        var y = cursorPhys.Y - active.OffsetPhys.Y;
        // Bypass ReactorWindow.SetPosition's DIP→physical roundtrip
        // (which uses the window's _dpi — unreliable for a fresh
        // NoActivate window). Drive AppWindow.Move with absolute
        // screen-physical coords directly. Same coordinate system
        // as GetCursorPos returns, so the cursor and the window
        // top-left stay locked together exactly.
        //
        // Ordering guarantee: every path that closes the floating window
        // (FinalizeInternal, ForceCancel, host-unmount UseEffect cleanup)
        // calls Stop() FIRST, which detaches the Tick handler and clears
        // s_active. PositionFloating is only invoked from OnTick (timer
        // disabled after Stop) and synchronously from Start (window is
        // fresh). No path leaves a live tracker pointing at a closed
        // window, so AppWindow.Move always targets a live AppWindow.
        active.FloatingWindow.AppWindow.Move(
            new global::Windows.Graphics.PointInt32(x, y));
    }

    private static void FinalizeInternal(bool commit)
    {
        var active = s_active;
        if (active is null) { Stop(); return; }
        DockTabTearOff.DiagnosticSink?.Invoke($"Tracker.Finalize commit={commit}");
        Stop();
        if (commit) active.ConfirmDropAtCursor();
        else active.CancelDrop();
    }

    /// <summary>True when a tear-off drag is in flight. Also used by
    /// the host renderer to detect stale tracker state (defensive
    /// force-stop in BeginImmediateTearOff).</summary>
    internal static bool IsActive => s_active is not null;

    // ─── Test-only API ──────────────────────────────────────────────────

    /// <summary>Alias for <see cref="IsActive"/> kept for symmetry with
    /// other *ForTest properties.</summary>
    internal static bool IsActiveForTest => IsActive;

    /// <summary>The currently-active tear-off, or null. Tests assert
    /// against this between simulated steps.</summary>
    internal static DockTabTearOff.TearOffActive? ActiveForTest => s_active;

    /// <summary>Set to false BEFORE calling Start to prevent the cursor
    /// poll timer from running. Tests then call
    /// <see cref="SimulateReleaseForTest"/> directly.</summary>
    internal static bool AutoStartTimerForTest
    {
        get => s_autoStartTimer;
        set => s_autoStartTimer = value;
    }

    /// <summary>Synthesize a release. <paramref name="commit"/>=true mimics
    /// LBUTTON-up (run ConfirmDropAtCursor); false mimics Esc.</summary>
    internal static void SimulateReleaseForTest(bool commit)
    {
        FinalizeInternal(commit);
    }

    /// <summary>Force-clear tracker state without finalizing. Used at
    /// fixture teardown to recover from a stuck state.</summary>
    internal static void ResetForTest()
    {
        if (s_timer is not null)
        {
            s_timer.Stop();
            s_timer.Tick -= OnTick;
            s_timer = null;
        }
        s_active = null;
        s_autoStartTimer = true;
    }
}
