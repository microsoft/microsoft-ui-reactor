using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VTH = Microsoft.UI.Xaml.Media.VisualTreeHelper;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.1 — Reactor-native splitter control.
//
//  Translates WinUI.Dock's reliance on CommunityToolkit GridSplitter to a
//  Reactor primitive. The control:
//    • renders an 8 DIP visual handle centered inside a 16 DIP hit-test
//      area (spec §8.7 WCAG 2.5.5 touch targets);
//    • emits ResizeDelta DIPs as the user drags or presses arrow keys;
//    • is focusable; arrow keys resize by KeyboardStep DIPs (default 16);
//    • respects reduced-motion implicitly (no animation; the handle just
//      moves with the pointer).
//
//  The control is layout-engine-agnostic on purpose — the consumer (a
//  Reactor element that owns the surrounding panes) interprets the delta
//  as a ratio adjustment between two flex children.
// ════════════════════════════════════════════════════════════════════════

/// <summary>Direction the splitter resizes children along.</summary>
internal enum DockSplitterDirection
{
    /// <summary>Vertical handle that resizes columns side-by-side.</summary>
    Columns,

    /// <summary>Horizontal handle that resizes stacked rows.</summary>
    Rows,
}

/// <summary>Pointer/keyboard delta event raised by <see cref="DockSplitterControl"/>.</summary>
internal sealed class DockSplitterDeltaEventArgs : EventArgs
{
    public DockSplitterDeltaEventArgs(
        double delta,
        DockSplitterDirection direction,
        double hostExtentDip,
        bool isFinal)
    {
        Delta = delta;
        Direction = direction;
        HostExtentDip = hostExtentDip;
        IsFinal = isFinal;
    }

    /// <summary>Movement in DIPs along the split axis (positive grows the trailing child).</summary>
    public double Delta { get; }

    public DockSplitterDirection Direction { get; }

    /// <summary>
    /// The host container's measured extent along the split axis at the moment
    /// of the event (DIPs). Equals the parent <c>FlexPanel.ActualWidth</c> for
    /// <see cref="DockSplitterDirection.Columns"/> or <c>ActualHeight</c> for
    /// <see cref="DockSplitterDirection.Rows"/>. Consumers pass this as the
    /// <c>totalDip</c> to the ratio solver so the delta is interpreted in the
    /// same DIP space the layout was arranged in.
    /// </summary>
    public double HostExtentDip { get; }

    /// <summary>True for the terminal delta of a drag/key gesture (release, capture lost, key chord).</summary>
    public bool IsFinal { get; }
}

/// <summary>
/// Spec 045 §2.1 splitter — 8 DIP visual / 16 DIP hit, pointer + keyboard.
/// Backed by a <c>Grid</c> (no XAML template; visuals built in code).
/// </summary>
internal sealed partial class DockSplitterControl : Grid
{
    public const double VisualThicknessDip = 8.0;
    public const double HitThicknessDip = 16.0;
    public const double DefaultKeyboardStepDip = 16.0;

    private readonly Rectangle _handle;
    private DockSplitterDirection _direction = DockSplitterDirection.Columns;
    private bool _isCapturing;
    private Point _captureOrigin;
    private uint _capturePointerId;
    // Snapshot the pair's GROW values (not DIPs) at capture. ActualWidth /
    // ActualHeight can be transiently wrong at PointerPressed time — the
    // first press in a 3+ pane split has been observed reporting half-panel
    // widths (2-pane shape) before the layout settles into its real shape
    // on the next MOVE. Locking pair-DIP space at PRESS made the cursor
    // lag the splitter handle. Grow values are stable: the splitter only
    // mutates leading+trailing grow during a drag, so the captured pair
    // grow remains the right total to distribute on every MOVE.
    private double _leadingGrowAtCapture;
    private double _pairGrowAtCapture;

    public event EventHandler<DockSplitterDeltaEventArgs>? ResizeDelta;

    public DockSplitterControl()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Background = new SolidColorBrush(Colors.Transparent);

        // Spec 045 §2.22 high-contrast — handle Fill resolves to a
        // theme brush so HC keeps the splitter legible against the
        // system accent. Falls back to a ~50% gray ARGB literal when
        // the theme resources aren't available (e.g. headless tests
        // running without an Application instance).
        _handle = new Rectangle
        {
            Fill = ThemedBrush(
                "SystemControlForegroundBaseMediumLowBrush",
                Color.FromArgb(0x88, 0x80, 0x80, 0x80)),
            RadiusX = 1,
            RadiusY = 1,
        };
        Children.Add(_handle);

