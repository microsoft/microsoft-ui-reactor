using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Tabular;
using Microsoft.UI.Xaml.Data;
using WinUITableView = Microsoft.UI.Xaml.Controls.Tabular.TableView;
using SelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs;

namespace Microsoft.UI.Reactor.Advanced.Tabular;

/// <summary>
/// V1 handler that mounts, updates, and unmounts <see cref="TableViewElement"/> instances
/// onto the WinUI <see cref="WinUITableView"/>.
/// </summary>
/// <remarks>
/// Columns are reconciled by position: a column whose record type is unchanged is updated
/// in place (so the control keeps its width, sort indicator and realized cells); any type
/// change, insertion or removal rebuilds the native column list. Template cells are
/// Reactor subtrees hosted by <see cref="ReactorDataTemplate"/>.
/// </remarks>
public sealed partial class TableViewHandler : IElementHandler<TableViewElement, WinUITableView>
{
    private static readonly ConditionalWeakTable<WinUITableView, TableState> s_states = new();

    /// <inheritdoc />
    public WinUITableView Mount(MountContext ctx, TableViewElement el)
    {
        ExperimentalControlResources.EnsureMerged<TabularControlsResources>();

        var table = new WinUITableView();
        Reconciler.SetElementTag(table, el);
        var state = new TableState(ctx.Reconciler, ctx.RequestRerender);
        s_states.AddOrUpdate(table, state);

        ApplyProperties(table, state, null, el);
        SyncColumns(table, state, el.Columns);
        SyncItems(table, state, el);

        var bind = ctx.BindFor(table, el);
        bind.OnCustomEvent<SelectionChangedEventArgs>(
            subscribe: static (c, h) => ((WinUITableView)c).SelectionChanged += (s, a) => h(s, a),
            unsubscribe: static (_, _) => { },
            handler: (cur, _) =>
            {
                var index = table.SelectedIndex;
                // A controlled write (Select/DeselectAll in ApplySelection) echoes back here
                // with the value the element already holds; that is not user input.
                if (cur.SelectedIndex is int controlled && controlled == index) return;
                cur.OnSelectionChanged?.Invoke(index, table.SelectedItem);
            });
        bind.OnCustomEvent<TableViewSortingEventArgs>(
            subscribe: static (c, h) => ((WinUITableView)c).Sorting += (s, a) => h(s, a),
            unsubscribe: static (_, _) => { },
            handler: static (cur, args) => cur.OnSorting?.Invoke(args));
        bind.OnCustomEvent<TableViewSortedEventArgs>(
            subscribe: static (c, h) => ((WinUITableView)c).Sorted += (s, a) => h(s, a),
            unsubscribe: static (_, _) => { },
            handler: static (cur, args) => cur.OnSorted?.Invoke(args));
        bind.OnCustomEvent<TableViewBeginningEditEventArgs>(
            subscribe: static (c, h) => ((WinUITableView)c).BeginningEdit += (s, a) => h(s, a),
            unsubscribe: static (_, _) => { },
            handler: (cur, args) =>
            {
                cur.OnBeginningEdit?.Invoke(args);
                // Remembered for TableTextColumn's converter: the edit writes back through
                // ConvertBack, which runs after CellEditEnding and is not told the row.
                state.EditingItem = args.Cancel ? null : args.Item;
                state.EditingColumn = args.Cancel ? null : args.Column;
            });
        bind.OnCustomEvent<TableViewCellEditEndingEventArgs>(
            subscribe: static (c, h) => ((WinUITableView)c).CellEditEnding += (s, a) => h(s, a),
            unsubscribe: static (_, _) => { },
            handler: (cur, args) =>
            {
                cur.OnCellEditEnding?.Invoke(args);
                if (args.EditAction != TableViewEditAction.Commit || args.Cancel)
                {
                    state.EditingItem = null;
                    state.EditingColumn = null;
                }
            });

        // Select() before the control is loaded is dropped, so the initial controlled
        // write waits for Loaded and reads the element current at that point.
        if (el.SelectedIndex is not null)
        {
            void OnLoaded(object sender, RoutedEventArgs e)
            {
                table.Loaded -= OnLoaded;
                if (Reconciler.GetElementTag(table) is TableViewElement current)
                    ApplySelection(table, current.SelectedIndex);
            }
            table.Loaded += OnLoaded;
        }
        ctx.ApplySetters(el.Setters, table);
        return table;
    }

