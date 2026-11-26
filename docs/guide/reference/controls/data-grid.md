# Data grid

The data grid component, its state, column builders and cell contexts. From the Microsoft.UI.Reactor.Advanced package.

**Learn more:** [Controls](../../controls.md), [Collections](../../collections.md)

## `CellContext<T>`

```csharp
public record CellContext<T>(T Row, RowKey Key, FieldDescriptor Column, object? Value, bool IsEditing, Action<object?> SetValue)
```

Namespace: `Microsoft.UI.Reactor.Controls`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `CellContext`

```csharp
public CellContext(T Row, RowKey Key, FieldDescriptor Column, object? Value, bool IsEditing, Action<object?> SetValue)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Row` | `T` |  |
| `Key` | [`RowKey`](../data/descriptors.md#rowkey) |  |
| `Column` | [`FieldDescriptor`](../data/descriptors.md#fielddescriptor) |  |
| `Value` | `object?` |  |
| `IsEditing` | `bool` |  |
| `SetValue` | `Action<object?>` |  |

### Properties

#### `Column`

```csharp
public FieldDescriptor Column  // get, init
```

#### `IsEditing`

```csharp
public bool IsEditing  // get, init
```

#### `Key`

```csharp
public RowKey Key  // get, init
```

#### `Row`

```csharp
public T Row  // get, init
```

#### `SetValue`

```csharp
public Action<object?> SetValue  // get, init
```

#### `Value`

```csharp
public object? Value  // get, init
```

## `ColumnBuilder<T>`

Fluent builder for column definitions. Supports validation chaining.

```csharp
public class ColumnBuilder<T>
```

Namespace: `Microsoft.UI.Reactor.Controls`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Build`

Build the FieldDescriptor.

```csharp
public FieldDescriptor Build()
```

#### `CellRenderer`

Set a custom cell renderer for this column.

```csharp
public ColumnBuilder<T> CellRenderer(Func<object, Element> renderer)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `renderer` | `Func<object, Element>` |  |

#### `NotFilterable`

Set filterable state.

```csharp
public ColumnBuilder<T> NotFilterable()
```

#### `NotSortable`

Set sortable state.

```csharp
public ColumnBuilder<T> NotSortable()
```

#### `Validate`

Add validators to this column.

```csharp
public ColumnBuilder<T> Validate(params IValidator[] validators)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `validators` | [`IValidator[]`](../validation/IValidator.md) |  |

#### `ValidateAsync`

Add async validators to this column.

```csharp
public ColumnBuilder<T> ValidateAsync(params IAsyncValidator[] validators)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `validators` | [`IAsyncValidator[]`](../validation/IAsyncValidator.md) |  |

#### `Width`

Width / min / max / flex for the column.

```csharp
public ColumnBuilder<T> Width(double? width = null, double? min = null, double? max = null, double? flex = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `width` | `double?` | `null` |  |
| `min` | `double?` | `null` |  |
| `max` | `double?` | `null` |  |
| `flex` | `double?` | `null` |  |

#### `WithEditor`

Set a custom inline editor for this column. The delegate receives the current value and an onChange callback and returns the editor Element. See [`Editors`](property-grid-templates.md#editors) for pre-built factories.

```csharp
public ColumnBuilder<T> WithEditor(Func<object, Action<object>, Element> editor)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `editor` | `Func<object, Action<object>, Element>` |  |

### Operators

#### `op_Implicit`

Implicit conversion to FieldDescriptor for clean DSL usage.

```csharp
public static implicit operator FieldDescriptor(ColumnBuilder<T> builder)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `builder` | [`ColumnBuilder<T>`](#columnbuildert) |  |

## `DataGridComponent<T>`

The core DataGrid component. Composes VirtualList for the row area, renders a fixed header row with sort indicators, and resolves cell renderers from TypeRegistry. Supports keyboard navigation and inline editing.

Architecture: the VirtualList's renderItem callback is stored in a ref so it's stable across re-renders. This prevents the LazyVStack from replacing its ElementFactory, which would cause ItemsRepeater to re-realize items and crash with "Cannot run layout in the middle of a collection change." Instead, cells read current state from the DataGridState ref at render time. When state changes, the DataGrid forceRenders, the VirtualList sees the same props (stable callback, same item count), and the Reactor reconciler only updates the cells whose output changed — as property updates on existing controls, not collection changes.

```csharp
public class DataGridComponent<T> : Component<DataGridElement<T>>
```

Namespace: `Microsoft.UI.Reactor.Controls`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`Component<TProps>`](../components/Component-1.md).

### Constructors

#### `DataGridComponent`

```csharp
public DataGridComponent()
```

### Methods

#### `Render`

```csharp
public override Element Render()
```

## `DataGridState<T>`

Headless state machine for the DataGrid. Manages sort state, selection, column order/sizing/visibility, and editing state. Pure logic, no UI dependencies — fully testable without rendering.

```csharp
public class DataGridState<T>
```

Namespace: `Microsoft.UI.Reactor.Controls`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `DataGridState`

```csharp
public DataGridState(IDataSource<T> source, IReadOnlyList<FieldDescriptor> columns, SelectionMode selectionMode, int blockSize = 0)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | [`IDataSource<T>`](../data/IDataSource.md) |  | Data source feeding rows into the grid. |
| `columns` | `IReadOnlyList<FieldDescriptor>` |  | Column descriptors defining accessor + editor + renderer per column. |
| `selectionMode` | [`SelectionMode`](#selectionmode_1) |  | Single-row, multi-row, or no selection. |
| `blockSize` | `int` | `0` | Page cache block size. When 0 (default), the cache uses its built-in default (50). Pass a viewport-derived value to ensure the first block fills the screen. |

### Properties

#### `AllColumns`

All column definitions, including hidden ones.

```csharp
public IReadOnlyList<FieldDescriptor> AllColumns  // get
```

#### `AnchorKey`

Anchor key for shift-click range selection.

```csharp
public RowKey? AnchorKey  // get
```

#### `Columns`

Current column definitions (in display order), excluding hidden columns.

```csharp
public IReadOnlyList<FieldDescriptor> Columns  // get
```

#### `CommitDispatcher`

Delegate installed by [`DataGridComponent<T>`](#datagridcomponentt) each render to route row-commit dispatch through a `UseMutation` hook. Static helpers in the component (`HandleKeyDown`, `RenderRow`) invoke it synchronously, on the thread that committed the edit, instead of spinning up their own `Task.Run`; the component's own delegate goes on to reach `OnRowChanged` on that same thread, but a delegate you install yourself is free to offload or marshal from there. When null — e.g. in headless unit tests — callers fall back to invoking `OnRowChanged` themselves on a thread-pool thread; see `DataGridComponent<T>.HandleAsyncCommit` for both threading contracts.

```csharp
public Action<RowKey, T, T?>? CommitDispatcher  // get, set
```

**Remarks**

Installing a delegate of your own replaces that fallback outright. It is invoked synchronously, on the thread that committed the edit, with the row key, the post-edit item and the pre-edit item — and from there the grid does nothing further for that commit. The third argument is `T?` for a reason: the callers resolve it by row index just before committing, and it is `default` when there is no row being edited or the key no longer maps to a loaded row, so a delegate that snapshots it for an optimistic revert has to cope with that. The delegate also owns both halves the fallback used to handle: calling the element's `OnRowChanged`, and driving the row's commit lifecycle ([`DataGridState.BeginAsyncCommit`](#beginasynccommit), then [`DataGridState.CompleteAsyncCommit`](#completeasynccommit) or [`DataGridState.FailAsyncCommit`](#failasynccommit)). Skip the lifecycle calls and the row never shows a committing state, an error banner, or an optimistic revert. Note that a grid rendered through [`DataGridComponent<T>`](#datagridcomponentt) reassigns this property on every render, so a custom dispatcher only survives on a state you drive yourself.

#### `EditValidation`

The validation context for the current edit, or null if not editing.

```csharp
public ValidationContext? EditValidation  // get
```

#### `EditingColumnName`

Column name of the cell currently being edited, or null.

```csharp
public string? EditingColumnName  // get
```

#### `EditingRowKey`

Row key of the cell currently being edited, or null.

```csharp
public RowKey? EditingRowKey  // get
```

#### `EditingValue`

The pending (uncommitted) value for the cell being edited.

```csharp
public object? EditingValue  // get
```

#### `EditingVersion`

Monotonically increasing version, bumped on every editing state change.

```csharp
public int EditingVersion  // get
```

#### `ExpandedRows`

Set of currently expanded row keys.

```csharp
public IReadOnlySet<RowKey> ExpandedRows  // get
```

#### `Filters`

Current filter descriptors.

```csharp
public IReadOnlyList<FilterDescriptor> Filters  // get
```

#### `FocusedColIndex`

Column index of the focused cell, or -1 if no focus.

```csharp
public int FocusedColIndex  // get
```

#### `FocusedKey`

Currently focused row key (for keyboard navigation).

```csharp
public RowKey? FocusedKey  // get
```

#### `FocusedRowIndex`

Row index of the focused cell, or -1 if no focus.

```csharp
public int FocusedRowIndex  // get
```

#### `HasPendingCommits`

Whether any rows are currently being committed.

```csharp
public bool HasPendingCommits  // get
```

#### `HasValidationErrors`

Whether the current edit has validation errors.

```csharp
public bool HasValidationErrors  // get
```

#### `HiddenColumns`

Set of currently hidden column names.

```csharp
public IReadOnlySet<string> HiddenColumns  // get
```

#### `HookResource`

The hook-owned [`InfiniteResource<TItem>`](../data/paging.md#infiniteresourcetitem) when running under `ReactorFeatureFlags.UseHookBasedPaging`, or null in legacy mode.

```csharp
public InfiniteResource<T>? HookResource  // get
```

#### `IsEditing`

Whether a cell is currently in edit mode.

```csharp
public bool IsEditing  // get
```

#### `IsLoading`

Whether data is currently being fetched.

```csharp
public bool IsLoading  // get
```

#### `IsRowEditing`

Whether the entire row is in edit mode (vs single cell).

```csharp
public bool IsRowEditing  // get
```

#### `ItemCount`

Total number of items in the data set. In paged mode, this is the total count from the data source (even if not all pages are loaded yet). The VirtualList uses this for the full scrollbar extent. Unloaded items render as placeholders.

```csharp
public int ItemCount  // get
```

#### `LoadedItems`

Currently loaded items. In paged mode, returns items from all loaded cache blocks plus any mutations overlay. Prefer GetItemAt for index-based access.

```csharp
public IReadOnlyList<T> LoadedItems  // get
```

#### `MaxClientFallbackPageSize`

Hard cap on the page size used for the client-fallback "load all rows" path. Apps mounting against a non-server-capable source that expect more rows should configure their backing store accordingly. TASK-097.

```csharp
public static int MaxClientFallbackPageSize  // get, set
```

#### `PageCache`

The underlying page cache, or null if using legacy eager loading.

```csharp
public DataPageCache<T>? PageCache  // get
```

#### `RowKeyCache`

Pre-computed row key strings (legacy — prefer GetRowKeyAt for paginated access).

```csharp
public string[] RowKeyCache  // get
```

#### `SearchQuery`

Current text search query.

```csharp
public string? SearchQuery  // get
```

#### `SelectedKeys`

Currently selected row keys.

```csharp
public IReadOnlySet<RowKey> SelectedKeys  // get
```

#### `SelectionMode`

The active selection mode. Reconciled from the `SelectionMode` prop each render via [`DataGridState.SetSelectionMode`](#setselectionmode) (issue #872), so it stays in sync after the first mount.

```csharp
public SelectionMode SelectionMode  // get
```

#### `SelectionVersion`

Monotonically increasing version number, bumped on every selection change.

```csharp
public int SelectionVersion  // get
```

#### `Sorts`

Current sort descriptors (ordered by priority).

```csharp
public IReadOnlyList<SortDescriptor> Sorts  // get
```

#### `TotalCount`

Total item count from the data source.

```csharp
public int? TotalCount  // get
```

### Methods

#### `BeginAsyncCommit`

Begin an async commit for a row. Stores the original item for potential revert. Call after CommitEdit/CommitRowEdit to mark the row as committing.

```csharp
public void BeginAsyncCommit(RowKey key, T originalItem)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`RowKey`](../data/descriptors.md#rowkey) |  |
| `originalItem` | `T` |  |

#### `BeginEdit`

##### `BeginEdit()`

Begin editing the currently focused cell. Returns false if the cell is not editable.

```csharp
public bool BeginEdit()
```

##### `BeginEdit(int, int)`

Begin editing a specific cell.

```csharp
public bool BeginEdit(int rowIndex, int colIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rowIndex` | `int` |  |
| `colIndex` | `int` |  |

#### `BeginRowEdit`

Begin editing an entire row. All editable columns switch to editors simultaneously. Returns false if the row index is invalid or there are no editable columns.

```csharp
public bool BeginRowEdit(int rowIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rowIndex` | `int` |  |

#### `CancelEdit`

Cancel the current edit, discarding pending changes.

```csharp
public void CancelEdit()
```

#### `CancelRowEdit`

Cancel the row edit, discarding all pending changes.

```csharp
public void CancelRowEdit()
```

#### `ClearAllFilters`

Remove all filters.

```csharp
public void ClearAllFilters()
```

#### `ClearFilter`

Remove the filter for a column.

```csharp
public void ClearFilter(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

#### `ClearSelection`

Clear all selection.

```csharp
public void ClearSelection()
```

#### `CollapseAllRows`

Collapse all expanded rows.

```csharp
public void CollapseAllRows()
```

#### `CollapseRow`

Collapse a row.

```csharp
public void CollapseRow(RowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`RowKey`](../data/descriptors.md#rowkey) |  |

#### `CommitAndMoveNext`

Commit the current edit and move focus to the next cell. Focus lands on the next cell whether or not it is editable; reopening an editor there is the caller's job (see `DataGridComponent<T>`'s Tab handling), not this method's.

```csharp
public (RowKey Key, T NewItem)? CommitAndMoveNext()
```

#### `CommitAndMovePrev`

Commit the current edit and move focus to the PREVIOUS cell — the Shift+Tab counterpart of [`DataGridState.CommitAndMoveNext`](#commitandmovenext), committing exactly what that would and differing only in where the cursor lands (issue #987). Like its twin it lands on the previous cell whether or not that cell is editable, and leaves reopening an editor to the caller.

```csharp
public (RowKey Key, T NewItem)? CommitAndMovePrev()
```

#### `CommitEdit`

Commit the current edit. Applies SetValue to produce the new item, updates the loaded items list, and returns the (row key, new item) for async commit. Returns null if no edit is active. In row-edit mode this delegates to [`DataGridState.CommitRowEdit`](#commitrowedit) — mirroring [`DataGridState.CancelEdit`](#canceledit)'s delegation to [`DataGridState.CancelRowEdit`](#cancelrowedit).

```csharp
public (RowKey Key, T NewItem)? CommitEdit()
```

#### `CommitRowEdit`

Commit the entire row edit. Applies all pending SetValue calls to produce the new item. Returns the (row key, new item) for async commit.

```csharp
public (RowKey Key, T NewItem)? CommitRowEdit()
```

#### `CompleteAsyncCommit`

Mark an async commit as successfully completed. Clears the pending state.

```csharp
public void CompleteAsyncCommit(RowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`RowKey`](../data/descriptors.md#rowkey) |  |

#### `DismissCommitError`

Dismiss the error for a specific row.

```csharp
public void DismissCommitError(RowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`RowKey`](../data/descriptors.md#rowkey) |  |

#### `EnsureRangeLoaded`

Request that blocks covering the given row range be loaded. Prefetches one block before and one block after the visible range so that small scrolls don't hit loading placeholders.

```csharp
public void EnsureRangeLoaded(int firstRow, int lastRow)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `firstRow` | `int` |  |
| `lastRow` | `int` |  |

#### `ExpandRow`

Expand a row.

```csharp
public void ExpandRow(RowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`RowKey`](../data/descriptors.md#rowkey) |  |

#### `FailAsyncCommit`

Mark an async commit as failed. Reverts the item to its pre-edit state and stores the error message.

```csharp
public void FailAsyncCommit(RowKey key, string errorMessage)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`RowKey`](../data/descriptors.md#rowkey) |  |
| `errorMessage` | `string` |  |

#### `FocusEnd`

Move focus to the last column in the current row.

```csharp
public void FocusEnd()
```

#### `FocusHome`

Move focus to the first column in the current row.

```csharp
public void FocusHome()
```

#### `FocusNextCell`

Move focus to the next cell (left to right, top to bottom). Returns false at the end.

```csharp
public bool FocusNextCell()
```

#### `FocusNextRowEditColumn`

Advance the grid's logical cell cursor to the next visible column taking part in the active row edit, wrapping to the first one after the last. Row-mode Tab navigation stays inside the row and never commits — a row commits only on Enter, Save, or click-away (spec 017 §6.7). Returns false when no row edit is active, or when no visible column in the row has an editor.

```csharp
public bool FocusNextRowEditColumn()
```

**Remarks**

This moves the grid's own [`DataGridState.FocusedColIndex`](#focusedcolindex) bookkeeping AND arms an editor-focus request, so the renderer pulls real XAML keyboard focus onto the target editor once it is mounted (#976). WinUI's FocusManager has already moved native focus by the time the grid's `handledEventsToo` KeyDown handler runs — the request overrides where it landed, which is what keeps Tab cycling inside the row's editors instead of walking out of the grid.

#### `FocusPrevCell`

Move focus to the previous cell (right to left, bottom to top). Returns false at the start.

```csharp
public bool FocusPrevCell()
```

#### `FocusPrevRowEditColumn`

Move the grid's logical cell cursor back to the previous visible column taking part in the active row edit, wrapping to the last one before the first. The Shift+Tab counterpart of [`DataGridState.FocusNextRowEditColumn`](#focusnextroweditcolumn), with the same never-commit guarantee. Returns false when no row edit is active, or when no visible column in the row has an editor.

```csharp
public bool FocusPrevRowEditColumn()
```

**Remarks**

This is what the grid's own KeyDown handler runs for Shift+Tab during a row edit (#987) — the modifier now survives dispatch, so the backward direction is reachable from the keyboard. It is also public for the same reason [`DataGridState.FocusPrevCell`](#focusprevcell) is: app authors driving custom keyboard handling need both directions. It arms the same editor-focus request as [`DataGridState.FocusNextRowEditColumn`](#focusnextroweditcolumn) — they share `MoveRowEditFocus` — so backward Tab moves real XAML focus exactly as forward Tab does (#976), rather than only the logical cursor.

#### `GetAllValidationMessages`

Get all validation messages for the current edit.

```csharp
public IReadOnlyList<ValidationMessage> GetAllValidationMessages()
```

#### `GetColumnWidth`

Gets the effective width for a column.

```csharp
public double GetColumnWidth(string columnName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `columnName` | `string` |  |

#### `GetCommitError`

Gets the commit error for a row, or null if no error.

```csharp
public string? GetCommitError(RowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`RowKey`](../data/descriptors.md#rowkey) |  |

#### `GetFilter`

Gets the active filter for a column, or null.

```csharp
public FilterDescriptor? GetFilter(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

#### `GetItemAt`

Get the item at a specific row index. Returns default(T) if the item's block hasn't been loaded yet. Does not trigger fetches — ItemCount is bounded to loaded items, so indices within range are always available.

```csharp
public T? GetItemAt(int index)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |

#### `GetPinnedColumnGroups`

Get visible columns grouped by pin position.

```csharp
public (IReadOnlyList<FieldDescriptor> Left, IReadOnlyList<FieldDescriptor> Center, IReadOnlyList<FieldDescriptor> Right) GetPinnedColumnGroups()
```

#### `GetRowEditValue`

Gets the pending row-edit value for a specific column, or null if not in row edit.

```csharp
public object? GetRowEditValue(string columnName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `columnName` | `string` |  |

#### `GetRowIndex`

Get the row index for a given row key, or -1 if not found. Searches the key cache (legacy mode) or scans loaded cache blocks (paged mode).

```csharp
public int GetRowIndex(RowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`RowKey`](../data/descriptors.md#rowkey) |  |

#### `GetRowKeyAt`

Get the row key string for a specific index. Returns null if the item is not yet loaded.

```csharp
public string? GetRowKeyAt(int index)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |

#### `GetSortDirection`

Gets the current sort direction for a column, or null if unsorted.

```csharp
public SortDirection? GetSortDirection(string field)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `field` | `string` |  |

#### `GetValidationMessages`

Get validation messages for a specific field in the current edit.

```csharp
public IReadOnlyList<ValidationMessage> GetValidationMessages(string fieldName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fieldName` | `string` |  |

#### `HandleRowClick`

Handles a row click with optional modifier keys.

```csharp
public void HandleRowClick(RowKey key, bool ctrlKey = false, bool shiftKey = false, IReadOnlyList<RowKey>? visibleOrder = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `key` | [`RowKey`](../data/descriptors.md#rowkey) |  |  |
| `ctrlKey` | `bool` | `false` |  |
| `shiftKey` | `bool` | `false` |  |
| `visibleOrder` | `IReadOnlyList<RowKey>?` | `null` |  |

#### `HideColumn`

Hide a column.

```csharp
public void HideColumn(string columnName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `columnName` | `string` |  |

#### `IsColumnInRowEdit`

Check if a specific column is being edited in row mode.

```csharp
public bool IsColumnInRowEdit(RowKey rowKey, string columnName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rowKey` | [`RowKey`](../data/descriptors.md#rowkey) |  |
| `columnName` | `string` |  |

#### `IsColumnVisible`

Check if a column is visible.

```csharp
public bool IsColumnVisible(string columnName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `columnName` | `string` |  |

#### `IsCommitting`

Whether a specific row is currently being committed asynchronously.

```csharp
public bool IsCommitting(RowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`RowKey`](../data/descriptors.md#rowkey) |  |

#### `IsExpanded`

Check if a row is expanded.

```csharp
public bool IsExpanded(RowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`RowKey`](../data/descriptors.md#rowkey) |  |

#### `IsItemLoaded`

Whether the item at a specific row index is loaded.

```csharp
public bool IsItemLoaded(int index)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |

#### `IsSelected`

Check if a row is selected.

```csharp
public bool IsSelected(RowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`RowKey`](../data/descriptors.md#rowkey) |  |

#### `LoadDataAsync`

Load data from the source using current sort/filter state.

```csharp
public Task LoadDataAsync(CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` |  |

#### `MoveFocus`

Move focus by a delta. Used for arrow key navigation.

```csharp
public void MoveFocus(int rowDelta, int colDelta)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rowDelta` | `int` |  |
| `colDelta` | `int` |  |

#### `PinColumn`

Pin a column to a position at runtime.

```csharp
public void PinColumn(string columnName, PinPosition position)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `columnName` | `string` |  |
| `position` | [`PinPosition`](../data/column-attributes.md#pinposition) |  |

#### `ReorderColumn`

Reorder a column to a new position.

```csharp
public void ReorderColumn(int fromIndex, int toIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fromIndex` | `int` |  |
| `toIndex` | `int` |  |

#### `ResizeColumn`

Resize a column and trigger a re-render.

```csharp
public void ResizeColumn(string columnName, double newWidth)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `columnName` | `string` |  |
| `newWidth` | `double` |  |

#### `SelectAll`

Select all provided keys.

```csharp
public void SelectAll(IReadOnlyList<RowKey> allKeys)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `allKeys` | `IReadOnlyList<RowKey>` |  |

#### `SelectRange`

Select a range of rows between two keys based on visible order.

```csharp
public void SelectRange(RowKey from, RowKey to, IReadOnlyList<RowKey> visibleOrder)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `from` | [`RowKey`](../data/descriptors.md#rowkey) |  |
| `to` | [`RowKey`](../data/descriptors.md#rowkey) |  |
| `visibleOrder` | `IReadOnlyList<RowKey>` |  |

#### `SetFilter`

Set a filter on a column. Replaces any existing filter on the same field.

```csharp
public void SetFilter(FilterDescriptor filter)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `filter` | [`FilterDescriptor`](../data/descriptors.md#filterdescriptor) |  |

#### `SetFocus`

Set cell focus to a specific row and column index.

```csharp
public void SetFocus(int rowIndex, int colIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rowIndex` | `int` |  |
| `colIndex` | `int` |  |

#### `SetHookResource`

Attach (or replace) the hook-owned [`InfiniteResource<TItem>`](../data/paging.md#infiniteresourcetitem) backing this grid's data. When attached, the legacy [`DataPageCache<T>`](../data/paging.md#datapagecachet) path is bypassed — [`DataGridState.ItemCount`](#itemcount), [`DataGridState.GetItemAt`](#getitemat), [`DataGridState.GetRowKeyAt`](#getrowkeyat), and [`DataGridState.EnsureRangeLoaded`](#ensurerangeloaded) read from the resource.

```csharp
public void SetHookResource(InfiniteResource<T>? resource)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `resource` | [`InfiniteResource<T>?`](../data/paging.md#infiniteresourcetitem) |  |

**Remarks**

Pass `null` to detach. Deps-change in the hook creates a new resource; call this each render so the latest reference is used. Re-attaching the same reference is a no-op.

#### `SetSearchQuery`

Set the text search query. Triggers a state change for data reload.

```csharp
public void SetSearchQuery(string? query)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `query` | `string?` |  |

#### `SetSelectionMode`

Updates the selection mode in response to a `SelectionMode` prop change (issue #872). When narrowing, trims the current selection so it stays valid for the new mode:

- [`SelectionMode.None`](#values_1) — clears all selected keys and the anchor.
- [`SelectionMode.Single`](#values_1) — keeps at most one key (prefers the selection anchor, then the focused row, else any remaining key).

Bumps [`DataGridState.SelectionVersion`](#selectionversion) when the selected set actually changes, and raises [`DataGridState.StateChanged`](#statechanged). No-ops when the mode is unchanged. The component reconciles this during render behind a `state.SelectionMode != el.SelectionMode` guard, so the deferred re-render it schedules cannot loop.

```csharp
public void SetSelectionMode(SelectionMode mode)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | [`SelectionMode`](#selectionmode_1) |  |

#### `ShowColumn`

Show a previously hidden column.

```csharp
public void ShowColumn(string columnName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `columnName` | `string` |  |

#### `ToggleColumnVisibility`

Toggle column visibility.

```csharp
public void ToggleColumnVisibility(string columnName)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `columnName` | `string` |  |

#### `ToggleRowExpansion`

Toggle the expanded state of a row.

```csharp
public void ToggleRowExpansion(RowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`RowKey`](../data/descriptors.md#rowkey) |  |

#### `ToggleSort`

Toggles sort on a column: None -\> Ascending -\> Descending -\> None. When additive is true (Ctrl+click), adds to multi-sort. Otherwise replaces.

```csharp
public void ToggleSort(string field, bool additive = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `field` | `string` |  |  |
| `additive` | `bool` | `false` |  |

#### `UpdateEditingValue`

Update the pending value during editing.

```csharp
public void UpdateEditingValue(object? value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `object?` |  |

#### `UpdateRowEditValue`

Update a pending column value during row editing.

```csharp
public void UpdateRowEditValue(string columnName, object? value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `columnName` | `string` |  |
| `value` | `object?` |  |

#### `ValidateFieldAsync`

Run async validators for a field. Returns when all validators complete.

```csharp
public Task ValidateFieldAsync(string fieldName, object? value, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fieldName` | `string` |  |  |
| `value` | `object?` |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

### Events

#### `StateChanged`

Fires when state changes requiring a re-render.

```csharp
public event Action? StateChanged
```

### Fields

#### `LastScrollTick`

Timestamp (Stopwatch ticks) of the last scroll event. Set by the onVisibleRangeChanged callback so the StateChanged handler can defer re-renders during active scrolling.

```csharp
public long LastScrollTick
```

#### `LastVisibleFirst`

Last visible range reported by onVisibleRangeChanged. Used to re-request blocks after scroll settles, in case the final position wasn't covered by requests made during rapid scrolling.

```csharp
public int LastVisibleFirst
```

#### `LastVisibleLast`

```csharp
public int LastVisibleLast
```

#### `RenderDispatchTick`

Timestamp set when a deferred forceRender is dispatched. If LastScrollTick moves past this value before RefreshRealizedItems runs, the reconciliation is skipped (scroll restarted).

```csharp
public long RenderDispatchTick
```

## `EditMode`

```csharp
public enum EditMode
```

Namespace: `Microsoft.UI.Reactor.Controls`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Cell` | `0` |  |
| `Row` | `1` |  |

## `HeaderContext`

```csharp
public record HeaderContext(FieldDescriptor Column, SortDirection? CurrentSort, Action ToggleSort, Action<double> Resize)
```

Namespace: `Microsoft.UI.Reactor.Controls`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `HeaderContext`

```csharp
public HeaderContext(FieldDescriptor Column, SortDirection? CurrentSort, Action ToggleSort, Action<double> Resize)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Column` | [`FieldDescriptor`](../data/descriptors.md#fielddescriptor) |  |
| `CurrentSort` | `SortDirection?` |  |
| `ToggleSort` | `Action` |  |
| `Resize` | `Action<double>` |  |

### Properties

#### `Column`

```csharp
public FieldDescriptor Column  // get, init
```

#### `CurrentSort`

```csharp
public SortDirection? CurrentSort  // get, init
```

#### `Resize`

```csharp
public Action<double> Resize  // get, init
```

#### `ToggleSort`

```csharp
public Action ToggleSort  // get, init
```

## `RowContext<T>`

```csharp
public record RowContext<T>(T Row, RowKey Key, int RowIndex, bool IsSelected, bool IsEditing, IReadOnlyList<Element> Cells)
```

Namespace: `Microsoft.UI.Reactor.Controls`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `RowContext`

```csharp
public RowContext(T Row, RowKey Key, int RowIndex, bool IsSelected, bool IsEditing, IReadOnlyList<Element> Cells)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Row` | `T` |  |
| `Key` | [`RowKey`](../data/descriptors.md#rowkey) |  |
| `RowIndex` | `int` |  |
| `IsSelected` | `bool` |  |
| `IsEditing` | `bool` |  |
| `Cells` | `IReadOnlyList<Element>` |  |

### Properties

#### `Cells`

```csharp
public IReadOnlyList<Element> Cells  // get, init
```

#### `IsEditing`

```csharp
public bool IsEditing  // get, init
```

#### `IsSelected`

```csharp
public bool IsSelected  // get, init
```

#### `Key`

```csharp
public RowKey Key  // get, init
```

#### `Row`

```csharp
public T Row  // get, init
```

#### `RowIndex`

```csharp
public int RowIndex  // get, init
```

## `SelectionMode`

```csharp
public enum SelectionMode
```

Namespace: `Microsoft.UI.Reactor.Controls`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `None` | `0` |  |
| `Single` | `1` |  |
| `Multiple` | `2` |  |

## `TypedColumns`

Typed column factories that ship specialized editors + cell renderers out of the box. Thin wrappers over [`Column`](../factories/data-grid.md#column) that pre-wire the matching pair from [`Editors`](property-grid-templates.md#editors) and [`CellRenderers`](property-grid-templates.md#cellrenderers).

Use when the column type is known at the call site and you want HitTable-style "batteries included" ergonomics. For discovery-driven columns, use [`AutoColumns`](../factories/data-grid.md#autocolumns) with a [`TypeRegistry`](property-grid-templates.md#typeregistry) populated with built-in editors and renderers.

```csharp
public static class TypedColumns
```

Namespace: `Microsoft.UI.Reactor.Controls`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `CheckBoxColumn`

```csharp
public static ColumnBuilder<T> CheckBoxColumn<T>(string name, Func<T, object?> accessor, string? displayName = null, double? width = null, PinPosition pin = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | `string` |  |  |
| `accessor` | `Func<T, object?>` |  |  |
| `displayName` | `string?` | `null` |  |
| `width` | `double?` | `null` |  |
| `pin` | [`PinPosition`](../data/column-attributes.md#pinposition) | `default` |  |

#### `ColorColumn`

```csharp
public static ColumnBuilder<T> ColorColumn<T>(string name, Func<T, object?> accessor, string? displayName = null, double? width = null, PinPosition pin = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | `string` |  |  |
| `accessor` | `Func<T, object?>` |  |  |
| `displayName` | `string?` | `null` |  |
| `width` | `double?` | `null` |  |
| `pin` | [`PinPosition`](../data/column-attributes.md#pinposition) | `default` |  |

#### `ComboBoxColumn`

```csharp
public static ColumnBuilder<T> ComboBoxColumn<T, TValue>(string name, Func<T, object?> accessor, IReadOnlyList<TValue> choices, string? displayName = null, double? width = null, PinPosition pin = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | `string` |  |  |
| `accessor` | `Func<T, object?>` |  |  |
| `choices` | `IReadOnlyList<TValue>` |  |  |
| `displayName` | `string?` | `null` |  |
| `width` | `double?` | `null` |  |
| `pin` | [`PinPosition`](../data/column-attributes.md#pinposition) | `default` |  |

#### `DateColumn`

```csharp
public static ColumnBuilder<T> DateColumn<T>(string name, Func<T, object?> accessor, string format = "d", string? displayName = null, double? width = null, PinPosition pin = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | `string` |  |  |
| `accessor` | `Func<T, object?>` |  |  |
| `format` | `string` | `"d"` |  |
| `displayName` | `string?` | `null` |  |
| `width` | `double?` | `null` |  |
| `pin` | [`PinPosition`](../data/column-attributes.md#pinposition) | `default` |  |

#### `HyperlinkColumn`

```csharp
public static ColumnBuilder<T> HyperlinkColumn<T>(string name, Func<T, object?> accessor, string? displayTextFormat = null, string? displayName = null, double? width = null, PinPosition pin = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | `string` |  |  |
| `accessor` | `Func<T, object?>` |  |  |
| `displayTextFormat` | `string?` | `null` |  |
| `displayName` | `string?` | `null` |  |
| `width` | `double?` | `null` |  |
| `pin` | [`PinPosition`](../data/column-attributes.md#pinposition) | `default` |  |

#### `NumberColumn`

```csharp
public static ColumnBuilder<T> NumberColumn<T>(string name, Func<T, object?> accessor, double? min = null, double? max = null, double smallChange = 1, string? displayName = null, string? format = null, double? width = null, PinPosition pin = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | `string` |  |  |
| `accessor` | `Func<T, object?>` |  |  |
| `min` | `double?` | `null` |  |
| `max` | `double?` | `null` |  |
| `smallChange` | `double` | `1` |  |
| `displayName` | `string?` | `null` |  |
| `format` | `string?` | `null` |  |
| `width` | `double?` | `null` |  |
| `pin` | [`PinPosition`](../data/column-attributes.md#pinposition) | `default` |  |

#### `TimeColumn`

```csharp
public static ColumnBuilder<T> TimeColumn<T>(string name, Func<T, object?> accessor, string format = "t", string? displayName = null, double? width = null, PinPosition pin = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | `string` |  |  |
| `accessor` | `Func<T, object?>` |  |  |
| `format` | `string` | `"t"` |  |
| `displayName` | `string?` | `null` |  |
| `width` | `double?` | `null` |  |
| `pin` | [`PinPosition`](../data/column-attributes.md#pinposition) | `default` |  |

#### `ToggleSwitchColumn`

```csharp
public static ColumnBuilder<T> ToggleSwitchColumn<T>(string name, Func<T, object?> accessor, string? onContent = null, string? offContent = null, string? displayName = null, double? width = null, PinPosition pin = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `name` | `string` |  |  |
| `accessor` | `Func<T, object?>` |  |  |
| `onContent` | `string?` | `null` |  |
| `offContent` | `string?` | `null` |  |
| `displayName` | `string?` | `null` |  |
| `width` | `double?` | `null` |  |
| `pin` | [`PinPosition`](../data/column-attributes.md#pinposition) | `default` |  |
