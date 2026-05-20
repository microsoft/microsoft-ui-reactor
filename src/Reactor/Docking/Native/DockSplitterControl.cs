using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
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
    public DockSplitterDeltaEventArgs(double delta, DockSplitterDirection direction, bool isFinal)
    {
        Delta = delta;
        Direction = direction;
        IsFinal = isFinal;
    }

    /// <summary>Movement in DIPs along the split axis (positive grows the trailing child).</summary>
    public double Delta { get; }

    public DockSplitterDirection Direction { get; }

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

    public event EventHandler<DockSplitterDeltaEventArgs>? ResizeDelta;

    public DockSplitterControl()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Background = new SolidColorBrush(Colors.Transparent);

        _handle = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)),
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
        KeyDown += OnKeyDown;

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
            brush.Color = Color.FromArgb(0x66, 0x80, 0x80, 0x80);
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
            _captureOrigin = pointer.Position;
            _capturePointerId = e.Pointer.PointerId;
            Focus(FocusState.Pointer);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isCapturing || e.Pointer.PointerId != _capturePointerId) return;
        var p = e.GetCurrentPoint(this).Position;
        var delta = _direction == DockSplitterDirection.Columns
            ? p.X - _captureOrigin.X
            : p.Y - _captureOrigin.Y;
        if (delta == 0) return;
        ResizeDelta?.Invoke(this, new DockSplitterDeltaEventArgs(delta, _direction, isFinal: false));
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isCapturing || e.Pointer.PointerId != _capturePointerId) return;
        _isCapturing = false;
        _capturePointerId = 0;
        try { ReleasePointerCapture(e.Pointer); } catch { /* already lost */ }
        ResizeDelta?.Invoke(this, new DockSplitterDeltaEventArgs(0, _direction, isFinal: true));
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_isCapturing) return;
        _isCapturing = false;
        _capturePointerId = 0;
        ResizeDelta?.Invoke(this, new DockSplitterDeltaEventArgs(0, _direction, isFinal: true));
        if (_handle.Fill is SolidColorBrush brush)
            brush.Color = Color.FromArgb(0x33, 0x80, 0x80, 0x80);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        double step = KeyboardStep;
        double delta;
        switch (e.Key)
        {
            case VirtualKey.Left when _direction == DockSplitterDirection.Columns:
                delta = -step; break;
            case VirtualKey.Right when _direction == DockSplitterDirection.Columns:
                delta = step; break;
            case VirtualKey.Up when _direction == DockSplitterDirection.Rows:
                delta = -step; break;
            case VirtualKey.Down when _direction == DockSplitterDirection.Rows:
                delta = step; break;
            default: return;
        }

        ResizeDelta?.Invoke(this, new DockSplitterDeltaEventArgs(delta, _direction, isFinal: true));
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