    /// <inheritdoc />
    public void Update(UpdateContext ctx, TableViewElement oldEl, TableViewElement newEl, WinUITableView table)
    {
        Reconciler.SetElementTag(table, newEl);
        if (!s_states.TryGetValue(table, out var state))
            return;

        ApplyProperties(table, state, oldEl, newEl);
        SyncColumns(table, state, newEl.Columns);
        SyncItems(table, state, newEl);
        if (newEl.SelectedIndex != oldEl.SelectedIndex || !ReferenceEquals(oldEl.ItemsSource, newEl.ItemsSource))
            ApplySelection(table, newEl.SelectedIndex);
        ctx.ApplySetters(newEl.Setters, table);
    }

    /// <inheritdoc />
    public void Unmount(UnmountContext ctx, WinUITableView table)
    {
        if (!s_states.TryGetValue(table, out var state)) return;
        foreach (var column in state.Columns)
            column.UnmountTemplates();
        state.Empty?.UnmountAll();
        state.GroupHeader?.UnmountAll();
        s_states.Remove(table);
    }

    private static void ApplyProperties(WinUITableView t, TableState state, TableViewElement? o, TableViewElement n)
    {
        var mount = o is null;
        PropertyDiff.Write(t, static () => WinUITableView.SelectionModeProperty, o?.SelectionMode, n.SelectionMode, mount, v => t.SelectionMode = v!.Value);
        PropertyDiff.Write(t, static () => WinUITableView.DensityProperty, o?.Density, n.Density, mount, v => t.Density = v!.Value);
        PropertyDiff.Write(t, static () => WinUITableView.GridLinesVisibilityProperty, o?.GridLinesVisibility, n.GridLinesVisibility, mount, v => t.GridLinesVisibility = v!.Value);
        PropertyDiff.Write(t, static () => WinUITableView.HeadersVisibilityProperty, o?.HeadersVisibility, n.HeadersVisibility, mount, v => t.HeadersVisibility = v!.Value);
        PropertyDiff.Write(t, static () => WinUITableView.CanUserSortColumnsProperty, o?.CanUserSortColumns, n.CanUserSortColumns, mount, v => t.CanUserSortColumns = v!.Value);
        PropertyDiff.Write(t, static () => WinUITableView.CanUserResizeColumnsProperty, o?.CanUserResizeColumns, n.CanUserResizeColumns, mount, v => t.CanUserResizeColumns = v!.Value);
        PropertyDiff.Write(t, static () => WinUITableView.IsReadOnlyProperty, o is null ? null : EffectiveTableReadOnly(o), EffectiveTableReadOnly(n), mount, v => t.IsReadOnly = v!.Value);
        PropertyDiff.Write(t, static () => WinUITableView.RowBackgroundProperty, o?.RowBackground, n.RowBackground, mount, v => t.RowBackground = v);
        PropertyDiff.Write(t, static () => WinUITableView.AlternatingRowBackgroundProperty, o?.AlternatingRowBackground, n.AlternatingRowBackground, mount, v => t.AlternatingRowBackground = v);

        // EmptyContent: a template whose content ignores the item.
        if (n.EmptyContent is { } empty)
        {
            if (state.Empty is null)
            {
                state.Empty = new ReactorDataTemplate(state.Reconciler, state.RequestRerender, _ => empty, n);
                t.EmptyTemplate = state.Empty.Template;
            }
            else
            {
                state.Empty.SetRender(_ => empty, n);
            }
        }
        else if (state.Empty is not null)
        {
            state.Empty.UnmountAll();
            state.Empty = null;
            t.ClearValue(WinUITableView.EmptyTemplateProperty);
        }

        // GroupHeader: rendered from the TableViewGroupInfo the control hands each header.
        if (n.GroupHeader is { } groupHeader)
        {
            Func<object?, Element?> render = item => item is TableViewGroupInfo info ? groupHeader(info) : null;
            if (state.GroupHeader is null)
            {
                state.GroupHeader = new ReactorDataTemplate(state.Reconciler, state.RequestRerender, render, n);
                t.GroupHeaderTemplate = state.GroupHeader.Template;
            }
            else
            {
                state.GroupHeader.SetRender(render, n);
            }
        }
        else if (state.GroupHeader is not null)
        {
            state.GroupHeader.UnmountAll();
            state.GroupHeader = null;
            t.ClearValue(WinUITableView.GroupHeaderTemplateProperty);
        }
    }

