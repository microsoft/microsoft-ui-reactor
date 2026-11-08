using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ReactorRegedit.Core;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace ReactorRegedit.Native;

/// <summary>A row of the value list (reference identity drives selection preservation).</summary>
internal sealed class ValueItem(RegValue value)
{
    public RegValue Value { get; set; } = value;
    public override string ToString() => Value.DisplayName;
}

/// <summary>Callbacks the value list raises into the Reactor shell.</summary>
internal sealed class ValueListHandlers
{
    public Action<RegValue, bool>? ModifyRequested { get; init; }
    public Action<IReadOnlyList<RegValue>>? DeleteRequested { get; init; }
    /// <summary>Performs a rename; returns true on success.</summary>
    public Func<string, string, Task<bool>>? RenameRequested { get; init; }
    public Func<IReadOnlyList<RegValue>, IEnumerable<MenuSpec>>? BuildItemMenu { get; init; }
    public Func<IEnumerable<MenuSpec>>? BuildBackgroundMenu { get; init; }
    public Action<int>? SelectionChanged { get; init; }
    public Action? FocusEntered { get; init; }
    public Action<double, double>? ColumnsResized { get; init; }
}

/// <summary>
/// The value pane: a resizable, sortable Name/Type/Data header over a virtualized,
/// multi-select ListView with compact 22 epx rows.
/// </summary>
internal sealed partial class ValueListView : Grid
{
    public const double RowHeight = 22;
    private const double HeaderHeight = 24;
    private const double MinColumn = 40;

    private readonly ListView _list;
    private readonly RowCollection<ValueItem> _rows = [];
    private readonly Grid _header;
    private readonly Button[] _headerButtons = new Button[3];
    private readonly FontIcon[] _sortGlyphs = new FontIcon[3];
    private readonly DispatcherQueue _dispatcher;
    private double _nameWidth = 220;
    private double _typeWidth = 190;
    private ValueSortColumn _sortColumn = ValueSortColumn.Name;
    private bool _sortDescending;
    private string _keyPath = "\0";
    private RegValue[] _source = [];
    private bool _renaming;
    private int _suppressSelection;

    public ValueListHandlers Handlers { get; set; } = new();

    public ValueListView()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _header = BuildHeader();
        Children.Add(_header);

