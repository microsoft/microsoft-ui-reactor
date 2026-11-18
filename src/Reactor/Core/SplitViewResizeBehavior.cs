using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Microsoft.UI.Reactor.Core;

internal sealed partial class SplitViewResizeBehavior : IDisposable
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "SplitViewResizeBehavior", typeof(object), typeof(SplitViewResizeBehavior), new PropertyMetadata(null));
    private readonly SplitView _control;
    private readonly Grid _pane;
    private readonly ContentPresenter _content = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ResizeHandle _handle = new();
    private readonly PointerEventHandler _pressed;
    private readonly long _placementToken;
    private readonly long _directionToken;
    private readonly long _lengthToken;
    private readonly long _openToken;
    private PaneResizeOptions _options;
    private Action<double>? _changed;
    private double? _origin;
    private double _startLength;

    private sealed partial class ResizeHandle : Button
    {
        internal ResizeHandle()
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
            Content = "\u22EE";
            Width = 24;
            MinWidth = 0;
            Padding = new Thickness(0);
            VerticalAlignment = VerticalAlignment.Stretch;
            UseSystemFocusVisuals = true;
            ManipulationMode = ManipulationModes.None;
        }
    }

    private SplitViewResizeBehavior(SplitView control, PaneResizeOptions options)
    {
        _control = control;
        _options = options;
        _pressed = OnPressed;
        _pane = new Grid { FlowDirection = FlowDirection.LeftToRight };
        _pane.ColumnDefinitions.Add(new ColumnDefinition());
        _pane.ColumnDefinitions.Add(new ColumnDefinition());
        _pane.Children.Add(_content);
        _pane.Children.Add(_handle);
        _handle.AddHandler(UIElement.PointerPressedEvent, _pressed, true);
        _handle.PointerMoved += OnMoved;
        _handle.PointerReleased += OnReleased;
        _handle.PointerCaptureLost += OnCaptureLost;
        _handle.KeyDown += OnKeyDown;
        _control.SizeChanged += OnSizeChanged;
        _control.Unloaded += OnUnloaded;
        _placementToken = control.RegisterPropertyChangedCallback(SplitView.PanePlacementProperty, OnGeometryChanged);
        _directionToken = control.RegisterPropertyChangedCallback(FrameworkElement.FlowDirectionProperty, OnGeometryChanged);
        _lengthToken = control.RegisterPropertyChangedCallback(SplitView.OpenPaneLengthProperty, OnGeometryChanged);
        _openToken = control.RegisterPropertyChangedCallback(SplitView.IsPaneOpenProperty, OnGeometryChanged);
        var child = control.Pane as UIElement;
        control.Pane = null;
        _content.Content = child;
        control.Pane = _pane;
    }

    internal static void Update(SplitView control, SplitViewElement element)
    {
        if (element.PaneResize is not { } options)
        {
            Detach(control);
            return;
        }
        Validate(options);
        var state = control.GetValue(StateProperty) as SplitViewResizeBehavior;
        if (state is null)
        {
            state = new(control, options);
            control.SetValue(StateProperty, state);
        }
        state._options = options;
        state._changed = element.OnPaneLengthChanged;
        AutomationProperties.SetName(state._handle, options.AutomationName);
        ToolTipService.SetToolTip(state._handle, options.AutomationName);
        state.ApplyGeometry();
    }

    internal static void Validate(PaneResizeOptions options)
    {
        if (!double.IsFinite(options.Minimum) || !double.IsFinite(options.Maximum)
            || options.Minimum < 0 || options.Maximum < options.Minimum
            || !double.IsFinite(options.KeyboardStep) || options.KeyboardStep <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Pane bounds must be finite, ordered, and nonnegative; the keyboard step must be positive.");
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AutomationName);
    }

    internal static double Constrain(double value, PaneResizeOptions options, double viewportWidth)
    {
        var maximum = viewportWidth > 0 ? Math.Min(options.Maximum, Math.Max(0, viewportWidth - 48)) : options.Maximum;
        return Math.Clamp(value, Math.Min(options.Minimum, maximum), maximum);
    }

    private bool IsRight => (_control.PanePlacement == SplitViewPanePlacement.Right)
        != (_control.FlowDirection == FlowDirection.RightToLeft);

    private void ApplyGeometry()
    {
        _handle.Visibility = _control.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!_control.IsPaneOpen)
        {
            _origin = null;
            _handle.ReleasePointerCaptures();
        }
        var handleColumn = IsRight ? 0 : 1;
        Grid.SetColumn(_handle, handleColumn);
        Grid.SetColumn(_content, 1 - handleColumn);
        _pane.ColumnDefinitions[handleColumn].Width = GridLength.Auto;
        _pane.ColumnDefinitions[1 - handleColumn].Width = new GridLength(1, GridUnitType.Star);
        _content.FlowDirection = _control.FlowDirection;
        var length = Constrain(_control.OpenPaneLength, _options, _control.ActualWidth);
        if (_control.OpenPaneLength != length) _control.OpenPaneLength = length;
        AutomationProperties.SetItemStatus(_handle, $"{length:0} pixels");
    }

    private void OnGeometryChanged(DependencyObject sender, DependencyProperty property) => ApplyGeometry();
    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ApplyGeometry();
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _origin = null;
        _handle.ReleasePointerCaptures();
    }

    private void OnPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!_control.IsPaneOpen || !args.GetCurrentPoint(_handle).Properties.IsLeftButtonPressed) return;
        _startLength = _control.OpenPaneLength;
        _origin = args.GetCurrentPoint(_control).Position.X;
        if (!_handle.CapturePointer(args.Pointer)) _origin = null;
        _handle.Focus(FocusState.Pointer);
        args.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_origin is not { } origin) return;
        Resize(_startLength + (IsRight ? -1 : 1) * (args.GetCurrentPoint(_control).Position.X - origin));
        args.Handled = true;
    }

    private void OnReleased(object sender, PointerRoutedEventArgs args)
    {
        _origin = null;
        _handle.ReleasePointerCapture(args.Pointer);
    }

    private void OnCaptureLost(object sender, PointerRoutedEventArgs args) => _origin = null;

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || !_control.IsPaneOpen) return;
        var direction = IsRight ? -1 : 1;
        var length = args.Key switch
        {
            VirtualKey.Left => _control.OpenPaneLength - direction * _options.KeyboardStep,
            VirtualKey.Right => _control.OpenPaneLength + direction * _options.KeyboardStep,
            VirtualKey.Home => _options.Minimum,
            VirtualKey.End => _options.Maximum,
            _ => (double?)null,
        };
        if (length is null) return;
        args.Handled = true;
        Resize(length.Value);
    }

    internal void Resize(double length)
    {
        length = Constrain(length, _options, _control.ActualWidth);
        if (_control.OpenPaneLength == length) return;
        _control.OpenPaneLength = length;
        _changed?.Invoke(length);
    }

    internal static void ResizePane(SplitView control, double length)
    {
        if (control.GetValue(StateProperty) is SplitViewResizeBehavior state)
            state.Resize(length);
    }

    internal static UIElement? GetPane(SplitView control)
        => control.GetValue(StateProperty) is SplitViewResizeBehavior state
            ? state._content.Content as UIElement : control.Pane as UIElement;

    internal static void SetPane(SplitView control, UIElement? child)
    {
        if (control.GetValue(StateProperty) is SplitViewResizeBehavior state) state._content.Content = child;
        else control.Pane = child;
    }

    internal static void Detach(SplitView control)
    {
        if (control.GetValue(StateProperty) is not SplitViewResizeBehavior state) return;
        state.Dispose();
        control.ClearValue(StateProperty);
    }

    public void Dispose()
    {
        _origin = null;
        _changed = null;
        _handle.ReleasePointerCaptures();
        _handle.RemoveHandler(UIElement.PointerPressedEvent, _pressed);
        _handle.PointerMoved -= OnMoved;
        _handle.PointerReleased -= OnReleased;
        _handle.PointerCaptureLost -= OnCaptureLost;
        _handle.KeyDown -= OnKeyDown;
        _control.SizeChanged -= OnSizeChanged;
        _control.Unloaded -= OnUnloaded;
        _control.UnregisterPropertyChangedCallback(SplitView.PanePlacementProperty, _placementToken);
        _control.UnregisterPropertyChangedCallback(FrameworkElement.FlowDirectionProperty, _directionToken);
        _control.UnregisterPropertyChangedCallback(SplitView.OpenPaneLengthProperty, _lengthToken);
        _control.UnregisterPropertyChangedCallback(SplitView.IsPaneOpenProperty, _openToken);
        var child = _content.Content as UIElement;
        _content.Content = null;
        _control.Pane = child;
    }
}