    // The control defaults IsReadOnly to true, which would silently swallow the editor a
    // TextColumn onEdit callback or an EditingCell asks for; unless the author says otherwise,
    // a table with such a column is editable.
    private static bool? EffectiveTableReadOnly(TableViewElement el)
    {
        if (el.IsReadOnly is not null) return el.IsReadOnly;
        foreach (var column in el.Columns)
        {
            if (column.EditingCell is not null || column is TableTextColumn { OnEdit: not null })
                return false;
        }
        return null;
    }
    private static void SyncColumns(WinUITableView table, TableState state, IReadOnlyList<TableColumn> columns)
    {
        var old = state.Columns;
        var next = new List<ColumnSlot>(columns.Count);
        var structural = old.Count != columns.Count;
        for (var i = 0; i < columns.Count; i++)
        {
            var record = columns[i] ?? throw new ArgumentException($"Column {i} is null.", nameof(columns));
            ColumnSlot slot;
            if (i < old.Count && old[i].Record.GetType() == record.GetType())
            {
                slot = old[i];
                slot.Apply(record);
            }
            else
            {
                slot = ColumnSlot.Create(state, record);
                structural = true;
            }
            next.Add(slot);
        }

        if (!structural) return;

        for (var i = 0; i < old.Count; i++)
        {
            if (!next.Contains(old[i]))
                old[i].UnmountTemplates();
        }
        table.Columns.Clear();
        foreach (var slot in next)
            table.Columns.Add(slot.Native);
        state.Columns = next;
    }

    private static void SyncItems(WinUITableView table, TableState state, TableViewElement el)
    {
        // A plain snapshot (array, list, LINQ) is diffed into a collection this handler owns,
        // so replacing the snapshot touches only the rows that changed. A collection that
        // already reports its own changes is the caller's to manage.
        object? rows;
        if (el.ItemsSource is null or INotifyCollectionChanged)
        {
            state.Rows = null;
            state.RowsSnapshot = null;
            rows = el.ItemsSource;
        }
        else
        {
            state.Rows ??= new TableRows();
            if (!ReferenceEquals(state.RowsSnapshot, el.ItemsSource) || !ReferenceEquals(state.RowsKey, el.RowKey))
            {
                RowDiff.Apply(state.Rows, el.ItemsSource, el.RowKey);
                state.RowsSnapshot = el.ItemsSource;
                state.RowsKey = el.RowKey;
            }
            rows = state.Rows;
        }

        object? desired;
        if (el.Filter is null && el.GroupBy is null)
        {
            state.View = null;
            state.ViewSource = null;
            desired = rows;
        }
        else
        {
            if (state.View is null || !ReferenceEquals(state.ViewSource, rows))
            {
                state.View = rows is null ? null : TableViewSource.From(rows);
                state.ViewSource = rows;
                state.AppliedFilter = null;
                state.AppliedGroupBy = null;
            }

            if (state.View is { } view)
            {
                if (!ReferenceEquals(state.AppliedFilter, el.Filter))
                {
                    if (el.Filter is { } filter) view.Filter(item => filter(item));
                    else view.ClearFilter();
                    state.AppliedFilter = el.Filter;
                }
                if (!ReferenceEquals(state.AppliedGroupBy, el.GroupBy))
                {
                    if (el.GroupBy is { } groupBy) view.GroupBy(item => groupBy(item));
                    else view.ClearGroupBy();
                    state.AppliedGroupBy = el.GroupBy;
                }
            }
            desired = state.View;
        }

        if (!state.ItemsSourceApplied || !ReferenceEquals(state.AppliedItemsSource, desired))
        {
            table.ItemsSource = desired;
            state.AppliedItemsSource = desired;
            state.ItemsSourceApplied = true;
        }
    }

    private static void ApplySelection(WinUITableView table, int? selectedIndex)
    {
        if (selectedIndex is not int index) return;
        if (table.SelectedIndex == index) return;
        if (index < 0) table.DeselectAll();
        else table.Select(index);
    }

