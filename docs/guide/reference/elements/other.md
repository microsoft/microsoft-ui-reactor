# Elements: Other

Controls that do not fit one of the other areas.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

## `ChartElement`

Reactor element for the WinUI [`Chart`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.charts.chart) (`Microsoft.UI.Xaml.Controls.Charts`, Windows App SDK experimental channel): a native cartesian chart that plots line, bar and area series against linear, category and date-time axes.

```csharp
public sealed record ChartElement : Element
```

Namespace: `Microsoft.UI.Reactor.Advanced.Charts`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`Element`](Element.md).

**Created by:** [`Chart`](../factories/other.md#chart)

**Modifiers:** [`Legend`](../modifiers/shared-controls.md#legend), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `LegendTitle` | `string?` | Heading shown above the legend entries. |
| `Series` | `IReadOnlyList<ChartSeries>` | The plotted series, in drawing order. |
| `Setters` | `Action<Chart>[]` | Raw control setters applied after typed properties (escape hatch). |
| `ShowLegend` | `bool?` | Whether the legend is shown. |

**Remarks**

Create it with `Chart(series...)` from [`Factories`](../factories/index.md). Axes are declared on each series; series that share an axis pass equal axis records (records compare by value) and should pass the same X collection instance. A series that leaves an axis unset gets a shared default: a time axis for date X values, a category axis for any other X values, and a linear axis for Y.

This maps the platform control. For the D3-based charting toolkit (pie, radial, hierarchy, and custom-drawn charts) see `Microsoft.UI.Reactor.Charting`.

## `TableViewElement`

Reactor element for the WinUI [`TableView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.tabular.tableview) (`Microsoft.UI.Xaml.Controls.Tabular`, Windows App SDK experimental channel): a virtualized, column-oriented table with sorting, grouping, filtering, inline editing and single-row selection.

```csharp
public sealed record TableViewElement : Element
```

Namespace: `Microsoft.UI.Reactor.Advanced.Tabular`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`Element`](Element.md).

**Created by:** [`TableView`](../factories/other.md#tableview)

**Modifiers:** [`CellEditEnding`](../modifiers/shared-controls.md#celleditending), [`Density`](../modifiers/shared-controls.md#density), [`FilterRows`](../modifiers/shared-controls.md#filterrows), [`GridLines`](../modifiers/shared-controls.md#gridlines), [`GroupRows`](../modifiers/shared-controls.md#grouprows), [`KeyRows`](../modifiers/shared-controls.md#keyrows), [`ReadOnly`](../modifiers/shared-controls.md#readonly), [`SelectedIndex`](../modifiers/shared-controls.md#selectedindex), [`SelectionChanged`](../modifiers/shared-controls.md#selectionchanged), [`SelectionMode`](../modifiers/shared-controls.md#selectionmode), [`Set`](../modifiers/native-properties.md#set), [`Sorted`](../modifiers/shared-controls.md#sorted)

| Property | Type | Description |
| --- | --- | --- |
| `AlternatingRowBackground` | `Brush?` | Background brush for alternating rows (row banding). |
| `CanUserResizeColumns` | `bool?` | Whether the user can drag column header edges to resize columns. |
| `CanUserSortColumns` | `bool?` | Whether clicking a column header sorts by that column. |
| `Columns` | `IReadOnlyList<TableColumn>` | The columns, in display order. See `TableColumn`. |
| `Density` | `TableViewDensity?` | Row height density (`Compact`, `Standard`, `Comfortable`). |
| `EmptyContent` | [`Element?`](Element.md) | Content shown in place of the rows when the source is empty. |
| `Filter` | `Func<object, bool>?` | Optional row predicate. When set, [`TableViewElement.ItemsSource`](#tableviewelement) is wrapped in a [`TableViewSource`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.tabular.tableviewsource) and filtered without copying. The predicate is re-applied whenever the delegate instance changes, so memoize it (`UseMemo`/`UseCallback`) when the table is large. |
| `GridLinesVisibility` | `TableViewGridLinesVisibility?` | Which cell grid lines are drawn. |
| `GroupBy` | `Func<object, object?>?` | Optional grouping key selector. When set, rows are grouped under collapsible group headers. Re-applied whenever the delegate instance changes. |
| `GroupHeader` | `Func<TableViewGroupInfo, Element?>?` | Optional group header content. Receives the group's [`TableViewGroupInfo`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.tabular.tableviewgroupinfo) (key, item count, nesting level). When `null`, the control's default header is used. |
| `HeadersVisibility` | `TableViewHeadersVisibility?` | Whether the column header row is shown. |
| `IsReadOnly` | `bool?` | Whether cells are read-only. When `null`, a table with an editable column — a `TableTextColumn` with an `TableTextColumn.OnEdit` callback, or a column with an `TableColumn.EditingCell` — is editable; otherwise the control default (read-only) applies, so a `TableBoundColumn` table opts in with `.ReadOnly(false)`. |
| `ItemsSource` | `IEnumerable?` | The rows. Pass an immutable snapshot — an array or list of records held in component state — and replace it to change the rows: the handler diffs each new snapshot into a collection it owns (see [`TableViewElement.RowKey`](#tableviewelement)), so only the rows that changed are touched and selection, scroll position and the active edit survive. A user-owned [`INotifyCollectionChanged`](https://learn.microsoft.com/dotnet/api/system.collections.specialized.inotifycollectionchanged) collection is bound directly instead; under NativeAOT it must be a named `partial` collection type, or CsWinRT cannot project it. |
| `OnBeginningEdit` | `Action<TableViewBeginningEditEventArgs>?` | Raised before a cell enters edit mode. Set `Cancel` to veto it. |
| `OnCellEditEnding` | `Action<TableViewCellEditEndingEventArgs>?` | Raised before a cell edit is committed or cancelled. |
| `OnSelectionChanged` | `Action<int, object?>?` | Raised when the selected row changes, with the new index (`-1` when cleared) and item. |
| `OnSorted` | `Action<TableViewSortedEventArgs>?` | Raised after a column sort is applied (or cleared: `Direction == None`). |
| `OnSorting` | `Action<TableViewSortingEventArgs>?` | Raised before a column sort is applied. Set `Cancel` to veto it. |
| `RowBackground` | `Brush?` | Background brush for every row. |
| `RowKey` | `Func<object, object?>?` | Stable identity of a row, used to diff successive [`TableViewElement.ItemsSource`](#tableviewelement) snapshots: a row whose key moved is moved, a row whose key is new is inserted, and a row whose key persists but whose value changed (for example `person with { Name = … }`) is replaced in place. When `null`, rows are matched by position. |
| `SelectedIndex` | `int?` | Controlled selected row index (`-1` clears the selection). When `null`, the control owns the selection. |
| `SelectionMode` | `TableViewSelectionMode?` | Row selection mode (`None` or `Single`). |
| `Setters` | `Action<TableView>[]` | Raw control setters applied after typed properties (escape hatch). |

**Remarks**

Create it with `TableView(items, columns...)` from [`Factories`](../factories/index.md). Every optional property is `null` by default, which leaves the control's own default in place; a property is written only when it changes between renders, so state the user changes interactively (column widths, the active sort) survives re-renders.
