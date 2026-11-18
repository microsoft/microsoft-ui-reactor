// Adapted from OpenClaw's ChatHistoryNavigation; see OpenClaw-LICENSE.txt.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace WinUIGalleryReactor.ControlPages.Collections;

internal sealed record HistoryEntry(string Key, string Sender, string Preview);

internal sealed class OpenClawHistoryRail : IDisposable
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "OpenClawHistoryRail", typeof(object), typeof(OpenClawHistoryRail), new PropertyMetadata(null));
    private readonly AnnotatedScrollBar _bar;
    private readonly PointerEventHandler _pointer;
    private HistoryEntry[] _entries = [];
    private (HistoryEntry Entry, int Row)[] _ticks = [];
    private ItemsView? _items;
    private ScrollView? _scroll;
    private Action<string>? _navigate;
    private string? _selectedKey;
    private int _hovered = -1;
    private bool _painting;
    private bool _disposed;
    private int _navigationVersion;
    private double _height;
    private double _scale;
    private int _presenterCount = -1;

    private OpenClawHistoryRail(AnnotatedScrollBar bar)
    {
        _bar = bar;
        _pointer = OnPointer;
        bar.Scrolling += OnScrolling;
        bar.DetailLabelRequested += OnDetail;
        bar.KeyDown += OnKeyDown;
        bar.PointerExited += OnPointerExited;
        bar.Loaded += OnLoaded;
        bar.Unloaded += OnUnloaded;
        bar.LayoutUpdated += OnLayout;
        bar.AddHandler(UIElement.PointerMovedEvent, _pointer, true);
        bar.AddHandler(UIElement.PointerPressedEvent, _pointer, true);
    }

    internal static void Update(AnnotatedScrollBar bar, HistoryEntry[] entries, ItemsView? items, Action<string> navigate)
    {
        OpenClawChatStyles.HistoryBar(bar);
        var state = bar.GetValue(StateProperty) as OpenClawHistoryRail;
        if (state is null)
        {
            state = new(bar);
            bar.SetValue(StateProperty, state);
        }
        state._entries = entries;
        state._items = items;
        state._navigate = navigate;
        state.AttachScroll();
        state.Refresh();
    }

    private void AttachScroll()
    {
        var next = _items?.ScrollView;
        if (ReferenceEquals(_scroll, next)) return;
        if (_scroll is not null) _scroll.ViewChanged -= OnViewChanged;
        _scroll = next;
        if (_scroll is not null) _scroll.ViewChanged += OnViewChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs args) { AttachScroll(); Refresh(); }
    private void OnUnloaded(object sender, RoutedEventArgs args) => CancelPaint();
    private void OnViewChanged(ScrollView sender, object args) => QueuePaint();
    private Grid? Labels => Find<Grid>(_bar, "PART_LabelsGrid");

    private void OnLayout(object? sender, object args)
    {
        var labels = Labels;
        if (labels?.ActualHeight != _height || (_bar.XamlRoot?.RasterizationScale ?? 1) != _scale)
            Refresh();
        else if (labels?.Children.Count != _presenterCount)
            QueuePaint();
    }

    private void Refresh()
    {
        _height = Labels?.ActualHeight ?? 0;
        _scale = _bar.XamlRoot?.RasterizationScale ?? 1;
        var tickPixels = Math.Round(2 * _scale, MidpointRounding.AwayFromZero);
        var pitch = tickPixels + Math.Ceiling(4 * _scale);
        var capacity = _height * _scale < tickPixels ? 0
            : 1 + (int)Math.Floor((Math.Floor(_height * _scale) - tickPixels) / pitch);
        var count = Math.Min(_entries.Length, capacity);
        var ticks = new (HistoryEntry Entry, int Row)[count];
        var replace = count != _ticks.Length || _bar.Labels.Count != count;
        for (var index = 0; index < count; index++)
        {
            var row = count == 1 ? _entries.Length - 1
                : (int)Math.Round((double)index * (_entries.Length - 1) / (count - 1));
            ticks[index] = (_entries[row], row);
            replace |= index >= _ticks.Length || _ticks[index].Entry.Key != ticks[index].Entry.Key;
        }
        _ticks = ticks;
        if (replace)
        {
            _bar.Labels.Clear();
            for (var index = 0; index < count; index++)
                _bar.Labels.Add(new AnnotatedScrollBarLabel(ticks[index].Entry.Key,
                    count == 1 ? 0.5 : (double)index / (count - 1)));
        }
        QueuePaint();
    }

    private int TickAt(double offset) => _ticks.Length == 0 || !double.IsFinite(offset) ? -1
        : (int)Math.Round(Math.Clamp(offset, 0, 1) * (_ticks.Length - 1));

    private void OnScrolling(AnnotatedScrollBar sender, AnnotatedScrollBarScrollingEventArgs args)
    {
        args.Cancel = true;
        var index = TickAt(args.ScrollOffset);
        if (index >= 0) Request(_ticks[index].Entry);
    }

    private void OnPointer(object sender, PointerRoutedEventArgs args)
    {
        if (Labels is not { ActualHeight: > 2 } labels) return;
        _hovered = TickAt(args.GetCurrentPoint(labels).Position.Y / (labels.ActualHeight - 2));
        if (_hovered >= 0 && Find<ContentPresenter>(_bar, "PART_TooltipContentPresenter") is { } preview)
            preview.Content = Preview(_ticks[_hovered].Entry);
        QueuePaint();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs args) { _hovered = -1; QueuePaint(); }

    private void OnDetail(AnnotatedScrollBar sender, AnnotatedScrollBarDetailLabelRequestedEventArgs args)
    {
        var index = TickAt(args.ScrollOffset);
        args.Content = index < 0 ? null : Preview(_ticks[index].Entry);
    }

    private static StackPanel Preview(HistoryEntry entry)
    {
        var panel = new StackPanel { Spacing = 4, MaxWidth = 280 };
        panel.Children.Add(new TextBlock
        {
            Text = entry.Sender, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var length = Math.Min(240, entry.Preview.Length);
        if (length < entry.Preview.Length && length > 0 && char.IsHighSurrogate(entry.Preview[length - 1])) length--;
        panel.Children.Add(new TextBlock
        {
            Text = entry.Preview[..length], Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return panel;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || _entries.Length == 0) return;
        var selected = Array.FindIndex(_entries, entry => entry.Key == _selectedKey);
        if (selected < 0) selected = VisibleRows().DefaultIfEmpty(_entries.Length - 1).Min();
        var index = args.Key switch
        {
            VirtualKey.Home => 0,
            VirtualKey.End => _entries.Length - 1,
            VirtualKey.Up => Math.Max(0, selected - 1),
            VirtualKey.Down => Math.Min(_entries.Length - 1, selected + 1),
            VirtualKey.Enter or VirtualKey.Space => selected,
            _ => -1,
        };
        if (index < 0) return;
        args.Handled = true;
        Request(_entries[index]);
    }

    private void Request(HistoryEntry entry)
    {
        _selectedKey = entry.Key;
        AutomationProperties.SetItemStatus(_bar, $"{entry.Sender}: {entry.Preview}");
        var version = ++_navigationVersion;
        if (!_bar.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed && version == _navigationVersion && _entries.Any(item => item.Key == entry.Key))
                _navigate?.Invoke(entry.Key);
        }))
            System.Diagnostics.Trace.TraceWarning("OpenClaw history navigation could not be dispatched.");
    }

    private HashSet<int> VisibleRows()
    {
        var rows = new HashSet<int>();
        if (_scroll?.Content is not ItemsRepeater repeater) return rows;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(repeater); index++)
        {
            if (VisualTreeHelper.GetChild(repeater, index) is not FrameworkElement row) continue;
            var itemIndex = repeater.GetElementIndex(row);
            if (itemIndex < 0) continue;
            var bounds = row.TransformToVisual(_scroll).TransformBounds(
                new global::Windows.Foundation.Rect(0, 0, row.ActualWidth, row.ActualHeight));
            if (bounds.Bottom > 0 && bounds.Top < _scroll.ViewportHeight) rows.Add(itemIndex);
        }
        return rows;
    }

    private void QueuePaint()
    {
        if (_painting || !_bar.IsLoaded || _disposed) return;
        _painting = true;
        CompositionTarget.Rendering += Paint;
    }

    private void Paint(object? sender, object args)
    {
        CancelPaint();
        if (Labels is not { } labels) return;
        var visible = VisibleRows();
        for (var index = 0; index < Math.Min(labels.Children.Count, _ticks.Length); index++)
        {
            var tick = Find<ContentControl>(labels.Children[index]);
            if (tick is null) { _presenterCount = -1; return; }
            tick.MaxWidth = Math.Max(0, _bar.ActualWidth - 8);
            var distance = _hovered < 0 ? -1 : Math.Abs(index - _hovered);
            VisualStateManager.GoToState(tick, distance switch
            {
                0 => "PointerOver", 1 => "HoverAdjacent", 2 => "HoverNear", 3 => "HoverFar", _ => "Normal",
            }, false);
            VisualStateManager.GoToState(tick, distance == 0 ? "Highlighted"
                : visible.Contains(_ticks[index].Row) ? "Visible" : "Quiet", false);
        }
        _presenterCount = labels.Children.Count;
    }

    private void CancelPaint()
    {
        if (_painting) CompositionTarget.Rendering -= Paint;
        _painting = false;
    }

    internal static T? Find<T>(DependencyObject root, string? name = null) where T : FrameworkElement
    {
        if (root is T match && (name is null || match.Name == name)) return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (Find<T>(VisualTreeHelper.GetChild(root, index), name) is { } found) return found;
        return null;
    }

    internal static void Detach(FrameworkElement control)
    {
        (control.GetValue(StateProperty) as OpenClawHistoryRail)?.Dispose();
        control.ClearValue(StateProperty);
    }

    public void Dispose()
    {
        _disposed = true;
        _navigate = null;
        CancelPaint();
        if (_scroll is not null) _scroll.ViewChanged -= OnViewChanged;
        _bar.Scrolling -= OnScrolling;
        _bar.DetailLabelRequested -= OnDetail;
        _bar.KeyDown -= OnKeyDown;
        _bar.PointerExited -= OnPointerExited;
        _bar.Loaded -= OnLoaded;
        _bar.Unloaded -= OnUnloaded;
        _bar.LayoutUpdated -= OnLayout;
        _bar.RemoveHandler(UIElement.PointerMovedEvent, _pointer);
        _bar.RemoveHandler(UIElement.PointerPressedEvent, _pointer);
    }
}