    private sealed class TableState(Reconciler reconciler, Action requestRerender)
    {
        public Reconciler Reconciler { get; } = reconciler;
        public Action RequestRerender { get; } = requestRerender;
        public List<ColumnSlot> Columns = new();
        public ReactorDataTemplate? Empty;
        public ReactorDataTemplate? GroupHeader;
        public TableViewSource? View;
        public object? ViewSource;
        public Func<object, bool>? AppliedFilter;
        public Func<object, object?>? AppliedGroupBy;
        public object? AppliedItemsSource;
        public bool ItemsSourceApplied;
        public TableRows? Rows;
        public object? RowsSnapshot;
        public Func<object, object?>? RowsKey;
        // The row being edited and its column, between BeginningEdit and the commit.
        public object? EditingItem;
        public TableViewColumn? EditingColumn;
    }

    private sealed class ColumnSlot
    {
        private readonly TableState _state;
        private ReactorDataTemplate? _cell;
        private ReactorDataTemplate? _editing;
        private SortComparerAdapter? _comparer;

        private ColumnSlot(TableState state, TableColumn record, TableViewColumn native)
        {
            _state = state;
            Record = record;
            Native = native;
        }

        public TableColumn Record { get; private set; }
        public TableViewColumn Native { get; }

        public static ColumnSlot Create(TableState state, TableColumn record)
        {
            TableViewColumn native = record switch
            {
                TableBoundColumn bound => new TableViewTextColumn { Binding = CreateBinding(bound.Path) },
                TableTextColumn => new TableViewTextColumn(),
                TableTemplateColumn => new TableViewTemplateColumn(),
                _ => throw new NotSupportedException($"Unsupported TableView column type '{record.GetType().Name}'."),
            };
            var slot = new ColumnSlot(state, record, native);
            if (record is TableTextColumn)
            {
                // A pathless binding through a C# converter: the native text column keeps its
                // TextBlock cell, TextBox editor and accessibility, while the text is read (and
                // edits are reported) in C# — no property path, nothing reflected.
                ((TableViewTextColumn)native).Binding = new Binding { Converter = new TextColumnConverter(slot, state) };
            }
            slot.Write(null, record);
            return slot;
        }

        public void Apply(TableColumn record)
        {
            var old = Record;
            Record = record;
            Write(old, record);
        }

        public void UnmountTemplates()
        {
            _cell?.UnmountAll();
            _editing?.UnmountAll();
        }

        private void Write(TableColumn? o, TableColumn n)
        {
            var c = Native;
            var mount = o is null;
            PropertyDiff.Write(c, static () => TableViewColumn.HeaderProperty, o?.Header, n.Header, mount, v => c.Header = v);
            PropertyDiff.Write(c, static () => TableViewColumn.HeaderToolTipProperty, o?.HeaderToolTip, n.HeaderToolTip, mount, v => c.HeaderToolTip = v);
            PropertyDiff.Write(c, static () => TableViewColumn.WidthProperty, o?.Width, n.Width, mount, v => c.Width = v!.Value);
            PropertyDiff.Write(c, static () => TableViewColumn.MinWidthProperty, o?.MinWidth, n.MinWidth, mount, v => c.MinWidth = v!.Value);
            PropertyDiff.Write(c, static () => TableViewColumn.MaxWidthProperty, o?.MaxWidth, n.MaxWidth, mount, v => c.MaxWidth = v!.Value);
            PropertyDiff.Write(c, static () => TableViewColumn.CanSortProperty, o?.CanSort, n.CanSort, mount, v => c.CanSort = v!.Value);
            PropertyDiff.Write(c, static () => TableViewColumn.CanResizeProperty, o?.CanResize, n.CanResize, mount, v => c.CanResize = v!.Value);
            PropertyDiff.Write(c, static () => TableViewColumn.IsReadOnlyProperty, o is null ? null : EffectiveReadOnly(o), EffectiveReadOnly(n), mount, v => c.IsReadOnly = v!.Value);
            PropertyDiff.Write(c, static () => TableViewColumn.FrozenEdgeProperty, o?.FrozenEdge, n.FrozenEdge, mount, v => c.FrozenEdge = v!.Value);
            PropertyDiff.Write(c, static () => TableViewColumn.SortCycleProperty, o?.SortCycle, n.SortCycle, mount, v => c.SortCycle = v!.Value);
            PropertyDiff.Write(c, static () => TableViewColumn.SortMemberPathProperty, o?.SortMemberPath, n.SortMemberPath, mount, v => c.SortMemberPath = v);
            if (mount ? !n.IsVisible : o!.IsVisible != n.IsVisible)
                c.Visibility = n.IsVisible ? Visibility.Visible : Visibility.Collapsed;

            // Swapping the adapter's delegate needs no native write.
            if (EffectiveComparer(n) is { } comparison)
            {
                if (_comparer is null)
                {
                    _comparer = new SortComparerAdapter(comparison);
                    c.CustomSortComparer = _comparer;
                }
                else
                {
                    _comparer.Comparison = comparison;
                }
            }
            else if (_comparer is not null)
            {
                _comparer = null;
                c.CustomSortComparer = null;
            }

            // An EditingCell replaces any column's built-in editor (the native column honours
            // CellEditingTemplate first), including a TableTextColumn's TextBox.
            if (n.EditingCell is { } editing)
            {
                if (_editing is null)
                {
                    _editing = new ReactorDataTemplate(_state.Reconciler, _state.RequestRerender, editing, n);
                    c.CellEditingTemplate = _editing.Template;
                }
                else
                {
                    _editing.SetRender(editing, n);
                }
            }
            else if (_editing is not null)
            {
                _editing.UnmountAll();
                _editing = null;
                c.ClearValue(TableViewColumn.CellEditingTemplateProperty);
            }

            switch (n)
            {
                case TableBoundColumn bound when o is not TableBoundColumn oldBound || oldBound.Path != bound.Path:
                    ((TableViewTextColumn)c).Binding = CreateBinding(bound.Path);
                    break;
                case TableTemplateColumn template:
                    var cell = template.Cell;
                    SetCell(item => item is null ? null : cell(item), n);
                    break;
            }
        }

