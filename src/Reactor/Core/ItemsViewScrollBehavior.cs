using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Microsoft.UI.Reactor.Core;

/// <summary>Control-owned navigation; never retains a render's item indices across a layout pass.</summary>
internal sealed class ItemsViewScrollBehavior : IDisposable
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "ItemsViewScrollBehavior", typeof(object), typeof(ItemsViewScrollBehavior), new PropertyMetadata(null));
    private readonly ItemsView _control;
    private readonly PointerEventHandler _readerInput;
    private ItemsViewElementBase _element;
    private ScrollView? _scroll;
    private double _originalAnchor;
    private bool _following;
    private bool _history;
    private bool _pendingTail;
    private string? _pendingKey;
    private bool _rendering;
    private bool _disposed;
    private int _frames;
    private int _stableFrames;
    private FrameworkElement? _target;
    private FrameworkElement? _bringTarget;
    private string? _targetKey;
    private (double Top, double Height, double Extent, double Viewport)? _geometry;
    private bool _brought;
    private bool _waitingForBring;
    private int _pendingOperation = -1;
    private double _lastCompletedOffset;
    private bool _offsetCorrection;
    private bool _queued;
    private int _version;

    private ItemsViewScrollBehavior(ItemsView control, ItemsViewElementBase element)
    {
        _control = control;
        _readerInput = OnReaderInput;
        _element = element;
        _following = element.IsTailFollowingEnabled;
        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        control.SizeChanged += OnSizeChanged;
        control.PreviewKeyDown += OnKeyDown;
        control.AddHandler(UIElement.PointerWheelChangedEvent, _readerInput, true);
        control.AddHandler(UIElement.PointerPressedEvent, _readerInput, true);
    }

    internal static void Update(ItemsView control, ItemsViewElementBase? previous, ItemsViewElementBase element)
    {
        if (!double.IsFinite(element.TailFollowThreshold) || element.TailFollowThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(element.TailFollowThreshold));
        var state = control.GetValue(StateProperty) as ItemsViewScrollBehavior;
        if (state is null)
        {
            if (!element.IsTailFollowingEnabled && element.ScrollIntoViewKey is null && element.TailScrollRequest == 0) return;
            state = new(control, element);
            control.SetValue(StateProperty, state);
        }
        state._element = element;
        state.AttachScroll();
        if (previous is not null && previous.IsTailFollowingEnabled != element.IsTailFollowingEnabled)
        {
            state.CancelNavigation();
            state.SetFollowing(element.IsTailFollowingEnabled);
        }
        if (previous?.TailScrollRequest != element.TailScrollRequest && element.TailScrollRequest != 0)
        {
            state._history = false;
            state.SetFollowing(element.IsTailFollowingEnabled);
            state.Navigate(tail: true);
        }
        else if (element.ScrollIntoViewKey is not null
            && (previous?.ScrollIntoViewKey != element.ScrollIntoViewKey
                || previous?.ScrollIntoViewRequest != element.ScrollIntoViewRequest))
        {
            state._history = true;
            state.SetFollowing(false);
            state.Navigate(tail: false, element.ScrollIntoViewKey);
        }
        else if (state._following && (previous is null || previous.ItemCount != element.ItemCount
            || element.ItemCount > 0 && previous.GetKeyAt(previous.ItemCount - 1) != element.GetKeyAt(element.ItemCount - 1)))
            state.Navigate(tail: true);
        else if (element.ScrollIntoViewKey is null && state._pendingKey is not null)
            state.CancelNavigation();
    }

    internal static bool IsNearTail(double offset, double scrollableHeight, double threshold)
        => double.IsFinite(offset) && double.IsFinite(scrollableHeight) && scrollableHeight - offset <= threshold;

    internal static bool IsNavigationPending(ItemsView control)
        => control.GetValue(StateProperty) is ItemsViewScrollBehavior { _rendering: true };

    private void AttachScroll()
    {
        if (!_control.IsLoaded || ReferenceEquals(_scroll, _control.ScrollView)) return;
        ReleaseScroll();
        _scroll = _control.ScrollView;
        if (_scroll is null) return;
        _originalAnchor = _scroll.VerticalAnchorRatio;
        ApplyAnchor();
        _scroll.ExtentChanged += OnExtentChanged;
        _scroll.ViewChanged += OnViewChanged;
        _scroll.StateChanged += OnStateChanged;
        _scroll.BringingIntoView += OnBringingIntoView;
        _scroll.ScrollCompleted += OnScrollCompleted;
        _scroll.AnchorRequested += OnAnchorRequested;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        AttachScroll();
        if (_pendingKey is not null) StartRendering();
        else if (_following || _pendingTail) Navigate(tail: true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        CancelNavigation();
        ReleaseScroll();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        AttachScroll();
        if (_following) Navigate(tail: true);
    }

    private void OnExtentChanged(ScrollView sender, object args)
    {
        if (_following && !IsNearTail(sender.VerticalOffset, sender.ScrollableHeight, 1))
            Navigate(tail: true);
    }

    private void OnViewChanged(ScrollView sender, object args)
    {
        if (_pendingTail || _pendingKey is not null || _history) return;
        SetFollowing(_element.IsTailFollowingEnabled
            && IsNearTail(sender.VerticalOffset, sender.ScrollableHeight, _element.TailFollowThreshold));
    }

    private void OnStateChanged(ScrollView sender, object args)
    {
        if (sender.State == ScrollingInteractionState.Interaction) ReaderNavigation();
        else if (sender.State == ScrollingInteractionState.Idle) OnViewChanged(sender, args);
    }

    private void OnBringingIntoView(ScrollView sender, ScrollingBringingIntoViewEventArgs args)
    {
        if (ReferenceEquals(args.RequestEventArgs.TargetElement, _bringTarget))
        {
            _waitingForBring = false;
            _pendingOperation = args.CorrelationId;
            _bringTarget = null;
            return;
        }

        if (_following && !HasReaderFocus())
            args.Cancel = true;
        else if (args.RequestEventArgs.TargetElement is Control { FocusState: FocusState.Keyboard })
            ReaderNavigation();
    }

    private bool HasReaderFocus()
    {
        if (_control.XamlRoot is not { } root) return false;
        for (var focused = FocusManager.GetFocusedElement(root) as DependencyObject;
            focused is not null; focused = VisualTreeHelper.GetParent(focused))
            if (ReferenceEquals(focused, _control)) return true;
        return false;
    }

    private void OnAnchorRequested(ScrollView sender, ScrollingAnchorRequestedEventArgs args)
    {
        if (_offsetCorrection && _target is { IsLoaded: true, Visibility: Visibility.Visible }
            && sender.Content is ItemsRepeater repeater
            && repeater.GetElementIndex(_target) == _element.ItemCount - 1
            && _targetKey == _element.GetKeyAt(_element.ItemCount - 1))
            args.AnchorElement = _target;
    }

    private void OnScrollCompleted(ScrollView sender, ScrollingScrollCompletedEventArgs args)
    {
        if (args.CorrelationId == _pendingOperation)
        {
            if (_pendingTail && sender.VerticalOffset > _lastCompletedOffset + 1)
            {
                _frames = 0;
                _lastCompletedOffset = sender.VerticalOffset;
            }
            _pendingOperation = -1;
            _stableFrames = 0;
            _geometry = null;
        }
    }

    private void OnReaderInput(object sender, PointerRoutedEventArgs args) => ReaderNavigation();

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.PageUp or VirtualKey.PageDown
            or VirtualKey.Home or VirtualKey.End or VirtualKey.Space)
            ReaderNavigation();
    }

    private void ReaderNavigation()
    {
        _history = false;
        CancelNavigation();
        SetFollowing(false);
    }

    private void SetFollowing(bool following)
    {
        if (_following == following) return;
        _following = following;
        ApplyAnchor();
        _element.OnTailFollowingChanged?.Invoke(following);
    }

    private void ApplyAnchor()
    {
        // Exactly 1 enables WinUI's extent-edge anchoring, which fights virtual row remeasurement.
        if (_scroll is not null) _scroll.VerticalAnchorRatio = _following ? Math.BitDecrement(1.0) : _originalAnchor;
    }

    private void Navigate(bool tail, string? key = null)
    {
        if (_disposed) return;
        if (_pendingTail != tail || _pendingKey != key)
        {
            CancelNavigation();
            _pendingTail = tail;
            _pendingKey = key;
            _lastCompletedOffset = _scroll?.VerticalOffset ?? 0;
        }
        if (_control.IsLoaded) StartRendering();
    }

    private void StartRendering()
    {
        if (_rendering) return;
        _frames = 0;
        _rendering = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, object args)
    {
        AttachScroll();
        if (++_frames > 120)
        {
            CancelNavigation();
            SetFollowing(false);
            DiagnosticLog.Warning(LogCategory.Reactor, "ItemsView.Scroll", "Item navigation did not settle within 120 frames.");
            return;
        }
        if (_disposed || !_control.IsLoaded || _element.ItemCount == 0)
        {
            CancelNavigation();
            return;
        }
        if (_scroll is not { IsLoaded: true, ViewportHeight: > 0 } scroll
            || scroll.State != ScrollingInteractionState.Idle
            || scroll.Content is not ItemsRepeater repeater) return;
        if (_waitingForBring || _pendingOperation >= 0 || _queued) return;

        var index = _pendingTail ? _element.ItemCount - 1 : FindIndex(_pendingKey);
        if (index < 0)
        {
            CancelNavigation();
            DiagnosticLog.Warning(LogCategory.Reactor, "ItemsView.Scroll", "The requested item key is no longer in the source.");
            return;
        }
        var key = _element.GetKeyAt(index);
        if (_target is null || _targetKey != key)
        {
            QueueNative(() =>
            {
                if (!IsCurrent(index, key)) return;
                _target = repeater.GetOrCreateElement(index) as FrameworkElement;
                _targetKey = key;
                _geometry = null;
                _stableFrames = 0;
                _brought = false;
            });
            return;
        }
        if (_target is not { IsLoaded: true, ActualHeight: > 0 } || repeater.GetElementIndex(_target) != index)
            return;
        var bounds = _target.TransformToVisual(scroll).TransformBounds(
            new global::Windows.Foundation.Rect(0, 0, _target.ActualWidth, _target.ActualHeight));
        var geometry = (bounds.Top, bounds.Height, scroll.ExtentHeight, scroll.ViewportHeight);
        _stableFrames = _geometry == geometry ? _stableFrames + 1 : 0;
        _geometry = geometry;
        if (_stableFrames < 4) return;

        if (!_brought)
        {
            QueueNative(() =>
            {
                if (!IsCurrent(index, key) || _target is null || repeater.GetElementIndex(_target) != index) return;
                _brought = true;
                _waitingForBring = true;
                _bringTarget = _target;
                _target.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = false,
                    VerticalAlignmentRatio = _pendingTail ? 1 : 0,
                });
            });
            return;
        }

        var targetOffset = _pendingTail ? scroll.ScrollableHeight
            : Math.Clamp(scroll.VerticalOffset + bounds.Top, 0, scroll.ScrollableHeight);
        if (Math.Abs(targetOffset - scroll.VerticalOffset) > 1)
        {
            _stableFrames = 0;
            QueueNative(() =>
            {
                if (!IsCurrent(index, key)) return;
                if (_pendingTail)
                {
                    // Pin the realized tail, not the estimated extent edge, while rows remeasure.
                    _offsetCorrection = true;
                    scroll.VerticalAnchorRatio = 0.5;
                }
                _pendingOperation = scroll.ScrollTo(scroll.HorizontalOffset,
                    _pendingTail ? scroll.ScrollableHeight : targetOffset,
                    new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
            });
            return;
        }
        CancelNavigation();
    }

    private bool IsCurrent(int index, string key) => (uint)index < (uint)_element.ItemCount
        && _element.GetKeyAt(index) == key && (!_pendingTail || index == _element.ItemCount - 1);

    private void QueueNative(Action action)
    {
        _queued = true;
        var version = _version;
        // Native realization/bring must not force layout from CompositionTarget.Rendering.
        if (_control.DispatcherQueue.TryEnqueue(() =>
        {
            if (version != _version || _disposed || !_control.IsLoaded) return;
            _queued = false;
            action();
        })) return;
        CancelNavigation();
        SetFollowing(false);
        DiagnosticLog.Warning(LogCategory.Reactor, "ItemsView.Scroll", "Item navigation could not be dispatched.");
    }

    private int FindIndex(string? key)
    {
        for (var index = 0; index < _element.ItemCount; index++)
            if (_element.GetKeyAt(index) == key) return index;
        return -1;
    }

    private void CancelNavigation()
    {
        _version++;
        _queued = false;
        _offsetCorrection = false;
        if (_rendering) CompositionTarget.Rendering -= OnRendering;
        _rendering = false;
        _pendingTail = false;
        _pendingKey = null;
        _target = null;
        _bringTarget = null;
        _targetKey = null;
        _geometry = null;
        _stableFrames = 0;
        _brought = false;
        _waitingForBring = false;
        _pendingOperation = -1;
        ApplyAnchor();
    }

    private void ReleaseScroll()
    {
        if (_scroll is null) return;
        _scroll.ExtentChanged -= OnExtentChanged;
        _scroll.ViewChanged -= OnViewChanged;
        _scroll.StateChanged -= OnStateChanged;
        _scroll.BringingIntoView -= OnBringingIntoView;
        _scroll.ScrollCompleted -= OnScrollCompleted;
        _scroll.AnchorRequested -= OnAnchorRequested;
        _scroll.VerticalAnchorRatio = _originalAnchor;
        _scroll = null;
    }

    internal static void Detach(ItemsView control)
    {
        (control.GetValue(StateProperty) as ItemsViewScrollBehavior)?.Dispose();
        control.ClearValue(StateProperty);
    }

    public void Dispose()
    {
        _disposed = true;
        CancelNavigation();
        ReleaseScroll();
        _control.Loaded -= OnLoaded;
        _control.Unloaded -= OnUnloaded;
        _control.SizeChanged -= OnSizeChanged;
        _control.PreviewKeyDown -= OnKeyDown;
        _control.RemoveHandler(UIElement.PointerWheelChangedEvent, _readerInput);
        _control.RemoveHandler(UIElement.PointerPressedEvent, _readerInput);
    }
}
