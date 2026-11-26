# Elements: Data grid

The data grid and its column factories, from the Microsoft.UI.Reactor.Advanced package.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Collections](../../collections.md)

## `DataGridElement<T>`

Element record for the DataGrid component. Defines the full API surface for data grid rendering and interaction.

```csharp
public record DataGridElement<T> : Element
```

Namespace: `Microsoft.UI.Reactor.Controls`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`Element`](Element.md).

**Modifiers:** [`SelectionChanged`](../modifiers/shared-controls.md#selectionchanged)

| Property | Type | Description |
| --- | --- | --- |
| `AllowColumnReorder` | `bool` | Whether columns can be reordered via drag. |
| `AllowColumnResize` | `bool` | Whether columns can be resized. |
| `CellTemplate` | `Func<CellContext<T>, Element>?` | Custom cell template override. |
| `ColumnOverrides` | `Func<FieldDescriptor, FieldDescriptor>?` | Column override function for reflection-generated columns. |
| `Columns` | `IReadOnlyList<FieldDescriptor>?` | Column definitions. If null, auto-generated from TypeRegistry + reflection. |
| `EditMode` | [`EditMode`](../controls/data-grid.md#editmode) | Editing mode: Cell (one cell at a time) or Row (whole row). |
| `Editable` | `bool` | Whether rows are editable (enables inline editing). |
| `EmptyTemplate` | [`Element?`](Element.md) | Element to show when data is empty. |
| `EstimatedRowHeight` | `double` | Estimated row height for variable-height mode. |
| `HeaderTemplate` | `Func<HeaderContext, Element>?` | Custom header template override. |
| `LoadingTemplate` | [`Element?`](Element.md) | Element to show when data is loading. |
| `OnRowChanged` | `Func<RowKey, T, Task>?` | Callback when a row is edited and committed. |
| `OnSelectionChanged` | `Action<IReadOnlySet<RowKey>>?` | Callback when selection changes. |
| `PlaceholderCellTemplate` | `Func<FieldDescriptor, double, Element>?` | Custom cell content for placeholder (unloaded) rows. Receives the column descriptor and column width; returns the element to render in that cell. When null, uses a default shimmer-style gray bar. |
| `Registry` | [`TypeRegistry?`](../controls/property-grid-templates.md#typeregistry) | Type registry for editor/renderer resolution. If null, uses built-in defaults. |
| `RowDetailTemplate` | `Func<T, RowKey, Element>?` | Callback for row detail content when expanded. |
| `RowHeight` | `double?` | Fixed row height. When set, all rows have this exact height and the virtualizer uses O(1) offset calculation. When null, rows are measured. |
| `RowTemplate` | `Func<RowContext<T>, Element>?` | Custom row template override. |
| `SelectionMode` | [`SelectionMode`](../controls/data-grid.md#selectionmode_1) | Selection mode. |
| `ShowHeaders` | `bool` | Whether to show column headers. |
| `ShowSearch` | `bool` | Whether to show the search bar above the grid. |
| `Source` | [`IDataSource<T>`](../data/IDataSource.md) | The data source providing rows. |
