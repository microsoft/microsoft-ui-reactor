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

        // ~50% opaque gray handle so the splitter is visible against both
        // light and dark backgrounds. Hover transitions to a stronger shade
        // via OnPointerEntered.
        _handle = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x88, 0x80, 0x80, 0x80)),
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
        if (_handle.Fill is SolidColorBrush brush)
            brush.Color = Color.FromArgb(0xAA, 0x80, 0x80, 0x80);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_isCapturing) return;
        if (_handle.Fill is SolidColorBrush brush)
            brush.Color = Color.FromArgb(0x33, 0x80, 0x80, 0x80);
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

        _leadingDipAtCapture = _direction == DockSplitterDirection.Columns
            ? leading.ActualWidth
            : leading.ActualHeight;
        var trailingDip = _direction == DockSplitterDirection.Columns
            ? trailing.ActualWidth
            : trailing.ActualHeight;
        _pairDipAtCapture = _leadingDipAtCapture + trailingDip;
        _pairGrowAtCapture = Microsoft.UI.Reactor.Layout.FlexPanel.GetGrow(leading)
                           + Microsoft.UI.Reactor.Layout.FlexPanel.GetGrow(trailing);

        // Pin the splitter's parent panel on the PERPENDICULAR axis so
        // mutating pane sizes on our axis doesn't ripple into the outer
        // layout via DesiredSize changes (e.g., resizing column widths
        // would otherwise reflow TabView content, change topInner's
        // measured Height, and let the outer Vertical FlexPanel
        // redistribute — shrinking the bottom row visibly during the
        // drag).
        if (_direction == DockSplitterDirection.Columns)
        {
            if (panel.ActualHeight > 0) panel.Height = panel.ActualHeight;
        }
        else
        {
            if (panel.ActualWidth > 0) panel.Width = panel.ActualWidth;
        }
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
        ApplyAbsoluteGrowFromCapture(cumDelta);
        e.Handled = true;
    }

    /// <summary>
    /// Direct-mutation fast path during a drag. Reads the splitter's
    /// parent <see cref="Microsoft.UI.Reactor.Layout.FlexPanel"/> and its
    /// immediate-sibling children, shifts their <c>FlexPanel.Grow</c>
    /// attached values by <paramref name="rawDeltaDip"/> on the leading
    /// side (cursor-direction = positive), with min-size clamping.
    /// Bypasses Reactor's reconciler — the panel's
    /// <see cref="Microsoft.UI.Xaml.UIElement.InvalidateMeasure"/> fires
    /// from the attached-property change, and the visible layout updates
    /// without a re-render pass that would otherwise detach the splitter
    /// (mysteriously) and kill pointer capture.
    /// </summary>
    /// <summary>
    /// Apply a cumulative-from-capture pointer displacement to the
    /// splitter's pair. Uses snapshotted pair size + grow so layout-lag
    /// during rapid PointerMoved events doesn't reintroduce sub-pixel
    /// drift (the "shimmy"). For incremental callers (arrow keys), see
    /// <see cref="ApplyDirectGrowMutation"/>.
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
        var newTrailing = _pairDipAtCapture - newLeading;

        if (_direction == DockSplitterDirection.Columns)
        {
            leading.Width = newLeading;
            trailing.Width = newTrailing;
        }
        else
        {
            leading.Height = newLeading;
            trailing.Height = newTrailing;
            // Force shrink in case inner content reports a higher
            // measured min — without this, panes with substantial
            // content (TabView with tabs + body) refuse to go below
            // an intrinsic min and the splitter "sticks" going up.
            leading.MinHeight = 0;
            trailing.MinHeight = 0;
        }
        Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(leading, 0);
        Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(trailing, 0);
    }

    private void ApplyDirectGrowMutation(double rawDeltaDip)
    {
        if (VTH.GetParent(this) is not Microsoft.UI.Reactor.Layout.FlexPanel panel) return;

        // Locate self in the panel's children list, and grab the leading
        // (idx-1) + trailing (idx+1) siblings.
        int splitterIdx = -1;
        for (int i = 0; i < panel.Children.Count; i++)
        {
            if (ReferenceEquals(panel.Children[i], this)) { splitterIdx = i; break; }
        }
        if (splitterIdx <= 0 || splitterIdx >= panel.Children.Count - 1) return;
        if (panel.Children[splitterIdx - 1] is not FrameworkElement leading) return;
        if (panel.Children[splitterIdx + 1] is not FrameworkElement trailing) return;

        var extent = _direction == DockSplitterDirection.Columns
            ? panel.ActualWidth
            : panel.ActualHeight;
        if (extent < 1) return;

        var leadingGrow = Microsoft.UI.Reactor.Layout.FlexPanel.GetGrow(leading);
        var trailingGrow = Microsoft.UI.Reactor.Layout.FlexPanel.GetGrow(trailing);
        var pair = leadingGrow + trailingGrow;
        if (pair <= 0) return;

        // Read the actual rendered sizes of the pair along the split
        // axis — that's the true DIP budget shared between them, which
        // excludes the splitter handle's 16 DIP slice and any sibling
        // panes (in N-way splits). Using `panel.ActualWidth` directly
        // would smear the splitter handle's width into the pair's share
        // and produce sub-pixel cursor lag during drag.
        var leadingDip = _direction == DockSplitterDirection.Columns
            ? leading.ActualWidth
            : leading.ActualHeight;
        var trailingDip = _direction == DockSplitterDirection.Columns
            ? trailing.ActualWidth
            : trailing.ActualHeight;
        var pairDip = leadingDip + trailingDip;
        if (pairDip < 1) return;

        const double minDip = 60.0;
        var newLeading = Math.Clamp(leadingDip + rawDeltaDip, minDip, pairDip - minDip);
        if (newLeading <= 0 || double.IsNaN(newLeading)) return;
        var newTrailing = pairDip - newLeading;
        if (newTrailing < minDip) return;

        var newLeadingGrow = pair * (newLeading / pairDip);
        var newTrailingGrow = pair - newLeadingGrow;

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
        // Restore panes to grow-based sizing so the host's re-render
        // (triggered by ResizeDelta) lands cleanly via the normal path.
        RestorePairToGrow();
        ResizeDelta?.Invoke(this, new DockSplitterDeltaEventArgs(-cumDelta, _direction, GetHostExtent(), isFinal: true));
        e.Handled = true;
    }

    private void RestorePairToGrow()
    {
        // Convert the inline Width/Height set during the drag back into
        // FlexPanel.Grow values, then clear the inline sizes + the
        // pinned perpendicular-axis Width/Height on the parent panel.
        // This is what lets a subsequent window resize redistribute
        // space (Yoga grow flexes to fill the available extent),
        // while preserving the proportional split the user just
        // settled on.
        //
        // Pre-fix history: this method intentionally left the inline
        // sizes set, on the theory that "cursor-driven sizes ARE the
        // source of truth". That worked for the splitter itself but
        // froze the panes at absolute DIPs — resizing the window
        // afterward left the panel with mismatched extent vs child
        // total. The §2.4 matrix M15 fixture surfaces that regression.
        if (VTH.GetParent(this) is not Microsoft.UI.Reactor.Layout.FlexPanel panel) return;
        int idx = -1;
        for (int i = 0; i < panel.Children.Count; i++)
            if (ReferenceEquals(panel.Children[i], this)) { idx = i; break; }
        if (idx <= 0 || idx >= panel.Children.Count - 1) return;
        if (panel.Children[idx - 1] is not FrameworkElement leading) return;
        if (panel.Children[idx + 1] is not FrameworkElement trailing) return;

        // Compute new grow values from the current measured pair.
        var leadingDip = _direction == DockSplitterDirection.Columns
            ? leading.ActualWidth
            : leading.ActualHeight;
        var trailingDip = _direction == DockSplitterDirection.Columns
            ? trailing.ActualWidth
            : trailing.ActualHeight;
        var pairDip = leadingDip + trailingDip;
        if (pairDip > 0)
        {
            // Use the pair-grow total captured at drag start (so we
            // preserve the same relative weight against any other
            // panes in an N-way split). Falls back to 1.0 when the
            // capture snapshot is unavailable.
            var totalGrow = _pairGrowAtCapture > 0 ? _pairGrowAtCapture : 1.0;
            var newLeadingGrow = totalGrow * (leadingDip / pairDip);
            var newTrailingGrow = totalGrow - newLeadingGrow;
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(leading, newLeadingGrow);
            Microsoft.UI.Reactor.Layout.FlexPanel.SetGrow(trailing, newTrailingGrow);
        }

        // Clear the inline absolute sizes so Yoga's grow distribution
        // resumes on the next layout pass (window resize, DPI change,
        // sibling reflow). Also clear the forced MinHeight=0 we set
        // during the drag to allow shrinking.
        if (_direction == DockSplitterDirection.Columns)
        {
            leading.ClearValue(FrameworkElement.WidthProperty);
            trailing.ClearValue(FrameworkElement.WidthProperty);
        }
        else
        {
            leading.ClearValue(FrameworkElement.HeightProperty);
            trailing.ClearValue(FrameworkElement.HeightProperty);
            leading.ClearValue(FrameworkElement.MinHeightProperty);
            trailing.ClearValue(FrameworkElement.MinHeightProperty);
        }

        // Release the perpendicular-axis pin on the parent panel so
        // window resize affects it again. Set in SnapshotPairAtCapture
        // to keep DesiredSize stable during the drag.
        if (_direction == DockSplitterDirection.Columns)
            panel.ClearValue(FrameworkElement.HeightProperty);
        else
            panel.ClearValue(FrameworkElement.WidthProperty);
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_isCapturing) return;
        _isCapturing = false;
        _capturePointerId = 0;
        ResizeDelta?.Invoke(this, new DockSplitterDeltaEventArgs(0, _direction, GetHostExtent(), isFinal: true));
        if (_handle.Fill is SolidColorBrush brush)
            brush.Color = Color.FromArgb(0x88, 0x80, 0x80, 0x80);
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
    /// inside) and read its measured extent along the split axis. Returns
    /// 0 if the parent isn't available yet (control hasn't been laid out)
    /// — caller treats that as "no delta applied this frame".
    /// </summary>
    internal double GetHostExtent()
    {
        if (VTH.GetParent(this) is not FrameworkElement parent) return 0;
        return _direction == DockSplitterDirection.Columns
            ? parent.ActualWidth
            : parent.ActualHeight;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        double step = KeyboardStep;
        // Direct-mutate path: positive raw delta = grow leading (cursor
        // direction). Right/Down → +step; Left/Up → -step. The fired
        // ResizeDelta event uses the solver convention (negated).
        double rawDelta;
        switch (e.Key)
        {
            case VirtualKey.Left when _direction == DockSplitterDirection.Columns:
                rawDelta = -step; break;
            case VirtualKey.Right when _direction == DockSplitterDirection.Columns:
                rawDelta = step; break;
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