        ApplyDirection();

        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        // Subscribe with handledEventsToo so we receive arrow keys even
        // when WinUI's keyboard-nav engine has marked them Handled
        // (which moves focus away from us before regular KeyDown runs).
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown), handledEventsToo: true);

        AutomationProperties.SetName(this, "Resize");
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Control);
    }

    /// <summary>Direction the splitter resizes; controls cursor and arrow-key mapping.</summary>
    public DockSplitterDirection Direction
    {
        get => _direction;
        set
        {
            if (_direction == value) return;
            _direction = value;
            ApplyDirection();
        }
    }

    /// <summary>Per-keystroke resize amount in DIPs. Default 16.</summary>
    public double KeyboardStep { get; set; } = DefaultKeyboardStepDip;

    /// <summary>
    /// Optional diagnostic sink for the spec 045 operation log — fires
    /// one entry per pointer event (pressed / moved / released) with
    /// the snapshot values + clamp math so cursor-tracking regressions
    /// can be diagnosed without binary debugging.
    /// </summary>
    public Action<string>? DiagnosticSink { get; set; }

    private void Trace(string msg) => DiagnosticSink?.Invoke(msg);

    private void ApplyDirection()
    {
        switch (_direction)
        {
            case DockSplitterDirection.Columns:
                ClearValue(HeightProperty);
                Width = HitThicknessDip;
                MinWidth = HitThicknessDip;
                HorizontalAlignment = HorizontalAlignment.Stretch;
                VerticalAlignment = VerticalAlignment.Stretch;
                _handle.Width = VisualThicknessDip;
                _handle.ClearValue(HeightProperty);
                _handle.HorizontalAlignment = HorizontalAlignment.Center;
                _handle.VerticalAlignment = VerticalAlignment.Stretch;
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
                break;
            case DockSplitterDirection.Rows:
                ClearValue(WidthProperty);
                Height = HitThicknessDip;
                MinHeight = HitThicknessDip;
                HorizontalAlignment = HorizontalAlignment.Stretch;
                VerticalAlignment = VerticalAlignment.Stretch;
                _handle.ClearValue(WidthProperty);
                _handle.Height = VisualThicknessDip;
                _handle.HorizontalAlignment = HorizontalAlignment.Stretch;
                _handle.VerticalAlignment = VerticalAlignment.Center;
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
                break;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer()
        => new DockSplitterAutomationPeer(this);

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // Spec 045 §2.22 — hover state uses the system accent brush so
        // HC themes show a clearly differentiated splitter under
        // pointer; falls back to a darker gray when no theme.
        _handle.Fill = ThemedBrush(
            "SystemControlHighlightAccentBrush",
            Color.FromArgb(0xAA, 0x80, 0x80, 0x80));
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_isCapturing) return;
        _handle.Fill = ThemedBrush(
            "SystemControlForegroundBaseMediumLowBrush",
            Color.FromArgb(0x33, 0x80, 0x80, 0x80));
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(this);
        if (!pointer.Properties.IsLeftButtonPressed) return;

        if (CapturePointer(e.Pointer))
        {
            _capturePointerId = e.Pointer.PointerId;
            BeginDragCore(ParentPosition(e));
            Focus(FocusState.Pointer);
            e.Handled = true;
        }
    }

    // ─── Shared drag lifecycle ──────────────────────────────────────────
    //
    //  The real pointer/keyboard handlers and the synthetic
    //  BeginSimulatedDrag / ContinueSimulatedDrag / EndSimulatedDrag API
    //  both funnel through these *Core methods so the math, diagnostic
    //  trace lines, event firing, and state transitions are guaranteed
    //  identical between the live-input path and the test-input path.
    //  The real handlers add only WinUI-specific concerns (CapturePointer,
    //  ReleasePointerCapture, Focus, e.Handled, pointer-id matching).

    /// <summary>
    /// Begin a drag. Mutates <see cref="_isCapturing"/> /
    /// <see cref="_captureOrigin"/>, snapshots the pair's grow values,
    /// and emits the PRESS trace. Called by <see cref="OnPointerPressed"/>
    /// and <see cref="BeginSimulatedDrag"/>.
    /// </summary>
    private void BeginDragCore(Point parentRelativeOrigin)
    {
        _isCapturing = true;
        _captureOrigin = parentRelativeOrigin;
        SnapshotPairAtCapture();
        Trace($"PRESS dir={_direction} origin=({_captureOrigin.X:F1},{_captureOrigin.Y:F1}) leadingGrow={_leadingGrowAtCapture:F3} pairGrow={_pairGrowAtCapture:F3}");
    }

    private void SnapshotPairAtCapture()
    {
        _leadingGrowAtCapture = 0;
        _pairGrowAtCapture = 0;
        if (VTH.GetParent(this) is not Microsoft.UI.Reactor.Layout.FlexPanel panel) return;
        int idx = -1;
        for (int i = 0; i < panel.Children.Count; i++)
            if (ReferenceEquals(panel.Children[i], this)) { idx = i; break; }
        if (idx <= 0 || idx >= panel.Children.Count - 1) return;
        if (panel.Children[idx - 1] is not FrameworkElement leading) return;
        if (panel.Children[idx + 1] is not FrameworkElement trailing) return;

        // Capture grow only. Each MOVE re-reads panel.ActualWidth /
        // ActualHeight + the panel's total grow live, so the DIP→grow
        // conversion always reflects the current layout. The drag never
        // touches inline Width/Height; pair size is driven entirely by
        // grow weight against the panel's current allocation.
        _leadingGrowAtCapture = Microsoft.UI.Reactor.Layout.FlexPanel.GetGrow(leading);
        var trailingGrow = Microsoft.UI.Reactor.Layout.FlexPanel.GetGrow(trailing);
        _pairGrowAtCapture = _leadingGrowAtCapture + trailingGrow;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isCapturing || e.Pointer.PointerId != _capturePointerId) return;
        ContinueDragCore(ParentPosition(e));
        e.Handled = true;
    }

    /// <summary>
    /// Continue a drag. Computes the cumulative cursor delta against the
    /// captured origin, emits the MOVE trace (with pre/post leading-pane
    /// extents for cursor-tracking diagnostics), and applies the new
    /// grow values via <see cref="ApplyAbsoluteGrowFromCapture"/>. Called
    /// by <see cref="OnPointerMoved"/> and
    /// <see cref="ContinueSimulatedDrag"/>. No-op when the splitter
    /// isn't currently capturing.
    /// </summary>
    private void ContinueDragCore(Point parentRelativePoint)
    {
        if (!_isCapturing) return;
        var cumDelta = _direction == DockSplitterDirection.Columns
            ? parentRelativePoint.X - _captureOrigin.X
            : parentRelativePoint.Y - _captureOrigin.Y;
        // Direct-mutate only; don't fire ResizeDelta during the drag.
        // The host accumulates the per-event deltas in its solver — if
        // each event passes a cumulative-from-origin delta, the host
        // applies them all and the model drifts an order of magnitude
        // past the actual cursor movement. Fire once at drag end with
        // the final pair-size delta via EndDragCore.
        //
        // Diagnostic-only pre-snapshot. Skip the visual-tree walk when no
        // sink is wired (hot path — fires at input rate during drag).
        var diagSink = DiagnosticSink;
        double preLeading = -1;
        if (diagSink is not null)
        {
            preLeading = (VTH.GetParent(this) is Microsoft.UI.Reactor.Layout.FlexPanel pp && pp.Children.Count > 0
                && pp.Children[0] is FrameworkElement fe)
                ? (_direction == DockSplitterDirection.Columns ? fe.ActualWidth : fe.ActualHeight)
                : -1;
        }
        ApplyAbsoluteGrowFromCapture(cumDelta);
        if (diagSink is not null)
        {
            var postLeading = (VTH.GetParent(this) is Microsoft.UI.Reactor.Layout.FlexPanel pp2 && pp2.Children.Count > 0
                && pp2.Children[0] is FrameworkElement fe2)
                ? (_direction == DockSplitterDirection.Columns ? fe2.ActualWidth : fe2.ActualHeight)
                : -1;
            diagSink($"MOVE p=({parentRelativePoint.X:F1},{parentRelativePoint.Y:F1}) cumDelta={cumDelta:F1} leadingGrowAtCapture={_leadingGrowAtCapture:F3} pairGrow={_pairGrowAtCapture:F3} preLeadingActual={preLeading:F1} postLeadingActual={postLeading:F1}");
        }
    }

    /// <summary>
    /// Direct-mutation drag path — pure-grow with live panel extent.
    ///
    /// The cursor delta is translated to a grow delta against the panel's
    /// LIVE extent (read fresh each call) and the panel's LIVE total grow.
    /// Earlier revisions cached <c>leading.ActualWidth</c> + the pair's
    /// summed DIPs at PointerPressed; that locked in stale values when
    /// the layout hadn't settled into its N-column shape by press time
    /// (3+ pane splits could report 2-pane widths on the first press),
    /// which made the splitter handle visually lag the cursor by the
    /// amount the snapshot was wrong by.
    ///
    /// Since cursor follow is exactly "grow such that leading width
    /// grows by cumulativeDeltaDip DIPs", and Yoga distributes the
    /// panel's allocated extent by <c>grow / totalPanelGrow</c>, the
    /// grow delta is <c>cumulativeDelta * totalPanelGrow / panelExtent</c>
    /// — both factors are live readings, so a layout shift mid-drag
    /// does not desync the math.
    /// </summary>
    private void ApplyAbsoluteGrowFromCapture(double cumulativeDeltaDip)
    {
        if (_pairGrowAtCapture <= 0) return;
        if (VTH.GetParent(this) is not Microsoft.UI.Reactor.Layout.FlexPanel panel) return;
        int idx = -1;
        for (int i = 0; i < panel.Children.Count; i++)
            if (ReferenceEquals(panel.Children[i], this)) { idx = i; break; }
        if (idx <= 0 || idx >= panel.Children.Count - 1) return;
        if (panel.Children[idx - 1] is not FrameworkElement leading) return;
        if (panel.Children[idx + 1] is not FrameworkElement trailing) return;

        // Live panel extent along the split axis. GetHostExtent already
        // subtracts sibling splitter handles, so it equals what Yoga
        // distributes across pane children via grow weight.
        var panelExtent = GetHostExtent();
        if (panelExtent < 1) return;

        // Live total grow across the panel's non-splitter children. The
        // splitter only mutates the pair's grow during a drag, so this
        // total is effectively stable, but reading it live is cheap and
        // self-correcting if some other code path adjusts a sibling.
        double totalPanelGrow = 0;
        for (int i = 0; i < panel.Children.Count; i++)
        {
            if (panel.Children[i] is DockSplitterControl) continue;
            if (panel.Children[i] is FrameworkElement fe)
                totalPanelGrow += Microsoft.UI.Reactor.Layout.FlexPanel.GetGrow(fe);
        }
        if (totalPanelGrow <= 0) totalPanelGrow = _pairGrowAtCapture;

        // Convert the cursor delta (DIPs) into grow space using the live
        // panel extent + total grow. deltaGrow * (panelExtent / totalGrow)
        // = cumulativeDeltaDip, so the leading pane's width changes by
        // exactly cumulativeDeltaDip — the splitter handle tracks the
        // cursor 1:1.
        var deltaGrow = totalPanelGrow * (cumulativeDeltaDip / panelExtent);

        // Min-size floor expressed in grow space (60 DIP minimum per pane).
        const double minDip = 60.0;
        var minGrow = totalPanelGrow * (minDip / panelExtent);
        // Pair can't be split below 2*minGrow; clamp the target leading
        // grow inside [minGrow, pairGrow - minGrow] so neither side
        // collapses below 60 DIPs.
        if (_pairGrowAtCapture <= 2 * minGrow) return;
        var newLeadingGrow = Math.Clamp(
            _leadingGrowAtCapture + deltaGrow,
            minGrow,
            _pairGrowAtCapture - minGrow);
        var newTrailingGrow = _pairGrowAtCapture - newLeadingGrow;

        Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(leading, newLeadingGrow);
        Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(trailing, newTrailingGrow);
    }

    /// <summary>
    /// Pointer position relative to the splitter's parent panel. Falls
    /// back to splitter-local coords when the parent isn't available
    /// (control not yet attached) — the fallback case only fires on the
    /// PointerPressed before layout, when no movement has occurred yet.
    /// </summary>
    private Point ParentPosition(PointerRoutedEventArgs e)
    {
        var parent = VTH.GetParent(this) as UIElement;
        return parent is not null
            ? e.GetCurrentPoint(parent).Position
            : e.GetCurrentPoint(this).Position;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isCapturing || e.Pointer.PointerId != _capturePointerId) return;
        // ─── ORDER MATTERS — see SplitterMatrix_K02/K03 regression tests ──
        //
        //  WinUI fires OnPointerCaptureLost SYNCHRONOUSLY inside
        //  ReleasePointerCapture. If that handler observes _isCapturing
        //  still true, AbortDragCore runs and fires a destructive
        //  ResizeDelta(0, IsFinal=true) that the host's solver applies
        //  as "no change" — snap-back to the pre-drag position. The
        //  user's drag is lost.
        //
        //  EndDragCore must complete its _isCapturing=false transition
        //  BEFORE ReleasePointerCapture is invoked, so the synchronous
        //  capture-loss handler observes a non-capturing state and
        //  early-returns. The legitimate drag-delta event from
        //  EndDragCore is then the only terminal event the host sees.
        //
        //  Keep this order in lock step with SimulateRealReleaseSequence.
        _capturePointerId = 0;
        EndDragCore(ParentPosition(e));
        try { ReleasePointerCapture(e.Pointer); } catch { /* already lost */ }
        e.Handled = true;
    }

    /// <summary>
    /// End a drag. Applies the final cumulative delta, restores pair
    /// state, emits the RELEASE trace, and fires <see cref="ResizeDelta"/>
    /// with <c>IsFinal=true</c>. The solver convention is positive-delta=
    /// shrink-leading, so the sign of the cursor delta is negated when
    /// raising the event. Called by <see cref="OnPointerReleased"/>,
    /// <see cref="OnKeyDown"/> (via <see cref="KeyboardStepCore"/>), and
    /// <see cref="EndSimulatedDrag"/>. No-op when not capturing.
    /// </summary>
    private void EndDragCore(Point parentRelativePoint)
    {
        if (!_isCapturing) return;
        var cumDelta = _direction == DockSplitterDirection.Columns
            ? parentRelativePoint.X - _captureOrigin.X
            : parentRelativePoint.Y - _captureOrigin.Y;
        // Capture pre-restore leading width for diagnostic — what the
        // drag actually rendered just before we hand off to grow.
        var preRestoreLeading = (VTH.GetParent(this) is Microsoft.UI.Reactor.Layout.FlexPanel pp && pp.Children.Count > 0
            && pp.Children[0] is FrameworkElement fe)
            ? (_direction == DockSplitterDirection.Columns ? fe.ActualWidth : fe.ActualHeight)
            : -1;
        ApplyAbsoluteGrowFromCapture(cumDelta);
        _isCapturing = false;
        RestorePairToGrow();
        var hostExtent = GetHostExtent();
        double postRestoreLeadingGrow = -1, postRestoreLeadingActual = -1;
        if (VTH.GetParent(this) is Microsoft.UI.Reactor.Layout.FlexPanel pp2 && pp2.Children.Count > 0
            && pp2.Children[0] is FrameworkElement fe2)
        {
            postRestoreLeadingGrow = Microsoft.UI.Reactor.Layout.FlexPanel.GetGrow(fe2);
            postRestoreLeadingActual = _direction == DockSplitterDirection.Columns ? fe2.ActualWidth : fe2.ActualHeight;
        }
        Trace($"RELEASE p=({parentRelativePoint.X:F1},{parentRelativePoint.Y:F1}) cumDelta={cumDelta:F1} preRestoreLeading={preRestoreLeading:F1} postRestoreLeadingGrow={postRestoreLeadingGrow:F3} postRestoreLeadingActual={postRestoreLeadingActual:F1} hostExtent={hostExtent:F1}");
        ResizeDelta?.Invoke(this, new DockSplitterDeltaEventArgs(-cumDelta, _direction, hostExtent, isFinal: true));
    }

    private void RestorePairToGrow()
    {
        // Direction A (current path): the drag path writes Grow values
        // directly and never touches inline Width/Height/MinHeight on
        // the pair OR the panel — so there is no "restore" work to do.
        //
        // Older revisions cleared inline Width/Height defensively here.
        // That cleanup was destructive: it stomped app-set
        // DockTabGroup.Width hints + explicit FlexPanel.Width values
        // after every drag, which the SplitterMatrix_A01 fixture caught.
        // Intentionally a no-op; kept as a named seam so future callers
        // that DO pin sizes have a single restore site.
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_isCapturing) return;
        _capturePointerId = 0;
        AbortDragCore();
        // Real-handler-only: fade the handle back to its un-hovered chrome
        // since we'll get no PointerExited after the capture loss.
        _handle.Fill = ThemedBrush(
            "SystemControlForegroundBaseMediumLowBrush",
            Color.FromArgb(0x88, 0x80, 0x80, 0x80));
    }

    /// <summary>
    /// Abort a drag without applying a final delta. Mirrors the normal
    /// release path's cleanup (state transition + RestorePairToGrow)
    /// then fires <see cref="ResizeDelta"/> with delta=0 / IsFinal=true
    /// so the host's solver catches up to the current grow values.
    /// Called by <see cref="OnPointerCaptureLost"/> and
    /// <see cref="AbortSimulatedDrag"/>. No-op when not capturing.
    /// </summary>
    private void AbortDragCore()
    {
        if (!_isCapturing) return;
        _isCapturing = false;
        RestorePairToGrow();
        ResizeDelta?.Invoke(this, new DockSplitterDeltaEventArgs(0, _direction, GetHostExtent(), isFinal: true));
    }

    // Spec 045 §2.22 — resolve a theme resource brush with a literal
    // ARGB fallback. The lookup walks `Application.Current.Resources`
    // which is the same dictionary high-contrast theme swaps populate,
    // so the splitter chrome updates on a system HC toggle without
    // bespoke wiring.
    private static Brush ThemedBrush(string key, Color fallback)
    {
        try
        {
            if (Application.Current?.Resources is { } res &&
                res.TryGetValue(key, out var v) &&
                v is Brush b)
                return b;
        }
        catch (InvalidOperationException)
        {
            // Headless harness — no Application instance / no UI thread.
        }
        catch (global::System.Runtime.InteropServices.COMException ex)
        {
            // Resource dictionary lookup can fail with WinUI's COM
            // wrappers when called before XAML is fully initialized.
            global::System.Diagnostics.Debug.WriteLine(
                $"[Docking] DockSplitter ThemedBrush('{key}') COMException — using fallback. HRESULT=0x{ex.HResult:X8}");
        }
        return new SolidColorBrush(fallback);
    }

    /// <summary>Test hook — fires the <see cref="ResizeDelta"/> event with
    /// caller-supplied args, bypassing pointer / keyboard. Used by the
    /// programmatic-drag self-test fixture (§2.1).</summary>
    internal void RaiseResizeDeltaForTest(DockSplitterDeltaEventArgs args)
        => ResizeDelta?.Invoke(this, args);

    // ─── Synthetic injection API ────────────────────────────────────────
    //
    //  Each method below is a thin wrapper over the same *Core method the
    //  real WinUI event handlers call. The only thing these wrappers add
    //  is zeroing `_capturePointerId` (no real pointer is in play), so
    //  every line of meaningful behavior — math, state transitions,
    //  diagnostic traces, event firing — runs through the shared path.
    //  That is what makes the matrix fixtures meaningful: a regression
    //  caught by a synthetic drag is a regression in production code.

    /// <summary>
    /// Test hook — begin a simulated pointer drag. Calls
    /// <see cref="BeginDragCore"/> directly, so the resulting state +
    /// snapshot + PRESS trace is byte-identical to a real pointer press.
    /// Subsequent <see cref="ContinueSimulatedDrag"/> /
    /// <see cref="EndSimulatedDrag"/> calls drive the same core methods
    /// the real handlers use. Pair this with
    /// <c>InternalsVisibleTo("Reactor.Tests")</c>.
    /// </summary>
    /// <param name="parentRelativeOrigin">
    /// Cursor position in the splitter's parent (the <c>FlexPanel</c>)
    /// coordinate space at the start of the drag. The real
    /// <c>OnPointerPressed</c> computes this via
    /// <c>e.GetCurrentPoint(parent).Position</c>; tests can pass
    /// arbitrary values since the math depends only on cursor delta.
    /// </param>
    internal void BeginSimulatedDrag(Point parentRelativeOrigin)
    {
        _capturePointerId = 0;
        BeginDragCore(parentRelativeOrigin);
    }

    /// <summary>
    /// Test hook — continue a simulated drag. Delegates to
    /// <see cref="ContinueDragCore"/>, the same method
    /// <see cref="OnPointerMoved"/> calls — so the MOVE trace + grow
    /// math the production drag uses is exactly what tests exercise.
    /// No-op if <see cref="BeginSimulatedDrag"/> hasn't been called.
    /// </summary>
    /// <param name="parentRelativePoint">
    /// Current cursor position in the splitter's parent coordinate
    /// space. The cumulative delta = <c>parentRelativePoint - origin</c>.
    /// </param>
    internal void ContinueSimulatedDrag(Point parentRelativePoint)
        => ContinueDragCore(parentRelativePoint);

    /// <summary>
    /// Test hook — finish a simulated drag. Delegates to
    /// <see cref="EndDragCore"/>, the same method
    /// <see cref="OnPointerReleased"/> calls. Fires
    /// <see cref="ResizeDelta"/> with <c>IsFinal=true</c>; the solver
    /// convention is positive-delta=shrink-leading so the sign of
    /// <paramref name="parentRelativePoint"/> minus the origin is
    /// negated when raising the event.
    /// </summary>
    /// <param name="parentRelativePoint">
    /// Final cursor position in the splitter's parent coordinate
    /// space. Determines the cumulative delta for the terminal event.
    /// </param>
    internal void EndSimulatedDrag(Point parentRelativePoint)
        => EndDragCore(parentRelativePoint);

    /// <summary>
    /// Test hook — abort a simulated drag without applying a final
    /// delta. Delegates to <see cref="AbortDragCore"/>, the same method
    /// <see cref="OnPointerCaptureLost"/> calls (minus the handle-fade
    /// chrome update which is real-pointer-specific). No-op if not
    /// currently capturing.
    /// </summary>
    internal void AbortSimulatedDrag()
    {
        _capturePointerId = 0;
        AbortDragCore();
    }

    /// <summary>
    /// Test hook — simulate a single keyboard step. Delegates to
    /// <see cref="KeyboardStepCore"/>, the same method the arrow-key
    /// branch of <see cref="OnKeyDown"/> calls — so RTL inversion and
    /// the snapshot/apply/restore/fire-event sequence run identically.
    /// <paramref name="forward"/> follows the screen-axis convention
    /// (Right/Down under LTR), inverted automatically under RTL.
    /// </summary>
    internal void SimulateKeyboardStep(bool forward)
        => KeyboardStepCore(forward);

    /// <summary>
    /// Test hook — convenience wrapper for fixtures that don't need to
    /// inspect mid-drag state. Drives the full press/move/release
    /// lifecycle in one call by chaining
    /// <see cref="BeginSimulatedDrag"/> + <see cref="EndSimulatedDrag"/>.
    /// New tests should prefer the granular methods so the rig can read
    /// pane state between events.
    /// </summary>
    internal void SimulatePointerDragForTest(double cumulativeDeltaDip)
    {
        var origin = new Point(0, 0);
        var dest = _direction == DockSplitterDirection.Columns
            ? new Point(cumulativeDeltaDip, 0)
            : new Point(0, cumulativeDeltaDip);
        BeginSimulatedDrag(origin);
        EndSimulatedDrag(dest);
    }

    /// <summary>
    /// Test hook — true if the splitter is currently in a drag capture.
    /// Tests read this from inside a <see cref="ResizeDelta"/> handler to
    /// assert the ordering invariant: the terminal event must fire AFTER
    /// <c>_isCapturing</c> has flipped to false, so any synchronous
    /// follow-up capture-loss handler (WinUI's PointerCaptureLost fires
    /// synchronously during ReleasePointerCapture) early-returns instead
    /// of firing a destructive <c>ResizeDelta(0, IsFinal=true)</c> that
    /// would revert the drag.
    /// </summary>
    internal bool IsCapturingForTest => _isCapturing;

    /// <summary>
    /// Test hook — model the WinUI <c>OnPointerReleased</c> handler's
    /// interaction with the synchronous <c>OnPointerCaptureLost</c> that
    /// fires inside <c>ReleasePointerCapture</c>. The handler's
    /// correctness depends on ordering: <see cref="EndDragCore"/> must
    /// complete its <c>_isCapturing = false</c> transition BEFORE
    /// <c>ReleasePointerCapture</c> is invoked, otherwise the
    /// synchronous capture-loss handler observes a live capture and
    /// runs <see cref="AbortDragCore"/> — which fires a destructive
    /// <c>ResizeDelta(0, IsFinal=true)</c> the host applies as "no
    /// change" against the pre-drag ratios, snapping the splitter
    /// back to its starting position.
    /// <para>
    /// This hook MIRRORS the actual order in
    /// <see cref="OnPointerReleased"/>: every step of the production
    /// handler (sans the WinUI-specific <c>ReleasePointerCapture</c>
    /// call, which is modeled by an unconditional
    /// <see cref="ProduceSyntheticCaptureLost"/>). When OnPointerReleased
    /// is buggy (ReleasePointerCapture before EndDragCore), this hook
    /// reproduces the destructive zero-delta event and the K-category
    /// fixtures fail. After the fix, it produces the legitimate
    /// drag-delta event and the fixtures pass. Keep this body in lock
    /// step with <see cref="OnPointerReleased"/>.
    /// </para>
    /// </summary>
    internal void SimulateRealReleaseSequence(Point parentRelativePoint)
    {
        if (!_isCapturing) return;
        // ─── Production-equivalent body — keep in sync with OnPointerReleased ─
        _capturePointerId = 0;
        EndDragCore(parentRelativePoint);
        ProduceSyntheticCaptureLost();   // models ReleasePointerCapture firing sync PointerCaptureLost — must be a no-op now
        // ─────────────────────────────────────────────────────────────────────
    }

    /// <summary>
    /// Inline simulator for the synchronous <c>OnPointerCaptureLost</c>
    /// callback WinUI invokes during <c>ReleasePointerCapture</c>.
    /// Identical body to <see cref="OnPointerCaptureLost"/> minus the
    /// chrome-fade (no real pointer is in play).
    /// </summary>
    private void ProduceSyntheticCaptureLost()
    {
        if (!_isCapturing) return;
        _capturePointerId = 0;
        AbortDragCore();
    }

    /// <summary>
    /// Walk to the parent panel (the FlexPanel the splitter is interleaved
    /// inside) and return the extent USABLE by the panes — the parent's
    /// measured extent along the split axis minus the total space taken
    /// by sibling splitter handles. This is what Yoga distributes among
    /// the pane children via flex.grow, so it's what the solver should
    /// reason about (otherwise the solver computes ratios against N+16
    /// DIP of space and the renderer paints into N DIP, producing a
    /// visible "jump back" at drag-end). Returns 0 if the parent isn't
    /// available yet — caller treats as "no delta applied this frame".
    /// </summary>
    internal double GetHostExtent()
    {
        if (VTH.GetParent(this) is not FrameworkElement parent) return 0;
        var totalExtent = _direction == DockSplitterDirection.Columns
            ? parent.ActualWidth
            : parent.ActualHeight;
        if (totalExtent <= 0) return 0;

        // Subtract every sibling splitter's measured size on the axis so
        // the solver works in the same DIP space Yoga distributes via
        // flex.grow (= total minus the splitter handles). When the
        // parent isn't a FlexPanel we can't enumerate siblings; fall
        // back to subtracting just this splitter's own size.
        double splitterAccum;
        if (parent is Microsoft.UI.Reactor.Layout.FlexPanel flex)
        {
            splitterAccum = 0;
            for (int i = 0; i < flex.Children.Count; i++)
            {
                if (flex.Children[i] is DockSplitterControl s && s.Direction == _direction)
                {
                    splitterAccum += _direction == DockSplitterDirection.Columns
                        ? s.ActualWidth
                        : s.ActualHeight;
                }
            }
        }
        else
        {
            splitterAccum = _direction == DockSplitterDirection.Columns
                ? this.ActualWidth
                : this.ActualHeight;
        }
        return Math.Max(0, totalExtent - splitterAccum);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Spec 045 §2.23 — under FlowDirection.RightToLeft the visual
        // "leading" pane (index 0 in the FlexPanel children list) paints
        // on the right edge instead of the left. The pointer-drag path
        // is RTL-correct by construction because WinUI reflects the
        // pointer coordinate space inside RTL containers (cursor moving
        // screen-right reports negative ΔX under RTL). Arrow keys are
        // physical (`VirtualKey.Left` is always the physical Left arrow
        // regardless of FlowDirection), so we invert the Left/Right
        // mapping under RTL so a Right press still moves the handle in
        // the same visual direction. Vertical (Rows) splitters are
        // unaffected.
        bool forward;
        switch (e.Key)
        {
            case VirtualKey.Left when _direction == DockSplitterDirection.Columns:
                forward = false; break;
            case VirtualKey.Right when _direction == DockSplitterDirection.Columns:
                forward = true; break;
            case VirtualKey.Up when _direction == DockSplitterDirection.Rows:
                forward = false; break;
            case VirtualKey.Down when _direction == DockSplitterDirection.Rows:
                forward = true; break;
            default: return;
        }

        KeyboardStepCore(forward);
        Focus(FocusState.Keyboard);
        e.Handled = true;
    }

    /// <summary>
    /// Apply a single keyboard step to the splitter. Encapsulates the
    /// RTL-inversion + snapshot/apply/restore/fire-event sequence shared
    /// between <see cref="OnKeyDown"/> and
    /// <see cref="SimulateKeyboardStep"/>. <paramref name="forward"/>
    /// follows the screen-axis convention: Right/Down on a Columns/Rows
    /// splitter under LTR; the method inverts horizontal for RTL so
    /// "forward=true" always moves the splitter handle in the same
    /// visual direction.
    /// </summary>
    private void KeyboardStepCore(bool forward)
    {
        double step = KeyboardStep;
        bool invertHorizontal =
            _direction == DockSplitterDirection.Columns
            && FlowDirection == FlowDirection.RightToLeft;
        var rawDelta = forward
            ? (invertHorizontal ? -step : step)
            : (invertHorizontal ? step : -step);

        // A keyboard step is a self-contained press+release: snapshot,
        // apply, restore, and fire the terminal event in one shot. We
        // do this inline (rather than calling BeginDragCore + EndDragCore)
        // because there's no MOVE phase and no cursor coordinate space —
        // re-using those would require a synthetic Point and the PRESS
        // trace string would be misleading. The math is identical.
        SnapshotPairAtCapture();
        ApplyAbsoluteGrowFromCapture(rawDelta);
        RestorePairToGrow();
        ResizeDelta?.Invoke(this, new DockSplitterDeltaEventArgs(-rawDelta, _direction, GetHostExtent(), isFinal: true));
    }

    private sealed partial class DockSplitterAutomationPeer : FrameworkElementAutomationPeer
    {
        public DockSplitterAutomationPeer(DockSplitterControl owner) : base(owner) { }
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;
        protected override string GetClassNameCore() => "DockSplitter";
        protected override string GetLocalizedControlTypeCore() => "splitter";
    }
}
