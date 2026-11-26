---
title: "Factories: Data grid"
description: "The data grid and its column factories, from the Microsoft.UI.Reactor.Advanced package."
sidebar:
  label: "Data grid"
---

Each factory method returns an element record. These are on `Microsoft.UI.Reactor.Advanced.Factories` in the `Microsoft.UI.Reactor.Advanced` package, so add `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Learn more:** [Collections](/microsoft-ui-reactor/main/controls/collections)

## `AutoColumns` [#autocolumns]

Auto-generate columns from a type using reflection and TypeRegistry.

```csharp
IReadOnlyList<FieldDescriptor> AutoColumns<T>(TypeRegistry? registry = null, Func<FieldDescriptor, FieldDescriptor>? overrides = null)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** [`IReadOnlyList`](https://learn.microsoft.com/dotnet/api/system.collections.generic.ireadonlylist-1).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `registry` | [`TypeRegistry?`](/microsoft-ui-reactor/main/reference/controls/property-grid-templates#typeregistry) | `null` |  |
| `overrides` | `Func<FieldDescriptor, FieldDescriptor>?` | `null` |  |

## `Column` [#column]

Define a column from a property accessor expression.

```csharp
ColumnBuilder<T> Column<T>(string name, Func<T, object?> accessor, bool editable = false, string? displayName = null, string? format = null, double? width = null, PinPosition pin = default)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** [`ColumnBuilder<T>`](/microsoft-ui-reactor/main/reference/controls/data-grid#columnbuildert).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | `string` |  |  |
| `accessor` | `Func<T, object?>` |  |  |
| `editable` | `bool` | `false` |  |
| `displayName` | `string?` | `null` |  |
| `format` | `string?` | `null` |  |
| `width` | `double?` | `null` |  |
| `pin` | [`PinPosition`](/microsoft-ui-reactor/main/reference/data/column-attributes#pinposition) | `default` |  |

## `DataGrid` [#datagrid]

```csharp
ComponentElement<DataGridElement<T>> DataGrid<T>(IDataSource<T> source, TypeRegistry registry, Func<FieldDescriptor, FieldDescriptor>? columnOverrides = null, SelectionMode selectionMode = SelectionMode.None, Action<IReadOnlySet<RowKey>>? onSelectionChanged = null, Func<RowKey, T, Task>? onRowChanged = null, double? rowHeight = 40, bool editable = false, EditMode editMode = EditMode.Cell, Element? loadingTemplate = null, Element? emptyTemplate = null)
ComponentElement<DataGridElement<T>> DataGrid<T>(IDataSource<T> source, IReadOnlyList<FieldDescriptor> columns, TypeRegistry? registry = null, SelectionMode selectionMode = SelectionMode.None, Action<IReadOnlySet<RowKey>>? onSelectionChanged = null, Func<RowKey, T, Task>? onRowChanged = null, double? rowHeight = 40, bool editable = false, EditMode editMode = EditMode.Cell, Func<CellContext<T>, Element>? cellTemplate = null, Func<RowContext<T>, Element>? rowTemplate = null, Func<HeaderContext, Element>? headerTemplate = null, Element? loadingTemplate = null, Element? emptyTemplate = null, bool showSearch = false, Func<T, RowKey, Element>? rowDetailTemplate = null, Func<FieldDescriptor, double, Element>? placeholderCellTemplate = null)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

| Overload | Description |
| --- | --- |
| `DataGrid<T>(IDataSource<T>, TypeRegistry, Func<FieldDescriptor, FieldDescriptor>?, SelectionMode, Action<IReadOnlySet<RowKey>>?, Func<RowKey, T, Task>?, double?, bool, EditMode, Element?, Element?)` | Creates a DataGrid with auto-generated columns from TypeRegistry + reflection. |
| `DataGrid<T>(IDataSource<T>, IReadOnlyList<FieldDescriptor>, TypeRegistry?, SelectionMode, Action<IReadOnlySet<RowKey>>?, Func<RowKey, T, Task>?, double?, bool, EditMode, Func<CellContext<T>, Element>?, Func<RowContext<T>, Element>?, Func<HeaderContext, Element>?, Element?, Element?, bool, Func<T, RowKey, Element>?, Func<FieldDescriptor, double, Element>?)` | Creates a DataGrid with explicit column definitions. |

**Returns:** [`ComponentElement<TProps>`](/microsoft-ui-reactor/main/reference/elements/composition#componentelementtprops).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | [`IDataSource<T>`](/microsoft-ui-reactor/main/reference/data/IDataSource) |  |  |
| `registry` | [`TypeRegistry`](/microsoft-ui-reactor/main/reference/controls/property-grid-templates#typeregistry) or [`TypeRegistry?`](/microsoft-ui-reactor/main/reference/controls/property-grid-templates#typeregistry) | `null` |  |
| `columnOverrides` | `Func<FieldDescriptor, FieldDescriptor>?` | `null` |  |
| `selectionMode` | [`SelectionMode`](/microsoft-ui-reactor/main/reference/controls/data-grid#selectionmode_1) | `SelectionMode.None` |  |
| `onSelectionChanged` | `Action<IReadOnlySet<RowKey>>?` | `null` |  |
| `onRowChanged` | `Func<RowKey, T, Task>?` | `null` |  |
| `rowHeight` | `double?` | `40` |  |
| `editable` | `bool` | `false` |  |
| `editMode` | [`EditMode`](/microsoft-ui-reactor/main/reference/controls/data-grid#editmode) | `EditMode.Cell` |  |
| `loadingTemplate` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | `null` |  |
| `emptyTemplate` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | `null` |  |
| `columns` | `IReadOnlyList<FieldDescriptor>` |  |  |
| `cellTemplate` | `Func<CellContext<T>, Element>?` | `null` |  |
| `rowTemplate` | `Func<RowContext<T>, Element>?` | `null` |  |
| `headerTemplate` | `Func<HeaderContext, Element>?` | `null` |  |
| `showSearch` | `bool` | `false` |  |
| `rowDetailTemplate` | `Func<T, RowKey, Element>?` | `null` |  |
| `placeholderCellTemplate` | `Func<FieldDescriptor, double, Element>?` | `null` |  |

**Remarks**

`onRowChanged` is normally invoked on the thread that committed the edit (the UI thread), but the grid falls back to a thread-pool thread in some cases — see [`DataGridElement.OnRowChanged`](/microsoft-ui-reactor/main/reference/elements/data-grid#datagridelementt) for the full threading contract.
