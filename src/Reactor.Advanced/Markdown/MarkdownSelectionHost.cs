using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Microsoft.UI.Reactor.Markdown;

internal sealed partial class MarkdownSelectionHost : ContentControl
{
    private readonly Grid _root = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly TextBlock _selectionDefaults = new() { Visibility = Visibility.Collapsed };
    private readonly List<MarkdownSelectionMarker> _surfaces = [];
    private readonly List<MarkdownSelectionMarker> _membership = [];
    private readonly HashSet<UIElement> _visibilityNodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<UIElement, long> _visibilitySubscriptions = new(ReferenceEqualityComparer.Instance);
    private readonly List<UIElement> _staleVisibilityNodes = [];
    private readonly List<Rect> _selectionRectangles = [];
    private readonly List<(double Score, int Index)> _hitCandidates = [];
    private readonly Dictionary<int, Rect> _characterRects = [];
    private readonly Dictionary<(int Offset, int Next), Rect> _glyphRects = [];
    private readonly Dictionary<MarkdownSelectionMarker, Rect> _surfaceBounds = [];
    private readonly HashSet<FrameworkElement> _layoutOwners = [];
    private readonly Dictionary<MarkdownSelectionMarker, TextSegment[]> _segments = [];
    private readonly Dictionary<FrameworkElement, GeneralTransform> _transforms = [];
    private readonly DispatcherTimer _dragTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _clickTimer = new();
    private readonly PointerEventHandler _pressed;
    private readonly PointerEventHandler _moved;
    private readonly PointerEventHandler _released;
    private readonly KeyEventHandler _keyDown;
    private readonly DependencyPropertyChangedCallback _visibilityChanged;
    private Pointer? _pointer;
    private Point _dragPoint;
    private Point _rootDragPoint;
    private Point _pressPoint;
    private UIElement? _nativePressOwner;
    private MarkdownClickSequence _clicks;
    private MarkdownSelectionUnit _dragUnit;
    private MarkdownSelectionSpan _pressSpan;
    private int _pressLocalGlyph;
    private MarkdownSelectionDocument.Entry? _pressEntry;
    private bool _dragged;
    private bool _documentDirty = true;
    private int _layoutSignature;
    private Rect _viewport;
    private bool _refreshing;
    private bool _detached;
    private bool _loaded;
    private bool _trackingLayout;
    private bool _refreshPending;
    private bool _suppressCaret;
    private MarkdownSelectionAutomationPeer? _automationPeer;
    private XamlRoot? _clickAwayRoot;

    internal MarkdownTextSelectionOptions Options { get; set; } = null!;
    internal UIElement? Body { get; private set; }
    internal MarkdownSelectionDocument Document { get; } = new();
    internal bool IsDocumentDirty => _documentDirty;
    internal bool IsTrackingLayout => _trackingLayout;
    internal bool HasPendingNativePress => _nativePressOwner is not null;
    internal int CachedCharacterCount => _characterRects.Count;
    internal int LayoutPassCount { get; private set; }
    internal int MembershipCheckCount { get; private set; }
    internal int SegmentReadCount { get; private set; }
    internal int TransformCount { get; private set; }

    internal MarkdownSelectionHost()
    {
        IsTabStop = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Content = _root;
        _root.Children.Add(_overlay);
        _root.Children.Add(_selectionDefaults);
        _pressed = OnPressed;
        _moved = OnMoved;
        _released = OnReleased;
        _keyDown = OnKeyDown;
        _visibilityChanged = OnVisibilityChanged;
        AddHandler(PointerPressedEvent, _pressed, true);
        AddHandler(PointerMovedEvent, _moved, true);
        AddHandler(PointerReleasedEvent, _released, true);
        AddHandler(KeyDownEvent, _keyDown, true);
        PointerCaptureLost += OnCaptureLost;
        PointerCanceled += OnPointerCanceled;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnThemeChanged;
        GotFocus += OnFocusChanged;
        LostFocus += OnFocusChanged;
        _dragTimer.Tick += OnDragTick;
        _clickTimer.Tick += OnClickTimeout;
        Document.Changed = OnDocumentChanged;
    }

    internal void InvalidateDocument()
    {
        _documentDirty = true;
        if (_loaded && !_trackingLayout && !_refreshPending && _automationPeer is not null
            && AutomationPeer.ListenerExists(AutomationEvents.TextPatternOnTextChanged))
        {
            _refreshPending = true;
            UpdateLayoutTracking();
            // LayoutUpdated only fires after a layout pass. Semantic-only changes
            // (a Visibility gate, surface text or separator) don't schedule one.
            InvalidateArrange();
        }
    }

    private void UpdateLayoutTracking()
    {
        // LayoutUpdated is window-wide, not subtree-scoped. Idle messages must
        // unsubscribe, rather than scan native text merely to discover no change.
        bool track = !_detached && _loaded && (_refreshPending || _pointer is not null
            || _nativePressOwner is not null || Document.Start != Document.End || HasVisibleCaret);
        if (track == _trackingLayout) return;
        _trackingLayout = track;
        if (track) LayoutUpdated += OnLayout;
        else LayoutUpdated -= OnLayout;
    }

    private bool HasVisibleCaret => Document.Start == Document.End && !_suppressCaret &&
        FocusState != FocusState.Unfocused && Document.Text.Length > 0;

    private void OnFocusChanged(object sender, RoutedEventArgs e)
    {
        if (FocusState != FocusState.Unfocused)
        {
            _suppressCaret = false;
            Refresh();
        }
        else DrawSelection();
        UpdateLayoutTracking();
    }

    private void OnDocumentChanged(bool text, bool selection)
    {
        UpdateLayoutTracking();
        UpdateClickAwayRegistration();
        _automationPeer?.NotifyChanged(text, selection);
    }

    private void OnVisibilityChanged(DependencyObject sender, DependencyProperty property)
    {
        if (property == UIElement.VisibilityProperty) InvalidateDocument();
    }

    private void UpdateClickAwayRegistration()
    {
        var root = _loaded ? XamlRoot : null;
        if (!ReferenceEquals(root, _clickAwayRoot))
        {
            if (_clickAwayRoot is { } previous) MarkdownSelectionClickAway.SetActive(previous, this, false);
            _clickAwayRoot = root;
        }
        if (root is not null)
            MarkdownSelectionClickAway.SetActive(root, this, Document.Start != Document.End || _clicks.IsPending);
    }

    private void OnClickTimeout(object? sender, object e) => ResetClickSequence();