        _list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Extended,
            ItemsSource = _rows,
            ItemTemplate = ListHelpers.EmptyGridTemplate(),
            ItemContainerStyle = ListHelpers.CompactItemStyle(RowHeight),
            Padding = new Thickness(0, 2, 0, 2),
        };
        AutomationProperties.SetName(_list, "Registry values");
        ListHelpers.ApplySelectionBrushes(_list);
        Grid.SetRow(_list, 1);
        _list.ContainerContentChanging += OnContainerContentChanging;
        _list.PreviewKeyDown += OnPreviewKeyDown;
        _list.DoubleTapped += OnDoubleTapped;
        _list.ContextRequested += OnContextRequested;
        _list.RightTapped += OnRightTapped;
        _list.SelectionChanged += (_, _) =>
        {
            if (_suppressSelection == 0) Handlers.SelectionChanged?.Invoke(_list.SelectedItems.Count);
        };
        _list.GotFocus += (_, _) => Handlers.FocusEntered?.Invoke();
        Children.Add(_list);
        UpdateSortGlyphs();
    }

    public string KeyPath => _keyPath;
    public double NameWidth => _nameWidth;
    public double TypeWidth => _typeWidth;

    public void SetColumnWidths(double name, double type)
    {
        _nameWidth = Math.Max(MinColumn, name);
        _typeWidth = Math.Max(MinColumn, type);
        ApplyColumnWidths();
    }

    public IReadOnlyList<RegValue> SelectedValues =>
        _list.SelectedItems.OfType<ValueItem>().Select(i => i.Value).ToList();

    /// <summary>The focused row if selected, else the first selected row.</summary>
    public RegValue? PrimarySelection
    {
        get
        {
            var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (ListHelpers.FindAncestor<ListViewItem>(focused) is { IsSelected: true } c &&
                _list.ItemFromContainer(c) is ValueItem vi)
                return vi.Value;
            return (_list.SelectedItem as ValueItem)?.Value;
        }
    }

    public bool HasFocusWithin()
    {
        if (XamlRoot is null) return false;
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        return ListHelpers.FindAncestor<ValueListView>(focused) == this;
    }

    public void FocusList()
    {
        if (_list.SelectedItem is { } sel && _list.ContainerFromItem(sel) is ListViewItem c)
            c.Focus(FocusState.Programmatic);
        else
            _list.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Shows <paramref name="values"/> for <paramref name="keyPath"/>. Selection is kept
    /// (by value name) when the key is unchanged, cleared otherwise.
    /// </summary>
    public void SetValues(string keyPath, RegValue[] values)
    {
        if (ReferenceEquals(values, _source) && keyPath == _keyPath) return;
        var sameKey = string.Equals(keyPath, _keyPath, StringComparison.OrdinalIgnoreCase);
        var keepNames = sameKey
            ? new HashSet<string>(SelectedValues.Select(v => v.Name), StringComparer.OrdinalIgnoreCase)
            : [];
        var focusedName = sameKey ? PrimarySelection?.Name : null;
        _keyPath = keyPath;
        _source = values;
        Rebuild(keepNames, focusedName);
    }

    private void Rebuild(HashSet<string> keepNames, string? focusedName)
    {
        var hadFocus = HasFocusWithin();
        var sorted = ValueSort.Sort(_source, _sortColumn, _sortDescending);
        var existing = _rows.ToDictionary(r => r.Value.Name, StringComparer.OrdinalIgnoreCase);
        var items = new List<ValueItem>(sorted.Length);
        foreach (var v in sorted)
        {
            if (existing.TryGetValue(v.Name, out var item) && item.Value.Name == v.Name)
            {
                item.Value = v;
                items.Add(item);
            }
            else items.Add(new ValueItem(v));
        }

        _suppressSelection++;
        try
        {
            _rows.ResetTo(items);
            foreach (var item in items)
                if (keepNames.Contains(item.Value.Name)) _list.SelectedItems.Add(item);
        }
        finally { _suppressSelection--; }
        Handlers.SelectionChanged?.Invoke(_list.SelectedItems.Count);

        if (focusedName is not null && hadFocus)
        {
            var target = items.FirstOrDefault(i => i.Value.Name.Equals(focusedName, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                _list.ScrollIntoView(target);
                _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    if (_list.ContainerFromItem(target) is ListViewItem c) c.Focus(FocusState.Programmatic);
                });
            }
        }
    }

    /// <summary>Selects (only) the named value, scrolls to it and optionally focuses it.</summary>
    public void SelectByName(string name, bool focus)
    {
        var item = _rows.FirstOrDefault(i => i.Value.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        _list.SelectedItems.Clear();
        _list.SelectedItem = item;
        _list.ScrollIntoView(item);
        if (focus)
            _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_list.ContainerFromItem(item) is ListViewItem c) c.Focus(FocusState.Programmatic);
            });
    }

    // ── Header ───────────────────────────────────────────────────────

    private Grid BuildHeader()
    {
        var header = new Grid { Height = HeaderHeight };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_nameWidth) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_typeWidth) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.BorderThickness = new Thickness(0, 0, 0, 1);
        header.BorderBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];

        string[] titles = [Strings.ColumnName, Strings.ColumnType, Strings.ColumnData];
        for (int i = 0; i < 3; i++)
        {
            var col = (ValueSortColumn)i;
            var glyph = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 8,
                Margin = new Thickness(6, 0, 0, 0),
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };
            _sortGlyphs[i] = glyph;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = titles[i], FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(glyph);
            var button = new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinHeight = 0,
                Padding = new Thickness(8, 0, 8, 0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0, 0, 1, 0),
                BorderBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                CornerRadius = new CornerRadius(0),
            };
            button.Click += (_, _) => SortBy(col);
            _headerButtons[i] = button;
            Grid.SetColumn(button, i);
            header.Children.Add(button);

            if (i < 2)
            {
                var grip = new ResizeGrip { Width = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, -4, 0) };
                Canvas.SetZIndex(grip, 1);
                Grid.SetColumn(grip, i);
                WireGrip(grip, i);
                header.Children.Add(grip);
            }
        }
        return header;
    }

    private void WireGrip(ResizeGrip grip, int column)
    {
        bool dragging = false;
        double startX = 0, startWidth = 0;
        grip.PointerPressed += (s, e) =>
        {
            dragging = grip.CapturePointer(e.Pointer);
            startX = e.GetCurrentPoint(this).Position.X;
            startWidth = column == 0 ? _nameWidth : _typeWidth;
            e.Handled = true;
        };
        grip.PointerMoved += (s, e) =>
        {
            if (!dragging) return;
            var w = Math.Max(MinColumn, startWidth + e.GetCurrentPoint(this).Position.X - startX);
            if (column == 0) _nameWidth = w; else _typeWidth = w;
            ApplyColumnWidths();
            e.Handled = true;
        };
        void End(PointerRoutedEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            grip.ReleasePointerCapture(e.Pointer);
            Handlers.ColumnsResized?.Invoke(_nameWidth, _typeWidth);
            e.Handled = true;
        }
        grip.PointerReleased += (_, e) => End(e);
        grip.PointerCaptureLost += (_, _) => dragging = false;
        grip.DoubleTapped += (_, e) =>
        {
            // Double-click a divider: fit the column to its content.
            AutoFit(column);
            e.Handled = true;
        };
    }

    private void AutoFit(int column)
    {
        double max = 60;
        var probe = new TextBlock { FontSize = 13 };
        foreach (var item in _rows.Take(2000))
        {
            probe.Text = column == 0 ? item.Value.DisplayName : item.Value.TypeName;
            probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            max = Math.Max(max, probe.DesiredSize.Width + (column == 0 ? 40 : 20));
        }
        if (column == 0) _nameWidth = Math.Min(max, 600); else _typeWidth = Math.Min(max, 400);
        ApplyColumnWidths();
        Handlers.ColumnsResized?.Invoke(_nameWidth, _typeWidth);
    }

    private void ApplyColumnWidths()
    {
        _header.ColumnDefinitions[0].Width = new GridLength(_nameWidth);
        _header.ColumnDefinitions[1].Width = new GridLength(_typeWidth);
        if (_list.ItemsPanelRoot is { } panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is ListViewItem { ContentTemplateRoot: Grid root } && root.ColumnDefinitions.Count == 3)
                {
                    root.ColumnDefinitions[0].Width = new GridLength(_nameWidth - 2);
                    root.ColumnDefinitions[1].Width = new GridLength(_typeWidth);
                }
            }
        }
    }

    private void SortBy(ValueSortColumn column)
    {
        if (_sortColumn == column) _sortDescending = !_sortDescending;
        else { _sortColumn = column; _sortDescending = false; }
        UpdateSortGlyphs();
        var keep = new HashSet<string>(SelectedValues.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
        Rebuild(keep, null);
    }

    private void UpdateSortGlyphs()
    {
        string[] titles = [Strings.ColumnName, Strings.ColumnType, Strings.ColumnData];
        for (int i = 0; i < 3; i++)
        {
            var active = (int)_sortColumn == i;
            _sortGlyphs[i].Glyph = active ? (_sortDescending ? "\uE70D" : "\uE70E") : "";
            AutomationProperties.SetName(_headerButtons[i],
                active ? $"{titles[i]}, sorted {(_sortDescending ? "descending" : "ascending")}" : $"{titles[i]}, sort");
        }
    }

    // ── Rows ─────────────────────────────────────────────────────────

    private sealed class RowParts
    {
        public required FrameworkElement StringIcon, BinaryIcon;
        public required TextBlock Name, Type, Data;
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        var value = ((ValueItem)args.Item).Value;
        var root = (Grid)args.ItemContainer.ContentTemplateRoot;
        if (root.Tag is not RowParts parts)
        {
            parts = BuildRow(root);
            root.Tag = parts;
        }
        root.ColumnDefinitions[0].Width = new GridLength(_nameWidth - 2);
        root.ColumnDefinitions[1].Width = new GridLength(_typeWidth);

        var isString = RegType.IsStringFamily(value.Type);
        parts.StringIcon.Visibility = isString ? Visibility.Visible : Visibility.Collapsed;
        parts.BinaryIcon.Visibility = isString ? Visibility.Collapsed : Visibility.Visible;
        parts.Name.Text = value.DisplayName;
        parts.Type.Text = value.TypeName;
        var data = value.DisplayData;
        parts.Data.Text = data;
        ToolTipService.SetToolTip(parts.Data, data.Length > 40 ? data[..Math.Min(data.Length, 1024)] : null);
        AutomationProperties.SetName(args.ItemContainer, $"{value.DisplayName}, {value.TypeName}, {(data.Length > 200 ? data[..200] : data)}");
        args.Handled = true;
    }

    private static RowParts BuildRow(Grid root)
    {
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var nameCell = new Grid { Margin = new Thickness(4, 0, 6, 0) };
        nameCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        nameCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icons = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        var sIcon = Icons.Value(true);
        var bIcon = Icons.Value(false);
        icons.Children.Add(sIcon);
        icons.Children.Add(bIcon);
        nameCell.Children.Add(icons);
        var name = Cell();
        Grid.SetColumn(name, 1);
        nameCell.Children.Add(name);
        root.Children.Add(nameCell);

        var type = Cell();
        type.Margin = new Thickness(8, 0, 6, 0);
        Grid.SetColumn(type, 1);
        root.Children.Add(type);

        var data = Cell();
        data.Margin = new Thickness(8, 0, 6, 0);
        Grid.SetColumn(data, 2);
        root.Children.Add(data);

        return new RowParts { StringIcon = sIcon, BinaryIcon = bIcon, Name = name, Type = type, Data = data };
    }

    private static TextBlock Cell() => new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        FontSize = 13,
        MaxLines = 1,
    };

    // ── Inline rename ────────────────────────────────────────────────

    public async void BeginRename(string name)
    {
        var item = _rows.FirstOrDefault(i => i.Value.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (item is null || item.Value.IsDefault || _renaming) return;
        _list.SelectedItems.Clear();
        _list.SelectedItem = item;
        _list.ScrollIntoView(item);
        _list.UpdateLayout();
        await Task.Yield();
        if (_list.ContainerFromItem(item) is not ListViewItem container ||
            container.ContentTemplateRoot is not Grid { Tag: RowParts parts } root)
            return;

        _renaming = true;
        var editor = ListHelpers.InlineEditor(item.Value.Name);
        editor.Margin = new Thickness(24, 0, 2, 0);
        parts.Name.Visibility = Visibility.Collapsed;
        root.Children.Add(editor);
        bool done = false;
        var oldName = item.Value.Name;

        async void Finish(bool commit)
        {
            if (done) return;
            done = true;
            var newName = editor.Text;
            root.Children.Remove(editor);
            parts.Name.Visibility = Visibility.Visible;
            _renaming = false;
            if (commit && newName != oldName && Handlers.RenameRequested is { } rename)
                await rename(oldName, newName);
            FocusList();
        }

        editor.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { e.Handled = true; Finish(true); }
            else if (e.Key == VirtualKey.Escape) { e.Handled = true; Finish(false); }
        };
        editor.LostFocus += (_, _) => Finish(true);
        editor.Focus(FocusState.Programmatic);
        editor.SelectAll();
    }

    // ── Input ────────────────────────────────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        var ctrlDown = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                        & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        switch (e.Key)
        {
            case VirtualKey.Enter when PrimarySelection is { } v:
                e.Handled = true;
                Handlers.ModifyRequested?.Invoke(v, false);
                break;
            case VirtualKey.Delete when !ctrlDown:
                var sel = SelectedValues;
                if (sel.Count > 0)
                {
                    e.Handled = true;
                    Handlers.DeleteRequested?.Invoke(sel);
                }
                break;
            case VirtualKey.F2 when PrimarySelection is { } r:
                e.Handled = true;
                BeginRename(r.Name);
                break;
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ListHelpers.FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) is { } c &&
            _list.ItemFromContainer(c) is ValueItem vi)
        {
            e.Handled = true;
            Handlers.ModifyRequested?.Invoke(vi.Value, false);
        }
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // ContextRequested covers items; right-clicking empty space lands here.
        if (e.Handled) return;
        if (ListHelpers.FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) is not null) return;
        e.Handled = true;
        ShowBackgroundMenu(e.GetPosition(_list));
    }

    private void ShowBackgroundMenu(Windows.Foundation.Point? at)
    {
        if (Handlers.BuildBackgroundMenu is not { } build) return;
        _list.SelectedItems.Clear();
        var flyout = ListHelpers.BuildMenu(build());
        if (at is { } p)
            flyout.ShowAt(_list, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = p });
        else
            flyout.ShowAt(_list);
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        var container = ListHelpers.FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        if (container is null || _list.ItemFromContainer(container) is not ValueItem item)
        {
            e.Handled = true;
            ShowBackgroundMenu(e.TryGetPosition(_list, out var p) ? p : null);
            return;
        }
        e.Handled = true;
        if (!container.IsSelected)
        {
            _list.SelectedItems.Clear();
            _list.SelectedItem = item;
        }
        if (Handlers.BuildItemMenu is not { } build) return;
        var flyout = ListHelpers.BuildMenu(build(SelectedValues));
        if (e.TryGetPosition(container, out var pt))
            flyout.ShowAt(container, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = pt });
        else
            flyout.ShowAt(container);
    }
}
