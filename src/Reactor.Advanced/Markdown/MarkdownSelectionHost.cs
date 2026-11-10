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
    private readonly Dictionary<int, Rect> _characterRects = [];
    private readonly Dictionary<(int Offset, int Next), Rect> _glyphRects = [];
    private readonly HashSet<FrameworkElement> _layoutOwners = [];
    private readonly Dictionary<MarkdownSelectionMarker, TextSegment[]> _segments = [];
    private readonly DispatcherTimer _dragTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly PointerEventHandler _pressed;
    private readonly PointerEventHandler _moved;
    private readonly PointerEventHandler _released;
    private readonly KeyEventHandler _keyDown;
    private Pointer? _pointer;
    private Point _dragPoint;
    private Point _rootDragPoint;
    private Point _pressPoint;
    private Pointer? _pendingLinkPointer;
    private bool _documentDirty = true;
    private int _layoutSignature;
    private Rect _viewport;
    private bool _refreshing;
    private bool _detached;

    internal MarkdownTextSelectionOptions Options { get; set; } = null!;
    internal UIElement? Body { get; private set; }
    internal MarkdownSelectionDocument Document { get; } = new();
    internal int CachedCharacterCount => _characterRects.Count;

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
        AddHandler(PointerPressedEvent, _pressed, true);
        AddHandler(PointerMovedEvent, _moved, true);
        AddHandler(PointerReleasedEvent, _released, true);
        AddHandler(KeyDownEvent, _keyDown, true);
        PointerCaptureLost += OnCaptureLost;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnThemeChanged;
        _dragTimer.Tick += OnDragTick;
        Document.Changed = OnDocumentChanged;
    }

    internal void InvalidateDocument() => _documentDirty = true;

    private void OnDocumentChanged(bool text, bool selection)
    {
        if (FrameworkElementAutomationPeer.FromElement(this) is MarkdownSelectionAutomationPeer peer)
            peer.NotifyChanged(text, selection);
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
        LayoutUpdated -= OnLayout;
        LayoutUpdated += OnLayout;
        Refresh();
    }
    private void OnLayout(object? sender, object e)
    {
        if (_detached || !IsLoaded) return;
        CheckMembership();
        bool nativeTextChanged = InvalidateNativeSegments();
        int signature = LayoutSignature();
        var viewport = Viewport();
        if (!_documentDirty && !nativeTextChanged && signature == _layoutSignature && viewport == _viewport) return;
        Refresh(membershipChecked: true, nativeTextChanged, signature);
        if (_pointer is not null)
        {
            _dragPoint = LocalPoint(_rootDragPoint);
            Document.Select(Document.Anchor, HitTest(_dragPoint));
            DrawSelection();
        }
    }

    private int LayoutSignature()
    {
        var hash = new HashCode();
        hash.Add(XamlRoot?.RasterizationScale);
        _layoutOwners.Clear();
        foreach (var surface in _surfaces)
        {
            hash.Add(Bounds(surface));
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
        LayoutUpdated -= OnLayout;
        StopDrag();
        _surfaces.Clear();
        _characterRects.Clear();
        _glyphRects.Clear();
        _layoutOwners.Clear();
        _segments.Clear();
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
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        ActualThemeChanged -= OnThemeChanged;
        LayoutUpdated -= OnLayout;
        _dragTimer.Tick -= OnDragTick;
    }

    private void CheckMembership()
    {
        _membership.Clear();
        if (Body is not null) Visit(Body, _membership);
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
            if (!membershipChecked) CheckMembership();
            bool geometryChanged = _documentDirty;
            if (_documentDirty)
            {
                _documentDirty = false;
                _surfaces.Clear();
                _segments.Clear();
                _surfaces.AddRange(_membership);
                Document.Rebuild(_surfaces.Select(s =>
                    new SelectionPart(s, s.Metadata.Text, s.Metadata.SeparatorAfter)));
                foreach (var surface in _surfaces) _ = Segments(surface);
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

    private void Visit(DependencyObject node, List<MarkdownSelectionMarker> target)
    {
        // Registration is semantic, not viewport-based. Fully clipped blocks remain
        // part of the document so Down/PageDown and UIA can reach them.
        if (node is MarkdownSelectionHost) return;
        if (node is UIElement { Visibility: Visibility.Collapsed }) return;
        if (node is MarkdownSelectionMarker marker)
        {
            if (marker.Metadata.Role == SelectionRole.Visibility && !marker.Metadata.IsVisible) return;
            if (marker.Metadata.Role == SelectionRole.Surface)
            {
                target.Add(marker);
                return;
            }
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            Visit(VisualTreeHelper.GetChild(node, i), target);
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
            return element.TransformToVisual(this)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        catch (ArgumentException) { return default; }
    }

    internal Rect Viewport()
    {
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent is not null;
            parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollView or ScrollViewer && parent is FrameworkElement viewport)
                rect = Intersect(rect, Bounds(viewport));
        }
        return rect;
    }

    internal static Rect Intersect(Rect a, Rect b)
    {
        double x = Math.Max(a.Left, b.Left), y = Math.Max(a.Top, b.Top);
        return new Rect(x, y, Math.Max(0, Math.Min(a.Right, b.Right) - x),
            Math.Max(0, Math.Min(a.Bottom, b.Bottom) - y));
    }

    private static bool Contains(Rect r, Point p) =>
        r.Width > 0 && r.Height > 0 && p.X >= r.Left && p.X <= r.Right && p.Y >= r.Top && p.Y <= r.Bottom;

    private bool IsChrome(object source)
    {
        if (source is Hyperlink) return false;
        for (var node = source as DependencyObject; node is not null && !ReferenceEquals(node, this);
            node = VisualTreeHelper.GetParent(node))
            if (node is ButtonBase or TextBox or PasswordBox or Selector) return true;
        return false;
    }

    internal bool OwnsInput(object source)
    {
        if (source is Hyperlink hyperlink)
            return _surfaces.Any(surface => Segments(surface).Any(segment => ReferenceEquals(segment.Link, hyperlink)));
        for (var node = source as DependencyObject; node is not null;
            node = VisualTreeHelper.GetParent(node))
            if (node is MarkdownSelectionHost scope) return ReferenceEquals(scope, this);
        return false;
    }

    internal Point RootPoint(Point local) => TransformToVisual(null).TransformPoint(local);
    internal Point LocalPoint(Point root) => TransformToVisual(null).Inverse.TransformPoint(root);

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsMouseSelection(e.Pointer.PointerDeviceType)) return;
        if (!OwnsInput(e.OriginalSource) || IsChrome(e.OriginalSource)) return;
        Refresh();
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || !Contains(Viewport(), point.Position) ||
            !_surfaces.Any(s => Contains(Bounds(s), point.Position))) return;
        _dragPoint = point.Position;
        _rootDragPoint = RootPoint(_dragPoint);
        int offset = HitTest(_dragPoint);
        if (LinkAtPoint(_dragPoint, offset))
        {
            _pendingLinkPointer = e.Pointer;
            _pressPoint = _rootDragPoint;
            return;
        }
        Focus(FocusState.Pointer);
        Document.Select(PointerAnchor(Document.Anchor, offset, e.KeyModifiers), offset);
        if (CapturePointer(e.Pointer))
        {
            _pointer = e.Pointer;
            _dragTimer.Start();
        }
        DrawSelection();
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pendingLinkPointer?.PointerId == e.Pointer.PointerId)
        {
            var point = e.GetCurrentPoint(this);
            var root = RootPoint(point.Position);
            if (!point.Properties.IsLeftButtonPressed) { _pendingLinkPointer = null; return; }
            if (Math.Abs(root.X - _pressPoint.X) + Math.Abs(root.Y - _pressPoint.Y) < 4) return;
            _pendingLinkPointer = null;
            if (!CapturePointer(e.Pointer)) return;
            Focus(FocusState.Pointer);
            _pointer = e.Pointer;
            Document.Select(HitTest(LocalPoint(_pressPoint)), HitTest(point.Position));
            _dragTimer.Start();
        }
        if (_pointer?.PointerId != e.Pointer.PointerId) return;
        _dragPoint = e.GetCurrentPoint(this).Position;
        _rootDragPoint = RootPoint(_dragPoint);
        Document.Select(Document.Anchor, HitTest(_dragPoint));
        DrawSelection();
        e.Handled = true;
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_pendingLinkPointer?.PointerId == e.Pointer.PointerId)
        { _pendingLinkPointer = null; return; }
        if (_pointer?.PointerId != e.Pointer.PointerId) return;
        _dragPoint = e.GetCurrentPoint(this).Position;
        Document.Select(Document.Anchor, HitTest(_dragPoint));
        DrawSelection();
        StopDrag();
        e.Handled = true;
    }

    private void OnCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, this)) StopDrag();
    }

    internal static bool IsMouseSelection(Microsoft.UI.Input.PointerDeviceType device) =>
        device == Microsoft.UI.Input.PointerDeviceType.Mouse;

    internal static int PointerAnchor(int anchor, int offset, VirtualKeyModifiers modifiers) =>
        (modifiers & VirtualKeyModifiers.Shift) != 0 ? anchor : offset;
    private void StopDrag()
    {
        _dragTimer.Stop();
        _pendingLinkPointer = null;
        var pointer = _pointer;
        _pointer = null;
        if (pointer is not null) ReleasePointerCapture(pointer);
    }

    private void OnDragTick(object? sender, object e)
    {
        if (_pointer is null) return;
        _dragPoint = LocalPoint(_rootDragPoint);
        var viewport = Viewport();
        double delta = _dragPoint.Y < viewport.Top ? -24 : _dragPoint.Y > viewport.Bottom ? 24 : 0;
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
        _dragPoint = LocalPoint(_rootDragPoint);
        Document.Select(Document.Anchor, HitTest(_dragPoint));
        DrawSelection();
    }

    private static bool IsDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!CanHandleKey(e.OriginalSource, e.Handled)) return;
        Refresh();
        bool extend = IsDown(VirtualKey.Shift), ctrl = IsDown(VirtualKey.Control);
        e.Handled = HandleKey(e.Key, extend, ctrl);
    }

    internal bool CanHandleKey(object source, bool handled)
    {
        if (!OwnsInput(source) || IsChrome(source)) return false;
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

    internal bool HandleKey(VirtualKey key, bool extend, bool ctrl)
    {
        switch (key)
        {
            case VirtualKey.A when ctrl:
                Document.Select(0, Document.Text.Length);
                break;
            case VirtualKey.C when ctrl:
                CopySelection();
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
        DrawSelection();
        BringCaretIntoView();
        return true;
    }

    internal void CopySelection(Action<string>? write = null)
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
        var local = marker.TransformToVisual(this).Inverse.TransformBounds(rect);
        marker.StartBringIntoView(new BringIntoViewOptions { TargetRect = local, AnimationDesired = false });
    }

    internal int HitTest(Point point, int direction = 0, double currentY = 0)
    {
        int best = 0;
        double score = double.MaxValue;
        var candidates = Document.Entries.Select(entry =>
        {
            var bounds = Bounds((MarkdownSelectionMarker)entry.Identity);
            double dy = point.Y < bounds.Top ? bounds.Top - point.Y :
                point.Y > bounds.Bottom ? point.Y - bounds.Bottom : 0;
            double dx = point.X < bounds.Left ? bounds.Left - point.X :
                point.X > bounds.Right ? point.X - bounds.Right : 0;
            return (Entry: entry, MinimumScore: dy * dy * 10000 + dx * dx);
        }).OrderBy(candidate => candidate.MinimumScore);
        foreach (var candidate in candidates)
        {
            if (candidate.MinimumScore > score) break;
            var entry = candidate.Entry;
            // No intersection with Viewport here: navigation traverses the entire message.
            for (int local = 0; local <= entry.Text.Length;)
            {
                int offset = entry.Start + local;
                var rect = CharacterBounds(offset);
                if (direction == 0 || direction * (rect.Y - currentY) > 0.5)
                {
                    double dy = point.Y < rect.Top ? rect.Top - point.Y :
                        point.Y > rect.Bottom ? point.Y - rect.Bottom : 0;
                    double dx = point.X - rect.X;
                    double distance = dy * dy * 10000 + dx * dx;
                    if (distance < score) { best = offset; score = distance; }
                }
                if (local == entry.Text.Length) break;
                int following = Document.MoveGrapheme(offset, 1) - entry.Start;
                local = Math.Min(entry.Text.Length, Math.Max(local + 1, following));
            }
        }
        return score == double.MaxValue ? Document.Caret : best;
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
            var pointer = segment.Start.GetPositionAtOffset(
                Math.Min(next - entry.Start - segment.LocalStart, segment.Text.Length), LogicalDirection.Backward);
            if (pointer is not null)
            {
                var end = segment.Owner.TransformToVisual(this).TransformBounds(
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
            TargetRect = marker.TransformToVisual(this).Inverse.TransformBounds(CharacterBounds(offset)),
            VerticalAlignmentRatio = alignToTop ? 0 : 1,
            AnimationDesired = false
        });
    }

    private Rect ComputeCharacterBounds(int offset)
    {
        var entry = Document.EntryAt(offset);
        if (entry?.Identity is not MarkdownSelectionMarker marker) return default;
        int local = Math.Clamp(offset - entry.Start, 0, entry.Text.Length);
        var found = SegmentAt(marker, local);
        if (found is { } segment)
        {
            var pointer = segment.Start.GetPositionAtOffset(
                Math.Clamp(local - segment.LocalStart, 0, segment.Text.Length), LogicalDirection.Forward);
            if (pointer is not null)
            {
                var rect = segment.Owner.TransformToVisual(this).TransformBounds(
                    pointer.GetCharacterRect(LogicalDirection.Forward));
                rect.Width = Math.Max(1, rect.Width);
                rect.Height = Math.Max(1, rect.Height);
                return rect;
            }
        }
        var bounds = Bounds(marker);
        // Explicit custom surfaces without native text still have a usable caret
        // and embedded-control range. Their text is supplied by the author, not XAML shape.
        return new Rect(bounds.X + bounds.Width * local / Math.Max(1, entry.Text.Length),
            bounds.Y, 1, Math.Max(1, bounds.Height));
    }

    private readonly record struct TextSegment(FrameworkElement Owner, string Text, TextPointer Start, Inline Identity,
        Hyperlink? Link = null, int LocalStart = 0, int Style = 0);

    private bool InvalidateNativeSegments()
    {
        bool changed = false;
        foreach (var surface in _surfaces)
        {
            if (!_segments.TryGetValue(surface, out var cached)) continue;
            int index = 0;
            bool matches = true;
            foreach (var current in TextSegments(surface))
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

    private TextSegment[] Segments(MarkdownSelectionMarker marker)
    {
        if (!_segments.TryGetValue(marker, out var segments))
        {
            int offset = 0;
            _segments[marker] = segments = TextSegments(marker).Where(segment => segment.Text.Length > 0)
                .Select(segment =>
                {
                    var indexed = segment with { LocalStart = offset };
                    offset += segment.Text.Length;
                    return indexed;
                }).ToArray();
        }
        return segments;
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
                foreach (var segment in TextSegments(child)) yield return segment;
        }
    }

    private void DrawSelection()
    {
        // Keep layout stable: the overlay is non-hit-testable and never participates
        // in document registration. Only selected, viewport-intersecting cells are drawn.
        var rectangles = new List<Rect>();
        var viewport = Viewport();
        if (Document.Start == Document.End && FocusState != FocusState.Unfocused && Document.Text.Length > 0)
        {
            var caret = Intersect(CharacterBounds(Document.Caret), viewport);
            if (caret.Width > 0 && caret.Height > 0) rectangles.Add(caret);
        }
        foreach (var (offset, next) in VisibleGlyphs(Document.Start, Document.End))
        {
            var rect = GlyphBounds(offset, next);
            rect = Intersect(rect, viewport);
            if (rect.Width > 0 && rect.Height > 0)
            {
                if (rectangles.Count > 0 && Math.Abs(rectangles[^1].Y - rect.Y) < 0.5 &&
                    rect.Left <= rectangles[^1].Right + 2 && rect.Right >= rectangles[^1].Left - 2)
                {
                    var previous = rectangles[^1];
                    rectangles[^1] = new Rect(Math.Min(previous.Left, rect.Left), previous.Y,
                        Math.Max(previous.Right, rect.Right) - Math.Min(previous.Left, rect.Left), previous.Height);
                }
                else rectangles.Add(rect);
            }
        }
        while (_overlay.Children.Count > rectangles.Count) _overlay.Children.RemoveAt(_overlay.Children.Count - 1);
        for (int i = 0; i < rectangles.Count; i++)
        {
            if (_overlay.Children.Count <= i)
                _overlay.Children.Add(new Rectangle { Opacity = .35 });
            var shape = (Rectangle)_overlay.Children[i];
            shape.Fill = SelectionBrush(Document.Start);
            var rect = rectangles[i];
            if (shape.Width != rect.Width) shape.Width = rect.Width;
            if (shape.Height != rect.Height) shape.Height = rect.Height;
            if (Canvas.GetLeft(shape) != rect.X) Canvas.SetLeft(shape, rect.X);
            if (Canvas.GetTop(shape) != rect.Y) Canvas.SetTop(shape, rect.Y);
        }
    }

    internal void Select(int start, int end)
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

    protected override AutomationPeer OnCreateAutomationPeer() => new MarkdownSelectionAutomationPeer(this);
}
