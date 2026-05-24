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
    // Cached at capture time so live mutations don't observe stale
    // ActualWidth/Height between layout commits. Updated only on
    // PointerPressed; the drag uses this fixed slice for the entire drag.
    private double _pairDipAtCapture;
    private double _leadingDipAtCapture;
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
            _isCapturing = true;
            _captureOrigin = ParentPosition(e);
            _capturePointerId = e.Pointer.PointerId;
            // Snapshot the pair's current sizes + grows so mutations during
            // the drag don't depend on ActualWidth/Height that may lag
            // behind a not-yet-committed layout pass.
            SnapshotPairAtCapture();
            Trace($"PRESS dir={_direction} origin=({_captureOrigin.X:F1},{_captureOrigin.Y:F1}) leadingDip={_leadingDipAtCapture:F1} pairDip={_pairDipAtCapture:F1} pairGrow={_pairGrowAtCapture:F3}");
            Focus(FocusState.Pointer);
            e.Handled = true;
        }
    }

    private void SnapshotPairAtCapture()
    {
        _pairDipAtCapture = 0;
        _leadingDipAtCapture = 0;
        _pairGrowAtCapture = 0;
        if (VTH.GetParent(this) is not Microsoft.UI.Reactor.Layout.FlexPanel panel) return;
        int idx = -1;
        for (int i = 0; i < panel.Children.Count; i++)
            if (ReferenceEquals(panel.Children[i], this)) { idx = i; break; }
        if (idx <= 0 || idx >= panel.Children.Count - 1) return;
        if (panel.Children[idx - 1] is not FrameworkElement leading) return;
        if (panel.Children[idx + 1] is not FrameworkElement trailing) return;

        var leadingGrow = Microsoft.UI.Reactor.Layout.FlexPanel.GetGrow(leading);
        var trailingGrow = Microsoft.UI.Reactor.Layout.FlexPanel.GetGrow(trailing);
        _pairGrowAtCapture = leadingGrow + trailingGrow;

        // Snapshot the actual measured extents of the pair. For 2-child
        // splits this happens to equal GetHostExtent() (panel.ActualSize
        // minus splitter handles); for 3+ child splits it is strictly
        // smaller because the other siblings consume some panel space.
        // The drag math distributes pairGrow proportionally to the
        // target leading-fraction of the PAIR, not the whole panel —
        // so the snapshot must be in the pair's DIP space or the rendered
        // splitter drifts away from the cursor (Scene J 3-child repro).
        var leadingActual = _direction == DockSplitterDirection.Columns
            ? leading.ActualWidth : leading.ActualHeight;
        var trailingActual = _direction == DockSplitterDirection.Columns
            ? trailing.ActualWidth : trailing.ActualHeight;
        _pairDipAtCapture = leadingActual + trailingActual;
        _leadingDipAtCapture = leadingActual;

        // Direction A drag path no longer writes inline Width/Height
        // on the panes — pair size is entirely driven by grow weight
        // against the panel's (unchanging) parent allocation. No need
        // to pin the panel: its measured size cannot drift because no
        // child claims a fixed absolute size during the drag.
        //
        // The perpendicular pin is also unnecessary now: panes can
        // re-measure perpendicular content freely; the panel just lets
        // its parent's allocation flow through. (If we observe perp-
        // axis flicker during a drag, we'd reintroduce just the perp
        // pin here.)
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isCapturing || e.Pointer.PointerId != _capturePointerId) return;
        var p = ParentPosition(e);
        var cumDelta = _direction == DockSplitterDirection.Columns
            ? p.X - _captureOrigin.X
            : p.Y - _captureOrigin.Y;
        // Direct-mutate only; don't fire ResizeDelta during the drag.
        // The host accumulates the per-event deltas in its solver — if
        // each event passes a cumulative-from-origin delta, the host
        // applies them all and the model drifts an order of magnitude
        // past the actual cursor movement. Fire once at drag end with
        // the final pair-size delta via OnPointerReleased.
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
            diagSink($"MOVE p=({p.X:F1},{p.Y:F1}) cumDelta={cumDelta:F1} leadingAtCapture={_leadingDipAtCapture:F1} pairDip={_pairDipAtCapture:F1} preLeadingActual={preLeading:F1} postLeadingActual={postLeading:F1}");
        }
        e.Handled = true;
    }

    /// <summary>
    /// Direct-mutation drag path — Direction A (pure-grow).
    ///
    /// Pre-2026-05-21-experiment, this method wrote inline
    /// <c>Width</c>/<c>Height</c> on the panes (absolute DIPs) and
    /// zeroed Grow. On release, <see cref="RestorePairToGrow"/>
    /// converted back to grow values and cleared inline sizes. The
    /// inline-Width detour caused a measurable snap-back on release
    /// because the panel size during drag (children-sum) differed
    /// from after release (parent-allocation), so the ratio-space
    /// re-render landed in a different DIP space than the cursor.
    ///
    /// Direction A: do NOT touch inline Width during the drag. Instead,
    /// compute new <c>FlexPanel.Grow</c> values for the pair that
    /// REPRESENT THE TARGET PROPORTION directly. Yoga redistributes
    /// the panel's parent-allocated extent across the children by
    /// grow weight — exactly as WinUI Grid + GridUnitType.Star
    /// redistributes a Grid's children. Single source of truth; no
    /// mode-switch at release; no panel-size drift.
    /// </summary>
    private void ApplyAbsoluteGrowFromCapture(double cumulativeDeltaDip)
    {
        if (_pairDipAtCapture < 1) return;
        if (VTH.GetParent(this) is not Microsoft.UI.Reactor.Layout.FlexPanel panel) return;
        int idx = -1;
        for (int i = 0; i < panel.Children.Count; i++)
            if (ReferenceEquals(panel.Children[i], this)) { idx = i; break; }
        if (idx <= 0 || idx >= panel.Children.Count - 1) return;
        if (panel.Children[idx - 1] is not FrameworkElement leading) return;
        if (panel.Children[idx + 1] is not FrameworkElement trailing) return;

        const double minDip = 60.0;
        var newLeading = Math.Clamp(
            _leadingDipAtCapture + cumulativeDeltaDip,
            minDip,
            _pairDipAtCapture - minDip);
        if (newLeading <= 0 || double.IsNaN(newLeading)) return;

        // Pure-grow path: distribute pairGrow proportionally to the
        // target DIP split. Trailing share = totalGrow - leadingGrow, so
        // newTrailing in DIPs is not needed explicitly. Inline
        // Width/Height is NOT touched — the panel stays at its parent
        // allocation throughout the drag.
        var totalGrow = _pairGrowAtCapture > 0 ? _pairGrowAtCapture : 1.0;
        var newLeadingGrow = totalGrow * (newLeading / _pairDipAtCapture);
        var newTrailingGrow = totalGrow - newLeadingGrow;
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
        _isCapturing = false;
        _capturePointerId = 0;
        try { ReleasePointerCapture(e.Pointer); } catch { /* already lost */ }
        // Compute the final cursor-driven delta and fire ResizeDelta once
        // so the host's model catches up. The solver convention is
        // positive=shrink-leading, so negate.
        var p = ParentPosition(e);
        var cumDelta = _direction == DockSplitterDirection.Columns
            ? p.X - _captureOrigin.X
            : p.Y - _captureOrigin.Y;
        // Capture pre-restore leading width for diagnostic — what the
        // drag actually rendered just before we hand off to grow.
        var preRestoreLeading = (VTH.GetParent(this) is Microsoft.UI.Reactor.Layout.FlexPanel pp && pp.Children.Count > 0
            && pp.Children[0] is FrameworkElement fe)
            ? (_direction == DockSplitterDirection.Columns ? fe.ActualWidth : fe.ActualHeight)
            : -1;
        // Restore panes to grow-based sizing so the host's re-render
        // (triggered by ResizeDelta) lands cleanly via the normal path.
        RestorePairToGrow();
        var hostExtent = GetHostExtent();
        // Read post-restore leading grow + size for the trace.
        double postRestoreLeadingGrow = -1, postRestoreLeadingActual = -1;
        if (VTH.GetParent(this) is Microsoft.UI.Reactor.Layout.FlexPanel pp2 && pp2.Children.Count > 0
            && pp2.Children[0] is FrameworkElement fe2)
        {
            postRestoreLeadingGrow = Microsoft.UI.Reactor.Layout.FlexPanel.GetGrow(fe2);
            postRestoreLeadingActual = _direction == DockSplitterDirection.Columns ? fe2.ActualWidth : fe2.ActualHeight;
        }
        Trace($"RELEASE p=({p.X:F1},{p.Y:F1}) cumDelta={cumDelta:F1} preRestoreLeading={preRestoreLeading:F1} postRestoreLeadingGrow={postRestoreLeadingGrow:F3} postRestoreLeadingActual={postRestoreLeadingActual:F1} hostExtent={hostExtent:F1}");
        ResizeDelta?.Invoke(this, new DockSplitterDeltaEventArgs(-cumDelta, _direction, hostExtent, isFinal: true));
        e.Handled = true;
    }

    private void RestorePairToGrow()
    {
        // Direction A: the drag path now writes Grow values directly;
        // there is nothing to "restore" because we never left grow-space.
        //
        // Defensive cleanup: clear any leftover inline Width/Height +
        // panel pin that an earlier code path (or a future caller) may
        // have set. Costs nothing when the props are already unset.
        if (VTH.GetParent(this) is not Microsoft.UI.Reactor.Layout.FlexPanel panel) return;
        int idx = -1;
        for (int i = 0; i < panel.Children.Count; i++)
            if (ReferenceEquals(panel.Children[i], this)) { idx = i; break; }
        if (idx <= 0 || idx >= panel.Children.Count - 1) return;
        if (panel.Children[idx - 1] is not FrameworkElement leading) return;
        if (panel.Children[idx + 1] is not FrameworkElement trailing) return;

        leading.ClearValue(FrameworkElement.WidthProperty);
        trailing.ClearValue(FrameworkElement.WidthProperty);
        leading.ClearValue(FrameworkElement.HeightProperty);
        trailing.ClearValue(FrameworkElement.HeightProperty);
        leading.ClearValue(FrameworkElement.MinHeightProperty);
        trailing.ClearValue(FrameworkElement.MinHeightProperty);
        panel.ClearValue(FrameworkElement.WidthProperty);
        panel.ClearValue(FrameworkElement.HeightProperty);
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_isCapturing) return;
        _isCapturing = false;
        _capturePointerId = 0;
        // Mirror the normal release path: clear any defensive inline
        // width/height/min-height state before the final ResizeDelta so
        // a capture-loss mid-drag converges to the same cleanup as a
        // pointer release. Skipping this left leading/trailing panes
        // pinned by inline size values that the host's re-render then
        // had to undo via grow.
        RestorePairToGrow();
        ResizeDelta?.Invoke(this, new DockSplitterDeltaEventArgs(0, _direction, GetHostExtent(), isFinal: true));
        _handle.Fill = ThemedBrush(
            "SystemControlForegroundBaseMediumLowBrush",
            Color.FromArgb(0x88, 0x80, 0x80, 0x80));
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

    /// <summary>
    /// Test hook — simulate a complete pointer drag: snapshot pair +
    /// apply absolute delta + release + fire <see cref="ResizeDelta"/>.
    /// Mirrors the side-effects of the production drag path (the same
    /// order as <c>OnPointerReleased</c>) so test fixtures can exercise
    /// the post-drag state — including the host's ratio-store sync and
    /// re-render — without needing real pointer input.
    /// </summary>
    internal void SimulatePointerDragForTest(double cumulativeDeltaDip)
    {
        SnapshotPairAtCapture();
        ApplyAbsoluteGrowFromCapture(cumulativeDeltaDip);
        RestorePairToGrow();
        ResizeDelta?.Invoke(this, new DockSplitterDeltaEventArgs(
            -cumulativeDeltaDip, _direction, GetHostExtent(), isFinal: true));
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
        double step = KeyboardStep;
        // Direct-mutate path: positive raw delta = grow leading (cursor
        // direction). Right/Down → +step; Left/Up → -step. The fired
        // ResizeDelta event uses the solver convention (negated).
        //
        // Spec 045 §2.23 — under FlowDirection.RightToLeft the visual
        // "leading" pane (index 0 in the FlexPanel children list) paints
        // on the right edge instead of the left. The pointer-drag path
        // is RTL-correct by construction because WinUI reflects the
        // pointer coordinate space inside RTL containers (cursor moving
        // screen-right reports negative ΔX under RTL). Arrow keys are
        // physical (`VirtualKey.Left` is always the physical Left arrow
        // regardless of FlowDirection), so we invert the Left/Right
        // mapping under RTL so a Right press still grows the screen-
        // right pane visually. Vertical (Rows) splitters are unaffected.
        bool invertHorizontal =
            _direction == DockSplitterDirection.Columns
            && FlowDirection == FlowDirection.RightToLeft;
        double rawDelta;
        switch (e.Key)
        {
            case VirtualKey.Left when _direction == DockSplitterDirection.Columns:
                rawDelta = invertHorizontal ? step : -step; break;
            case VirtualKey.Right when _direction == DockSplitterDirection.Columns:
                rawDelta = invertHorizontal ? -step : step; break;
            case VirtualKey.Up when _direction == DockSplitterDirection.Rows:
                rawDelta = -step; break;
            case VirtualKey.Down when _direction == DockSplitterDirection.Rows:
                rawDelta = step; break;
            default: return;
        }

        // Snapshot the current pair, then apply the arrow step as an
        // absolute cursor delta — same code path as the pointer drag.
        SnapshotPairAtCapture();
        ApplyAbsoluteGrowFromCapture(rawDelta);
        // Same fix as OnPointerReleased: convert inline sizes back into
        // grow values + release the perpendicular pin so window resize
        // continues to work after the keyboard nudge.
        RestorePairToGrow();
        ResizeDelta?.Invoke(this, new DockSplitterDeltaEventArgs(-rawDelta, _direction, GetHostExtent(), isFinal: true));
        Focus(FocusState.Keyboard);
        e.Handled = true;
    }

    private sealed partial class DockSplitterAutomationPeer : FrameworkElementAutomationPeer
    {
        public DockSplitterAutomationPeer(DockSplitterControl owner) : base(owner) { }
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Thumb;
        protected override string GetClassNameCore() => "DockSplitter";
        protected override string GetLocalizedControlTypeCore() => "splitter";
    }
}