        private void SetCell(Func<object?, Element?> render, object identity)
        {
            if (_cell is null)
            {
                _cell = new ReactorDataTemplate(_state.Reconciler, _state.RequestRerender, render, identity);
                ((TableViewTemplateColumn)Native).CellTemplate = _cell.Template;
            }
            else
            {
                _cell.SetRender(render, identity);
            }
        }

        // A column with no editor would enter an empty edit mode; make it read-only unless the
        // author says otherwise.
        private static bool? EffectiveReadOnly(TableColumn column) =>
            column.IsReadOnly
            ?? (column is TableBoundColumn
                || column.EditingCell is not null
                || column is TableTextColumn { OnEdit: not null } ? null : true);

        // A text column sorts by its text unless told otherwise — no reflection involved.
        private static Comparison<object?>? EffectiveComparer(TableColumn column)
        {
            if (column.SortComparer is { } explicitComparer) return explicitComparer;
            if (column is not TableTextColumn text || column.SortMemberPath is not null) return null;
            var read = text.Text;
            return (a, b) => string.Compare(read(a), read(b), StringComparison.CurrentCulture);
        }
        private static Binding CreateBinding(string path) => new() { Path = new PropertyPath(path) };
    }

    /// <summary>
    /// The converter behind a <see cref="TableTextColumn"/>'s pathless binding. Convert reads the
    /// cell text from the row; ConvertBack — called once, when an edit commits — reports the new
    /// text to <see cref="TableTextColumn.OnEdit"/> and writes nothing back into the row.
    /// </summary>
    private sealed partial class TextColumnConverter(ColumnSlot slot, TableState state) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            slot.Record is TableTextColumn text ? text.Text(value) ?? string.Empty : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            var item = state.EditingItem;
            var editingThisColumn = ReferenceEquals(state.EditingColumn, slot.Native);
            state.EditingItem = null;
            state.EditingColumn = null;
            if (editingThisColumn && slot.Record is TableTextColumn text && value is string edited
                && TextColumnEdit.ShouldCommit(text, item, edited))
            {
                text.OnEdit!(item!, edited);
            }
            return DependencyProperty.UnsetValue;
        }
    }
}

/// <summary>Adapts a <see cref="Comparison{T}"/> to the WinRT sort-comparer interface.</summary>
internal sealed partial class SortComparerAdapter(Comparison<object?> comparison) : ITableViewSortComparer
{
    public Comparison<object?> Comparison { get; set; } = comparison;

    public int Compare(object left, object right) => Comparison(left, right);
}