    private void ArmClickTimeout()
    {
        _clickTimer.Stop();
        _clickTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, GetDoubleClickTime()));
        _clickTimer.Start();
    }

    private void ResetClickSequence()
    {
        _clickTimer.Stop();
        _clicks.Reset();
        UpdateClickAwayRegistration();
    }

    internal void SetContent(UIElement? child)
    {
        if (ReferenceEquals(Body, child)) return;
        if (Body is not null) _root.Children.Remove(Body);
        Body = child;
        InvalidateDocument();
        if (child is not null) _root.Children.Insert(0, child);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_detached) return;
        _loaded = true;
        Refresh();
        UpdateLayoutTracking();
        UpdateClickAwayRegistration();
    }
    private void OnLayout(object? sender, object e)
    {
        if (_detached || !IsLoaded) return;
        LayoutPassCount++;
        _refreshPending = false;
        _transforms.Clear();
        _surfaceBounds.Clear();
        CheckMembership();
        bool nativeTextChanged = InvalidateNativeSegments();
        int signature = LayoutSignature();
        var viewport = Viewport();
        if (!_documentDirty && !nativeTextChanged && signature == _layoutSignature && viewport == _viewport)
        {
            UpdateLayoutTracking();
            return;
        }
        Refresh(membershipChecked: true, nativeTextChanged, signature);
        if (_pointer is not null && _dragged)
        {
            _dragPoint = LocalPoint(_rootDragPoint);
            UpdateDragSelection();
            DrawSelection();
        }
        UpdateLayoutTracking();
    }

    private int LayoutSignature()
    {
        var hash = new HashCode();
        hash.Add(XamlRoot?.RasterizationScale);
        _layoutOwners.Clear();
        foreach (var surface in _surfaces)
        {
            hash.Add(SurfaceBounds(surface));
            foreach (var segment in Segments(surface))
            {
                if (!_layoutOwners.Add(segment.Owner)) continue;
                hash.Add(Bounds(segment.Owner));
                if (segment.Owner is RichTextBlock rich)
                {
                    hash.Add(rich.FontSize);
                    hash.Add(rich.FontFamily.Source);
                    hash.Add(rich.FontWeight);
                    hash.Add(rich.FontStyle);
                    hash.Add(rich.FontStretch);
                    hash.Add(rich.CharacterSpacing);
                    hash.Add(rich.TextAlignment);
                    hash.Add(rich.Padding);
                    hash.Add(rich.IsTextScaleFactorEnabled);
                    hash.Add(rich.LineHeight);
                    hash.Add(rich.FlowDirection);
                }
                else if (segment.Owner is TextBlock text)
                {
                    hash.Add(text.FontSize);
                    hash.Add(text.FontFamily.Source);
                    hash.Add(text.FontWeight);
                    hash.Add(text.FontStyle);
                    hash.Add(text.FontStretch);
                    hash.Add(text.CharacterSpacing);
                    hash.Add(text.TextAlignment);
                    hash.Add(text.Padding);
                    hash.Add(text.IsTextScaleFactorEnabled);
                    hash.Add(text.LineHeight);
                    hash.Add(text.FlowDirection);
                }
            }
        }
        return hash.ToHashCode();
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        _refreshPending = false;
        UpdateLayoutTracking();
        ResetClickSequence();
        StopDrag();
        _surfaces.Clear();
        _characterRects.Clear();
        _glyphRects.Clear();
        _surfaceBounds.Clear();
        ClearVisibilitySubscriptions();
        _layoutOwners.Clear();
        _segments.Clear();
        _transforms.Clear();
        _documentDirty = true;
        Document.Rebuild([]);
        _overlay.Children.Clear();
    }

    internal void Detach()
    {
        _detached = true;
        OnUnloaded(this, new RoutedEventArgs());
        RemoveHandler(PointerPressedEvent, _pressed);
        RemoveHandler(PointerMovedEvent, _moved);
        RemoveHandler(PointerReleasedEvent, _released);
        RemoveHandler(KeyDownEvent, _keyDown);
        PointerCaptureLost -= OnCaptureLost;
        PointerCanceled -= OnPointerCanceled;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        ActualThemeChanged -= OnThemeChanged;
        GotFocus -= OnFocusChanged;
        LostFocus -= OnFocusChanged;
        LayoutUpdated -= OnLayout;
        _dragTimer.Tick -= OnDragTick;
        _clickTimer.Tick -= OnClickTimeout;
    }

    private void CheckMembership()
    {
        MembershipCheckCount++;
        _membership.Clear();
        _visibilityNodes.Clear();
        if (Body is not null) Visit(Body, _membership, ancestorsVisible: true);
        UpdateVisibilitySubscriptions();
        if (!_membership.SequenceEqual(_surfaces, ReferenceEqualityComparer.Instance))
            _documentDirty = true;
    }

    internal void Refresh() => Refresh(membershipChecked: false);

    private void Refresh(bool membershipChecked, bool nativeTextChanged = false, int? signature = null)
    {
        if (_detached || _refreshing || !IsLoaded) return;
        _refreshing = true;
        try
        {
            if (!membershipChecked)
            {
                _transforms.Clear();
                _surfaceBounds.Clear();
                CheckMembership();
            }
            bool geometryChanged = _documentDirty;
            if (_documentDirty)
            {
                _documentDirty = false;
                _surfaces.Clear();
                _segments.Clear();
                _surfaces.AddRange(_membership);
                bool documentChanged = Document.Rebuild(_surfaces.Select(s =>
                    new SelectionPart(s, s.Metadata.Text, s.Metadata.SeparatorAfter)));
                if (documentChanged)
                {
                    if (_clicks.Surface is { } clickSurface &&
                        !_surfaces.Any(surface => ReferenceEquals(surface, clickSurface)))
                        ResetClickSequence();
                    if (_pressEntry is { } previous)
                    {
                        var pressedEntry = Document.Entries.FirstOrDefault(entry =>
                            ReferenceEquals(entry.Identity, previous.Identity));
                        if (pressedEntry is null) StopDrag();
                        else
                        {
                            if (_dragUnit == MarkdownSelectionUnit.Word)
                                _pressLocalGlyph = MarkdownSelectionDocument.RemapLocalOffset(
                                    _pressLocalGlyph, previous.Text, pressedEntry.Text, followsInsertion: true);
                            _pressEntry = pressedEntry;
                            _pressSpan = _dragUnit == MarkdownSelectionUnit.Character
                                ? new(Document.Anchor, Document.Anchor)
                                : Document.UnitAt(_dragUnit, pressedEntry.Start + _pressLocalGlyph, pressedEntry);
                        }
                    }
                }
            }
            else if (!membershipChecked) nativeTextChanged = InvalidateNativeSegments();
            int currentSignature = signature ?? LayoutSignature();
            if (geometryChanged || nativeTextChanged || currentSignature != _layoutSignature)
            {
                _characterRects.Clear();
                _glyphRects.Clear();
            }
            _layoutSignature = currentSignature;
            _viewport = Viewport();
            DrawSelection();
        }
        finally { _refreshing = false; }
    }

    private bool Visit(DependencyObject node, List<MarkdownSelectionMarker> target, bool ancestorsVisible)
    {
        // Registration is semantic, not viewport-based. Fully clipped blocks remain
        // part of the document so Down/PageDown and UIA can reach them.
        if (node is MarkdownSelectionHost) return false;
        if (node is MarkdownSelectionMarker marker)
        {
            if (marker.Metadata.Role == SelectionRole.Visibility && !marker.Metadata.IsVisible) return false;
            if (marker.Metadata.Role == SelectionRole.Surface)
            {
                bool visible = ancestorsVisible && marker.Visibility != Visibility.Collapsed;
                if (visible) target.Add(marker);
                _visibilityNodes.Add(marker);
                return true;
            }
        }

        bool childAncestorsVisible = ancestorsVisible &&
            node is not UIElement { Visibility: Visibility.Collapsed };
        bool containsSurface = false;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            containsSurface |= Visit(VisualTreeHelper.GetChild(node, i), target, childAncestorsVisible);
        if (containsSurface && node is UIElement element) _visibilityNodes.Add(element);
        return containsSurface;
    }

    private void UpdateVisibilitySubscriptions()
    {
        _staleVisibilityNodes.Clear();
        foreach (var element in _visibilitySubscriptions.Keys)
        {
            if (!_visibilityNodes.Contains(element)) _staleVisibilityNodes.Add(element);
        }
        foreach (var element in _staleVisibilityNodes)
        {
            element.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, _visibilitySubscriptions[element]);
            _visibilitySubscriptions.Remove(element);
        }
        foreach (var element in _visibilityNodes)
        {
            if (_visibilitySubscriptions.ContainsKey(element)) continue;
            long token = element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, _visibilityChanged);
            _visibilitySubscriptions.Add(element, token);
        }
    }

    private void ClearVisibilitySubscriptions()
    {
        foreach (var (element, token) in _visibilitySubscriptions)
            element.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, token);
        _visibilitySubscriptions.Clear();
        _visibilityNodes.Clear();
    }

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    internal Rect Bounds(FrameworkElement element)
    {
        try
        {
            TransformCount++;
            return element.TransformToVisual(this)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        catch (ArgumentException) { return default; }
    }

    private GeneralTransform Transform(FrameworkElement element)
    {
        if (!_transforms.TryGetValue(element, out var transform))
        {
            TransformCount++;
            _transforms[element] = transform = element.TransformToVisual(this);
        }
        return transform;
    }

    private Rect SurfaceBounds(MarkdownSelectionMarker surface)
    {
        if (_surfaceBounds.TryGetValue(surface, out var bounds)) return bounds;
        return _surfaceBounds[surface] = Bounds(surface);
    }

    internal Rect Viewport()
        => Intersect(new Rect(0, 0, ActualWidth, ActualHeight), ScrollViewport());

    internal Rect ScrollViewport()
    {
        Rect? rect = null;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent is not null;
            parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollView or ScrollViewer && parent is FrameworkElement viewport)
                rect = rect is { } clipped ? Intersect(clipped, Bounds(viewport)) : Bounds(viewport);
        }
        return rect ?? new Rect(0, 0, ActualWidth, ActualHeight);
    }

    internal static Rect Intersect(Rect a, Rect b)
    {
        double x = Math.Max(a.Left, b.Left), y = Math.Max(a.Top, b.Top);
        return new Rect(x, y, Math.Max(0, Math.Min(a.Right, b.Right) - x),
            Math.Max(0, Math.Min(a.Bottom, b.Bottom) - y));
    }

    private static bool Contains(Rect r, Point p) =>
        r.Width > 0 && r.Height > 0 && p.X >= r.Left && p.X <= r.Right && p.Y >= r.Top && p.Y <= r.Bottom;

    private bool IsChrome(object source, out ButtonBase? linkButton)
    {
        linkButton = null;
        if (source is Hyperlink) return false;
        ButtonBase? button = null;
        for (var node = source as DependencyObject; node is not null && !ReferenceEquals(node, this);
            node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBox or PasswordBox or Selector or ScrollBar) return true;
            if (node is ButtonBase candidate) button ??= candidate;
            if (button is not null && node is MarkdownSelectionMarker { Metadata.IsLink: true } &&
                button.ClickMode == ClickMode.Release)
            {
                linkButton = button;
                return false;
            }
        }
        return button is not null;
    }

    internal bool OwnsInput(object source)
    {
        if (source is Hyperlink hyperlink)
        {
            Refresh();
            for (var node = (DependencyObject)hyperlink; node is not null && !ReferenceEquals(node, this);
                node = VisualTreeHelper.GetParent(node))
            {
                if (node is not MarkdownSelectionMarker { Metadata.Role: SelectionRole.Surface } surface) continue;
                return _surfaces.Contains(surface) &&
                    Segments(surface).Any(segment => ReferenceEquals(segment.Link, hyperlink));
            }
            return false;
        }
        for (var node = source as DependencyObject; node is not null;
            node = VisualTreeHelper.GetParent(node))
            if (node is MarkdownSelectionHost scope) return ReferenceEquals(scope, this);
        return false;
    }

    internal Point RootPoint(Point local) => TransformToVisual(null).TransformPoint(local);
    internal Point LocalPoint(Point root) => TransformToVisual(null).Inverse.TransformPoint(root);

    [LibraryImport("user32.dll")]
    private static partial uint GetDoubleClickTime();

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsMouseSelection(e.Pointer.PointerDeviceType)) return;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            ResetClickSequence();
            return;
        }
        Refresh();
        StopDrag();
        if (!OwnsInput(e.OriginalSource) || IsChrome(e.OriginalSource, out var linkButton) ||
            !Contains(Viewport(), point.Position))
        {
            ResetClickSequence();
            return;
        }
        _dragPoint = point.Position;
        int offset = HitTest(_dragPoint, out var entry, out int glyphOffset, requireInsideSurface: true);
        if (entry?.Identity is not MarkdownSelectionMarker surface)
        {
            ResetClickSequence();
            return;
        }

        _pressPoint = _rootDragPoint = RootPoint(_dragPoint);
        _suppressCaret = false;
        bool shift = (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;
        double scale = XamlRoot?.RasterizationScale ?? 1;
        uint dpi = (uint)Math.Round(96 * scale);
        // SM_CXDOUBLECLK / SM_CYDOUBLECLK describe the full rectangle, centered on the last press.
        int count;
        if (shift)
        {
            ResetClickSequence();
            count = 1;
        }
        else
        {
            count = _clicks.Press(surface, point.Timestamp,
                _pressPoint.X * scale, _pressPoint.Y * scale, GetDoubleClickTime() * 1_000UL,
                GetSystemMetricsForDpi(36, dpi) / 2.0, GetSystemMetricsForDpi(37, dpi) / 2.0);
            ArmClickTimeout();
            UpdateClickAwayRegistration();
        }
        _dragUnit = count switch
        {
            2 => MarkdownSelectionUnit.Word,
            3 => MarkdownSelectionUnit.Paragraph,
            _ => MarkdownSelectionUnit.Character,
        };
        int anchor = PointerAnchor(Document.Anchor, offset, e.KeyModifiers);
        _pressSpan = _dragUnit == MarkdownSelectionUnit.Character ? new(anchor, anchor) :
            Document.UnitAt(_dragUnit, glyphOffset, entry);
        _pressLocalGlyph = glyphOffset - entry.Start;
        _pressEntry = entry;
        Document.Select(_pressSpan.Start, _dragUnit == MarkdownSelectionUnit.Character ? offset : _pressSpan.End);

        var link = LinkSegmentAtPoint(_dragPoint, glyphOffset, entry, e.OriginalSource as Hyperlink);
        var nativeOwner = (UIElement?)linkButton ?? link?.Owner;
        _pointer = e.Pointer;
        UpdateLayoutTracking();
        if (nativeOwner is not null && count == 1 && !shift)
        {
            // Let the native control activate and retain focus for an ordinary click.
            _nativePressOwner = nativeOwner;
            DrawSelection();
            return;
        }
        TakeSelectionCapture(e.Pointer, nativeOwner);
        DrawSelection();
        e.Handled = true;
    }

    private bool TakeSelectionCapture(Pointer pointer, UIElement? nativeOwner)
    {
        // ButtonBase captures in its class handler, before the routed event reaches us.
        // Losing that capture cancels its pending release-mode click.
        nativeOwner?.ReleasePointerCapture(pointer);
        Focus(FocusState.Pointer);
        if (CapturePointer(pointer)) return true;
        StopDrag();
        ResetClickSequence();
        return false;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pointer?.PointerId != e.Pointer.PointerId) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            OnPointerCanceled(sender, e);
            return;
        }
        _dragPoint = point.Position;
        _rootDragPoint = RootPoint(_dragPoint);
        if (!_dragged)
        {
            if (Math.Abs(_rootDragPoint.X - _pressPoint.X) + Math.Abs(_rootDragPoint.Y - _pressPoint.Y) < 4)
                return;
            ResetClickSequence();
            var nativeOwner = _nativePressOwner;
            _nativePressOwner = null;
            if (nativeOwner is not null && !TakeSelectionCapture(e.Pointer, nativeOwner)) return;
            _dragged = true;
            _dragTimer.Start();
        }
        UpdateDragSelection();
        e.Handled = true;
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pointer?.PointerId != e.Pointer.PointerId) return;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased) return;
        if (_dragged)
        {
            _dragPoint = point.Position;
            UpdateDragSelection();
        }
        bool selectionOwnsRelease = _nativePressOwner is null;
        StopDrag();
        if (selectionOwnsRelease) e.Handled = true;
    }

    private void UpdateDragSelection()
    {
        int offset = HitTest(_dragPoint, out var entry, out int glyphOffset);
        if (_dragUnit == MarkdownSelectionUnit.Character) Document.Select(_pressSpan.Start, offset);
        else
        {
            var (anchor, caret) = _pressSpan.ExtendTo(Document.UnitAt(_dragUnit, glyphOffset, entry));
            Document.Select(anchor, caret);
        }
        DrawSelection();
    }

    private void OnCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, this)) OnPointerCanceled(sender, e);
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_pointer?.PointerId != e.Pointer.PointerId) return;
        StopDrag();
        ResetClickSequence();
    }

    internal static bool IsMouseSelection(Microsoft.UI.Input.PointerDeviceType device) =>
        device == Microsoft.UI.Input.PointerDeviceType.Mouse;

    internal static int PointerAnchor(int anchor, int offset, VirtualKeyModifiers modifiers) =>
        (modifiers & VirtualKeyModifiers.Shift) != 0 ? anchor : offset;
    private void StopDrag()
    {
        _dragTimer.Stop();
        _nativePressOwner = null;
        _dragged = false;
        _pressEntry = null;
        var pointer = _pointer;
        _pointer = null;
        UpdateLayoutTracking();
        if (pointer is not null) ReleasePointerCapture(pointer);
    }

    private void OnDragTick(object? sender, object e)
    {
        if (_pointer is null) return;
        ScrollDrag(_rootDragPoint);
    }

    internal static double DragScrollDelta(Rect viewport, Point point) =>
        viewport.Width <= 0 || viewport.Height <= 0 ? 0 :
        point.Y < viewport.Top ? -24 : point.Y > viewport.Bottom ? 24 : 0;

    internal void ScrollDrag(Point rootPoint)
    {
        _dragPoint = LocalPoint(rootPoint);
        double delta = DragScrollDelta(ScrollViewport(), _dragPoint);
        if (delta == 0) return;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent is not null;
            parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollView modern)
            {
                modern.ScrollBy(0, delta, new ScrollingScrollOptions(
                    ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
                break;
            }
            if (parent is ScrollViewer legacy)
            {
                legacy.ChangeView(null, legacy.VerticalOffset + delta, null, true);
                break;
            }
        }
        _dragPoint = LocalPoint(rootPoint);
        UpdateDragSelection();
    }

    private static bool IsDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!CanHandleKey(e.OriginalSource, e.Handled)) return;
        bool extend = IsDown(VirtualKey.Shift), ctrl = IsDown(VirtualKey.Control);
        e.Handled = HandleKey(e.Key, extend, ctrl);
    }

    internal bool CanHandleKey(object source, bool handled)
    {
        if (!OwnsInput(source) || IsChrome(source, out var linkButton) || linkButton is not null) return false;
        if (ReferenceEquals(source, this) || source is Hyperlink) return true;
        if (handled) return false;
        for (var node = source as DependencyObject; node is not null && !ReferenceEquals(node, this);
            node = VisualTreeHelper.GetParent(node))
        {
            if (node is Control and not MarkdownSelectionMarker) return false;
            if (node is MarkdownSelectionMarker { Metadata.Role: SelectionRole.Surface }) return true;
        }
        return false;
    }

    internal bool HasSurfaceAncestor(object source)
    {
        for (var node = source as DependencyObject; node is not null && !ReferenceEquals(node, this);
            node = VisualTreeHelper.GetParent(node))
            if (node is MarkdownSelectionMarker { Metadata.Role: SelectionRole.Surface }) return true;
        return false;
    }

    internal void ClearForClickAway()
    {
        ResetClickSequence();
        StopDrag();
        _suppressCaret = true;
        Document.Select(Document.Caret, Document.Caret);
        UpdateLayoutTracking();
        DrawSelection();
    }

    internal bool HandleKey(VirtualKey key, bool extend, bool ctrl)
    {
        Refresh();
        switch (key)
        {
            case VirtualKey.A when ctrl:
                Document.Select(0, Document.Text.Length);
                break;
            case VirtualKey.C when ctrl:
                CopySelectionCore();
                break;
            case VirtualKey.Left:
                Document.Move(-1, extend);
                break;
            case VirtualKey.Right:
                Document.Move(1, extend);
                break;
            case VirtualKey.Home:
            case VirtualKey.End:
                int next = key == VirtualKey.Home ? 0 : Document.Text.Length;
                Document.Select(extend ? Document.Anchor : next, next);
                break;
            case VirtualKey.Up:
            case VirtualKey.Down:
            case VirtualKey.PageUp:
            case VirtualKey.PageDown:
                MoveVertical(key is VirtualKey.Up or VirtualKey.PageUp ? -1 : 1,
                    key is VirtualKey.PageUp or VirtualKey.PageDown, extend);
                break;
            case VirtualKey.Escape:
                Document.Select(Document.Caret, Document.Caret);
                break;
            default: return false;
        }
        _suppressCaret = false;
        UpdateLayoutTracking();
        DrawSelection();
        BringCaretIntoView();
        return true;
    }

    internal void CopySelection(Action<string>? write = null)
    {
        Refresh();
        CopySelectionCore(write);
    }

    private void CopySelectionCore(Action<string>? write = null)
    {
        string text = Document.SelectedText;
        if (text.Length == 0) return;
        MarkdownSelectionCopy.Write(text, write ?? (static value =>
        {
            var package = new DataPackage();
            package.SetText(value);
            Clipboard.SetContent(package);
        }), Options.OnCopyResult);
    }

    internal void MoveVertical(int direction, bool page, bool extend)
    {
        var current = CharacterBounds(Document.Caret);
        double distance = page ? Math.Max(current.Height, Viewport().Height) : Math.Max(1, current.Height);
        var target = new Point(current.X, current.Y + direction * distance);
        int next = HitTest(target, direction, current.Y);
        Document.Select(extend ? Document.Anchor : next, next);
        BringCaretIntoView();
    }

    internal void BringCaretIntoView()
    {
        var entry = Document.EntryAt(Document.Caret);
        if (entry?.Identity is not MarkdownSelectionMarker marker) return;
        var rect = CharacterBounds(Document.Caret);
        var local = Transform(marker).Inverse.TransformBounds(rect);
        marker.StartBringIntoView(new BringIntoViewOptions { TargetRect = local, AnimationDesired = false });
    }

    internal int HitTest(Point point, int direction = 0, double currentY = 0) =>
        HitTest(point, out _, out _, direction, currentY);

    private int HitTest(Point point, out MarkdownSelectionDocument.Entry? hitEntry, out int glyphOffset,
        int direction = 0, double currentY = 0, bool requireInsideSurface = false)
    {
        int best = 0;
        hitEntry = null;
        double score = double.MaxValue;
        var entries = Document.Entries;
        _hitCandidates.Clear();
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            var entry = entries[entryIndex];
            var bounds = SurfaceBounds((MarkdownSelectionMarker)entry.Identity);
            double dy = point.Y < bounds.Top ? bounds.Top - point.Y :
                point.Y > bounds.Bottom ? point.Y - bounds.Bottom : 0;
            double dx = point.X < bounds.Left ? bounds.Left - point.X :
                point.X > bounds.Right ? point.X - bounds.Right : 0;
            if (requireInsideSurface && !Contains(bounds, point)) continue;
            _hitCandidates.Add((dy * dy * 10000 + dx * dx, entryIndex));
        }
        _hitCandidates.Sort();
        foreach (var candidate in _hitCandidates)
        {
            if (candidate.Score > score) break;
            var entry = entries[candidate.Index];
            // No intersection with Viewport here: navigation traverses the entire message.
            for (int local = 0; local <= entry.Text.Length;)
            {
                int offset = entry.Start + local;
                var rect = local < entry.Text.Length || ReferenceEquals(entry, Document.EntryAt(offset))
                    ? CharacterBounds(offset) : ComputeCharacterBounds(entry, offset);
                if (direction == 0 || direction * (rect.Y - currentY) > 0.5)
                {
                    double glyphDy = point.Y < rect.Top ? rect.Top - point.Y :
                        point.Y > rect.Bottom ? point.Y - rect.Bottom : 0;
                    double glyphDx = point.X - rect.X;
                    double distance = glyphDy * glyphDy * 10000 + glyphDx * glyphDx;
                    if (distance < score) { best = offset; score = distance; hitEntry = entry; }
                }
                if (local == entry.Text.Length) break;
                int following = Document.MoveGrapheme(offset, 1) - entry.Start;
                local = Math.Min(entry.Text.Length, Math.Max(local + 1, following));
            }
        }
        if (hitEntry is null) return glyphOffset = Document.Caret;
        glyphOffset = best;
        // The nearest caret may be at the right edge of the glyph under the pointer.
        // Prefer that glyph over the one starting at the caret (often the next word).
        if (best > hitEntry.Start)
        {
            int previous = Document.MoveGrapheme(best, -1);
            if (previous >= hitEntry.Start &&
                (best == hitEntry.End || !Contains(GlyphBounds(best, Document.MoveGrapheme(best, 1)), point)) &&
                Contains(GlyphBounds(previous, best), point))
                glyphOffset = previous;
        }
        return best;
    }

    internal Rect CharacterBounds(int offset)
    {
        if (_characterRects.TryGetValue(offset, out var cached)) return cached;
        var result = ComputeCharacterBounds(offset);
        _characterRects[offset] = result;
        return result;
    }

    internal static List<(int Start, int End)> VisibleRanges(
        IEnumerable<(int Start, int End, Rect Bounds)> spans, Rect viewport, Func<int, Rect> characterBounds)
    {
        var result = new List<(int Start, int End)>();
        int start = -1, end = 0;
        foreach (var span in spans)
        {
            if (span.Start == span.End) continue;
            var bounds = Intersect(span.Bounds, viewport);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                if (start >= 0) { result.Add((start, end)); start = -1; }
                continue;
            }
            for (int offset = span.Start; offset < span.End; offset++)
            {
                var rect = Intersect(characterBounds(offset), viewport);
                bool visible = rect.Width > 0 && rect.Height > 0;
                if (visible && start < 0) start = offset;
                if (!visible && start >= 0) { result.Add((start, offset)); start = -1; }
                end = offset + 1;
            }
        }
        if (start >= 0) result.Add((start, end));
        return result;
    }

    internal Rect GlyphBounds(int offset, int next)
    {
        if (_glyphRects.TryGetValue((offset, next), out var cached)) return cached;
        var start = CharacterBounds(offset);
        var entry = Document.EntryAt(offset);
        if (entry?.Identity is not MarkdownSelectionMarker marker || offset >= entry.End) return start;
        var found = SegmentAt(marker, offset - entry.Start);
        if (found is { } segment)
        {
            int endOffset = Math.Min(next - entry.Start - segment.LocalStart, segment.Text.Length);
            if (segment.Start is null)
                return _glyphRects[(offset, next)] = GlyphRectangle(start, FallbackCaret(segment.Owner, endOffset, segment.Text.Length));
            var pointer = segment.Start.GetPositionAtOffset(endOffset, LogicalDirection.Backward);
            if (pointer is not null)
            {
                var end = Transform(segment.Owner).TransformBounds(
                    pointer.GetCharacterRect(LogicalDirection.Backward));
                return _glyphRects[(offset, next)] = GlyphRectangle(start, end);
            }
        }
        return start;
    }

    internal IEnumerable<(int Offset, int Next)> VisibleGlyphs(int start, int end)
    {
        var viewport = Viewport();
        foreach (var entry in Document.Entries)
        {
            if (entry.End <= start || entry.Start >= end) continue;
            var bounds = Intersect(Bounds((MarkdownSelectionMarker)entry.Identity), viewport);
            if (bounds.Width <= 0 || bounds.Height <= 0) continue;
            for (int offset = Math.Max(start, entry.Start); offset < Math.Min(end, entry.End);)
            {
                int next = Document.MoveGrapheme(offset, 1);
                yield return (offset, next);
                offset = Math.Max(offset + 1, next);
            }
        }
    }

    internal int[] VisualLineBoundaries()
    {
        var boundaries = new List<int> { 0 };
        foreach (var entry in Document.Entries)
        {
            if (boundaries[^1] != entry.Start) boundaries.Add(entry.Start);
            double previousY = double.NaN;
            for (int offset = entry.Start; offset < entry.End;)
            {
                double y = CharacterBounds(offset).Y;
                if (!double.IsNaN(previousY) && Math.Abs(y - previousY) > .5 && boundaries[^1] != offset)
                    boundaries.Add(offset);
                previousY = y;
                offset = Math.Max(offset + 1, Document.MoveGrapheme(offset, 1));
            }
        }
        if (boundaries[^1] != Document.Text.Length) boundaries.Add(Document.Text.Length);
        return boundaries.ToArray();
    }

    internal SolidColorBrush SelectionBrush(int offset)
    {
        var entry = Document.EntryAt(offset);
        if (entry?.Identity is MarkdownSelectionMarker marker)
            foreach (var segment in Segments(marker))
                if (segment.Owner is RichTextBlock rich) return rich.SelectionHighlightColor;
                else if (segment.Owner is TextBlock text) return text.SelectionHighlightColor;
        return _selectionDefaults.SelectionHighlightColor;
    }

    private void OnThemeChanged(FrameworkElement sender, object args) => DrawSelection();

    internal static Rect GlyphRectangle(Rect start, Rect end) =>
        Math.Abs(start.Y - end.Y) < .5
            ? new Rect(Math.Min(start.X, end.X), start.Y, Math.Max(1, Math.Abs(end.X - start.X)),
                Math.Max(start.Height, end.Height))
            : start;

    internal void ScrollRangeIntoView(int offset, bool alignToTop)
    {
        var entry = Document.EntryAt(offset);
        if (entry?.Identity is not MarkdownSelectionMarker marker) return;
        marker.StartBringIntoView(new BringIntoViewOptions
        {
            TargetRect = Transform(marker).Inverse.TransformBounds(CharacterBounds(offset)),
            VerticalAlignmentRatio = alignToTop ? 0 : 1,
            AnimationDesired = false
        });
    }

    private Rect ComputeCharacterBounds(int offset)
    {
        var entry = Document.EntryAt(offset);
        return entry is null ? default : ComputeCharacterBounds(entry, offset);
    }

    private Rect ComputeCharacterBounds(MarkdownSelectionDocument.Entry entry, int offset)
    {
        var marker = (MarkdownSelectionMarker)entry.Identity;
        int local = Math.Clamp(offset - entry.Start, 0, entry.Text.Length);
        var found = SegmentAt(marker, local);
        if (found is { } segment)
        {
            int segmentOffset = Math.Clamp(local - segment.LocalStart, 0, segment.Text.Length);
            if (segment.Start is null) return FallbackCaret(segment.Owner, segmentOffset, segment.Text.Length);
            var pointer = segment.Start.GetPositionAtOffset(segmentOffset, LogicalDirection.Forward);
            if (pointer is not null)
            {
                var rect = Transform(segment.Owner).TransformBounds(
                    pointer.GetCharacterRect(LogicalDirection.Forward));
                rect.Width = Math.Max(1, rect.Width);
                rect.Height = Math.Max(1, rect.Height);
                return rect;
            }
        }
        // Explicit custom surfaces without native text still have a usable caret
        // and embedded-control range. Their text is supplied by the author, not XAML shape.
        return FallbackCaret(marker, local, entry.Text.Length);
    }

    private Rect FallbackCaret(FrameworkElement owner, int offset, int length)
    {
        var bounds = Bounds(owner);
        return new Rect(bounds.X + bounds.Width * offset / Math.Max(1, length),
            bounds.Y, 1, Math.Max(1, bounds.Height));
    }

    private readonly record struct TextSegment(FrameworkElement Owner, string Text, TextPointer? Start, DependencyObject Identity,
        Hyperlink? Link = null, int LocalStart = 0, int Style = 0);

    private bool InvalidateNativeSegments()
    {
        bool changed = false;
        foreach (var surface in _surfaces)
        {
            if (!_segments.TryGetValue(surface, out var cached)) continue;
            int index = 0;
            bool matches = true;
            foreach (var current in ReadSegments(surface))
            {
                if (current.Text.Length == 0) continue;
                if (index >= cached.Length || !ReferenceEquals(current.Identity, cached[index].Identity)
                    || !ReferenceEquals(current.Owner, cached[index].Owner)
                    || !ReferenceEquals(current.Link, cached[index].Link) || current.Text != cached[index].Text
                    || current.Style != cached[index].Style)
                {
                    matches = false;
                    break;
                }
                index++;
            }
            if (matches && index == cached.Length) continue;
            _segments.Remove(surface);
            changed = true;
        }
        return changed;
    }

    private TextSegment? SegmentAt(MarkdownSelectionMarker marker, int offset)
    {
        var segments = Segments(marker);
        int low = 0, high = segments.Length - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (segments[mid].LocalStart <= offset) low = mid + 1;
            else high = mid - 1;
        }
        return high >= 0 ? segments[high] : null;
    }

    internal Hyperlink? LinkAt(int offset)
    {
        var entry = Document.EntryAt(offset);
        if (entry?.Identity is not MarkdownSelectionMarker marker) return null;
        return offset < entry.End ? SegmentAt(marker, offset - entry.Start)?.Link : null;
    }

    internal bool LinkAtPoint(Point point, int offset)
    {
        int previous = Document.MoveGrapheme(offset, -1);
        return LinkAt(offset) is not null && Contains(GlyphBounds(offset, Document.MoveGrapheme(offset, 1)), point)
            || LinkAt(previous) is not null && Contains(GlyphBounds(previous, offset), point);
    }

    private TextSegment? LinkSegmentAtPoint(Point point, int offset, MarkdownSelectionDocument.Entry entry,
        Hyperlink? source)
    {
        var marker = (MarkdownSelectionMarker)entry.Identity;
        if (source is not null)
        {
            foreach (var segment in Segments(marker))
                if (ReferenceEquals(segment.Link, source)) return segment;
        }
        if (offset >= entry.End || SegmentAt(marker, offset - entry.Start) is not { Link: not null } hit ||
            !Contains(GlyphBounds(offset, Document.MoveGrapheme(offset, 1)), point)) return null;
        return hit;
    }

    private TextSegment[] Segments(MarkdownSelectionMarker marker)
    {
        if (!_segments.TryGetValue(marker, out var segments))
        {
            int offset = 0;
            _segments[marker] = segments = ReadSegments(marker).Where(segment => segment.Text.Length > 0)
                .Select(segment =>
                {
                    var indexed = segment with { LocalStart = offset };
                    offset += segment.Text.Length;
                    return indexed;
                }).ToArray();
        }

        return segments;
    }

    private IEnumerable<TextSegment> ReadSegments(MarkdownSelectionMarker marker)
    {
        SegmentReadCount++;
        return SurfaceTextSegments(marker);
    }

    private static IEnumerable<TextSegment> SurfaceTextSegments(MarkdownSelectionMarker marker)
    {
        if (!marker.Metadata.IsLink) return TextSegments(marker);
        // A button template's text pointers need not supply distinct caret
        // positions. Map the semantic label over the clickable control instead.
        if (marker.Child is ButtonBase button)
            return [new TextSegment(button, marker.Metadata.Text, null, marker)];
        var native = TextSegments(marker).ToArray();
        return string.Concat(native.Select(s => s.Text)) != marker.Metadata.Text
            ? [new TextSegment(marker.Child as FrameworkElement ?? marker, marker.Metadata.Text, null, marker)]
            : native;
    }

    private static IEnumerable<TextSegment> TextSegments(DependencyObject root)
    {
        if (root is MarkdownSelectionHost) yield break;
        if (root is TextBlock tb)
        {
            if (tb.IsTextSelectionEnabled) tb.IsTextSelectionEnabled = false;
            foreach (var segment in InlineSegments(tb, tb.Inlines)) yield return segment;
            yield break;
        }
        if (root is RichTextBlock rtb)
        {
            if (rtb.IsTextSelectionEnabled) rtb.IsTextSelectionEnabled = false;
            foreach (var paragraph in rtb.Blocks.OfType<Paragraph>())
                foreach (var segment in InlineSegments(rtb, paragraph.Inlines)) yield return segment;
            yield break;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var segment in TextSegments(VisualTreeHelper.GetChild(root, i))) yield return segment;
    }

    private static IEnumerable<TextSegment> InlineSegments(FrameworkElement owner, InlineCollection inlines, Hyperlink? link = null)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run) yield return new TextSegment(owner, run.Text, run.ContentStart, run, link,
                Style: HashCode.Combine(run.FontFamily.Source, run.FontSize, run.FontWeight, run.FontStyle,
                    run.FontStretch, run.CharacterSpacing, run.Language));
            else if (inline is LineBreak line) yield return new TextSegment(owner, "\n", line.ContentStart, line);
            else if (inline is Span span)
                foreach (var segment in InlineSegments(owner, span.Inlines, span as Hyperlink ?? link)) yield return segment;
            else if (inline is InlineUIContainer container && container.Child is { } child)
                foreach (var segment in child is MarkdownSelectionMarker { Metadata.IsLink: true } marker
                    ? SurfaceTextSegments(marker) : TextSegments(child)) yield return segment;
        }
    }

    private void DrawSelection()
    {
        if (Document.Start == Document.End && (FocusState == FocusState.Unfocused || Document.Text.Length == 0))
        {
            if (_overlay.Children.Count > 0) _overlay.Children.Clear();
            return;
        }
        // Keep layout stable: the overlay is non-hit-testable and never participates
        // in document registration. Only selected, viewport-intersecting cells are drawn.
        _selectionRectangles.Clear();
        var viewport = Viewport();
        if (HasVisibleCaret)
        {
            var caret = Intersect(CharacterBounds(Document.Caret), viewport);
            if (caret.Width > 0 && caret.Height > 0) _selectionRectangles.Add(caret);
        }
        var entries = Document.Entries;
        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            var entry = entries[entryIndex];
            if (entry.End <= Document.Start || entry.Start >= Document.End) continue;
            var surfaceBounds = Intersect(SurfaceBounds((MarkdownSelectionMarker)entry.Identity), viewport);
            if (surfaceBounds.Width <= 0 || surfaceBounds.Height <= 0) continue;
            for (int offset = Math.Max(Document.Start, entry.Start); offset < Math.Min(Document.End, entry.End);)
            {
                int next = Document.MoveGrapheme(offset, 1);
                var rect = Intersect(GlyphBounds(offset, next), viewport);
                if (rect.Width > 0 && rect.Height > 0)
                {
                    if (_selectionRectangles.Count > 0 && Math.Abs(_selectionRectangles[^1].Y - rect.Y) < 0.5 &&
                        rect.Left <= _selectionRectangles[^1].Right + 2 &&
                        rect.Right >= _selectionRectangles[^1].Left - 2)
                    {
                        var previous = _selectionRectangles[^1];
                        _selectionRectangles[^1] = new Rect(Math.Min(previous.Left, rect.Left), previous.Y,
                            Math.Max(previous.Right, rect.Right) - Math.Min(previous.Left, rect.Left), previous.Height);
                    }
                    else _selectionRectangles.Add(rect);
                }
                offset = Math.Max(offset + 1, next);
            }
        }
        while (_overlay.Children.Count > _selectionRectangles.Count) _overlay.Children.RemoveAt(_overlay.Children.Count - 1);
        for (int i = 0; i < _selectionRectangles.Count; i++)
        {
            if (_overlay.Children.Count <= i)
                _overlay.Children.Add(new Rectangle { Opacity = .35 });
            var shape = (Rectangle)_overlay.Children[i];
            shape.Fill = SelectionBrush(Document.Start);
            var rect = _selectionRectangles[i];
            if (shape.Width != rect.Width) shape.Width = rect.Width;
            if (shape.Height != rect.Height) shape.Height = rect.Height;
            if (Canvas.GetLeft(shape) != rect.X) Canvas.SetLeft(shape, rect.X);
            if (Canvas.GetTop(shape) != rect.Y) Canvas.SetTop(shape, rect.Y);
        }
    }

    internal void Select(int start, int end)
    {
        Refresh();
        SelectRefreshed(start, end);
    }

    internal void SelectRefreshed(int start, int end)
    {
        Document.Select(start, end);
        DrawSelection();
    }

    internal IReadOnlyList<(FrameworkElement Control, int Start, int End)> EmbeddedControls()
    {
        var children = new List<(FrameworkElement, int, int)>();
        foreach (var entry in Document.Entries)
        {
            var marker = (MarkdownSelectionMarker)entry.Identity;
            // Header templates place the semantic surface inside their control,
            // unlike embedded link renderers. Associate only header text, not body text.
            for (DependencyObject? ancestor = VisualTreeHelper.GetParent(marker);
                ancestor is not null && !ReferenceEquals(ancestor, this);
                ancestor = VisualTreeHelper.GetParent(ancestor))
            {
                if (ancestor is not Expander { Header: DependencyObject header } expander ||
                    !IsDescendantOf(marker, header)) continue;
                if (FrameworkElementAutomationPeer.CreatePeerForElement(expander) is null) continue;
                int existing = children.FindIndex(child => ReferenceEquals(child.Item1, expander));
                if (existing < 0) children.Add((expander, entry.Start, entry.End));
                else
                {
                    var previous = children[existing];
                    children[existing] = (expander, Math.Min(previous.Item2, entry.Start),
                        Math.Max(previous.Item3, entry.End));
                }
                break;
            }
            foreach (var child in Descendants(marker).OfType<Control>())
            {
                if (child is MarkdownSelectionHost or MarkdownSelectionMarker or ScrollViewer or ScrollView) continue;
                bool nestedControl = false;
                for (DependencyObject? parent = VisualTreeHelper.GetParent(child);
                    parent is not null && !ReferenceEquals(parent, marker);
                    parent = VisualTreeHelper.GetParent(parent))
                {
                    if (parent is Control parentControl
                        && parent is not MarkdownSelectionMarker and not ScrollViewer and not ScrollView
                        && FrameworkElementAutomationPeer.CreatePeerForElement(parentControl) is not null)
                    { nestedControl = true; break; }
                }
                if (nestedControl) continue;
                if (FrameworkElementAutomationPeer.CreatePeerForElement(child) is null) continue;
                int index = entry.Start, start = -1, end = -1;
                foreach (var segment in Segments(marker))
                {
                    if (IsDescendantOf(segment.Owner, child))
                    {
                        if (start < 0) start = index;
                        end = index + segment.Text.Length;
                    }
                    index += segment.Text.Length;
                }
                if (start < 0)
                {
                    var bounds = Bounds(child);
                    start = Math.Clamp(HitTest(new Point(bounds.Left, bounds.Top)), entry.Start, entry.End);
                    end = start;
                }
                // Non-text controls are insertion points, not invented copied
                // characters. Text-bearing controls retain their exact label range.
                children.Add((child, Math.Clamp(start, entry.Start, entry.End),
                    Math.Clamp(end, entry.Start, entry.End)));
            }
        }
        return children;
    }

    private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
    {
        for (DependencyObject? parent = node; parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (ReferenceEquals(parent, ancestor)) return true;
        return false;
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        _automationPeer ??= new MarkdownSelectionAutomationPeer(this);
}

internal static class MarkdownSelectionClickAway
{
    private static readonly ConditionalWeakTable<XamlRoot, Router> Routers = [];

    internal static void SetActive(XamlRoot root, MarkdownSelectionHost host, bool active)
    {
        if (active)
            Routers.GetValue(root, static xamlRoot => new Router(xamlRoot)).SetActive(host, true);
        else if (Routers.TryGetValue(root, out var router) && !router.SetActive(host, false))
            Routers.Remove(root);
    }

    private sealed class Router
    {
        private readonly List<WeakReference<MarkdownSelectionHost>> _hosts = [];
        private readonly UIElement? _content;
        private readonly PointerEventHandler _pressed;

        internal Router(XamlRoot root)
        {
            _content = root.Content;
            _pressed = OnPressed;
        }

        internal bool SetActive(MarkdownSelectionHost host, bool active)
        {
            bool contains = _hosts.Any(reference =>
                reference.TryGetTarget(out var existing) && ReferenceEquals(existing, host));
            if (active && !contains)
            {
                if (_hosts.Count == 0) _content?.AddHandler(UIElement.PointerPressedEvent, _pressed, true);
                _hosts.Add(new(host));
            }
            else if (!active && contains)
                _hosts.RemoveAll(reference =>
                    !reference.TryGetTarget(out var existing) || ReferenceEquals(existing, host));
            if (_hosts.Count > 0) return true;
            _content?.RemoveHandler(UIElement.PointerPressedEvent, _pressed);
            return false;
        }

        private void OnPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!MarkdownSelectionHost.IsMouseSelection(e.Pointer.PointerDeviceType) ||
                e.GetCurrentPoint(null).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
                return;

            MarkdownSelectionHost? target = null;
            for (var node = e.OriginalSource as DependencyObject; node is not null;
                node = VisualTreeHelper.GetParent(node))
            {
                if (node is ScrollBar) return;
                if (node is MarkdownSelectionHost host) { target = host; break; }
            }
            bool targetOwnsSurface = target?.HasSurfaceAncestor(e.OriginalSource) == true;
            foreach (var reference in _hosts.ToArray())
            {
                if (!reference.TryGetTarget(out var host))
                {
                    _hosts.Remove(reference);
                    continue;
                }
                if (ReferenceEquals(host, target) && targetOwnsSurface) continue;
                host.ClearForClickAway();
            }
        }
    }
}
