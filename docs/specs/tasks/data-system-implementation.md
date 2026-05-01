# Reactor Data System — Implementation Tasks

Derived from: `docs/spec/017-data-system-design.md`

---

## Phase 0: Shared Foundation (2-3 weeks)

### 0A. FieldDescriptor Definition

#### 0A.1 FieldDescriptor Record
- [x] Create `Reactor/Data/FieldDescriptor.cs`
- [x] Define core identity properties: `Name` (required), `DisplayName`, `FieldType` (required)
- [x] Define access properties: `GetValue` (required `Func<object, object?>`), `SetValue` (`Func<object, object?, object>?`), `IsReadOnly`
- [x] Define metadata properties: `Category`, `Description`, `Order`
- [x] Define editing properties: `Editor` (`Func<object, Action<object>, Element>?`)
- [x] Define validation properties: `Validators` (`IReadOnlyList<IValidator>?`), `AsyncValidators` (`IReadOnlyList<IAsyncValidator>?`)
- [x] Define grid-specific properties: `Width`, `MinWidth`, `MaxWidth`, `Flex`, `Sortable` (default true), `Filterable` (default true), `Pin` (`PinPosition`), `CellRenderer`, `FormatValue`
- [x] Define `PinPosition` enum: `None`, `Left`, `Right`
- [x] Unit tests: record creation, `with` expressions, default values
- [x] Unit tests: `SetValue` return-new-owner pattern — mutable object returns same reference
- [x] Unit tests: `SetValue` return-new-owner pattern — immutable record returns new object

#### 0A.2 Grid-Specific Attributes
- [x] Create `ColumnWidthAttribute(double width)` with optional `MinWidth`, `MaxWidth`
- [x] Create `ColumnPinAttribute(PinPosition position)`
- [x] Create `NotSortableAttribute`
- [x] Create `NotFilterableAttribute`
- [x] Unit tests: attribute construction and property access

---

### 0B. PropertyGrid Migration — PropertyDescriptor to FieldDescriptor (D1)

> **Critical path**: This migration touches the entire PropertyGrid subsystem. Every step
> must preserve existing behavior exactly. Run the full PropertyGrid test suite after each
> sub-task to catch regressions early.

#### 0B.1 ReflectionTypeMetadataProvider Migration
- [x] Update `ReflectionTypeMetadataProvider.CreateMetadata()` to return `FieldDescriptor` lists instead of `PropertyDescriptor` lists
- [x] Update `CreateDescriptor()` to produce `FieldDescriptor` with return-new-owner `SetValue`
- [x] Generate `SetValue` for mutable properties: mutate in place, return same reference
- [x] Generate `SetValue` for immutable (init-only) properties: constructor invocation, return new object
- [x] Read new grid-specific attributes (`ColumnWidth`, `ColumnPin`, `NotSortable`, `NotFilterable`) alongside existing property attributes
- [x] **Regression test**: run all existing `PropertyGridTypeRegistryTests` — all must pass
- [x] Unit tests: `SetValue` generated for mutable class property
- [x] Unit tests: `SetValue` generated for immutable record property
- [x] Unit tests: `SetValue` is null for truly read-only properties (no setter, no constructor match)
- [x] Unit tests: grid attributes populate `Width`, `Pin`, `Sortable`, `Filterable` on `FieldDescriptor`
- [x] Unit tests: mixed mutable/immutable properties on the same type

#### 0B.2 TypeMetadata Migration
- [x] Update `TypeMetadata.Decompose` signature: `Func<object, IReadOnlyList<FieldDescriptor>>`
- [x] Add `CompactEditor` slot to `TypeMetadata` (D17)
- [x] Add `FullEditor` slot to `TypeMetadata` (D17)
- [x] Update `ArrayTypeMetadata` to work with `FieldDescriptor`
- [x] **Regression test**: run all existing `PropertyGridDecompositionTests` — all must pass
- [x] Unit tests: `Decompose` returns `FieldDescriptor` list with correct types and accessors
- [x] Unit tests: `CompactEditor` and `FullEditor` can be registered alongside `Editor`

#### 0B.3 TypeRegistry Extension
- [x] Add `RegisterCellRenderer<T>(Func<object, Element>)` to `TypeRegistry`
- [x] Add `RegisterFormatter<T>(Func<object?, string>)` to `TypeRegistry`
- [x] Implement tiered editor resolution: `CompactEditor ?? Editor ?? built-in` (for grid), `Editor ?? built-in` (for PropertyGrid/FormField), `FullEditor` (for expand)
- [x] Maintain existing fallback chain: exact match -> enum -> primitive -> array/IList -> reflection
- [x] **Regression test**: run all existing `PropertyGridTypeRegistryTests` — all must pass
- [x] Unit tests: `RegisterCellRenderer` stores and retrieves renderer by type
- [x] Unit tests: `RegisterFormatter` stores and retrieves formatter by type
- [x] Unit tests: tiered resolution — `CompactEditor` returned when requested, falls back to `Editor`
- [x] Unit tests: tiered resolution — `FullEditor` returned when requested, null when not registered

#### 0B.4 EditChain Migration
- [x] Rewrite `EditChain` to use `FieldDescriptor.SetValue` with return-new-owner pattern
- [x] Implement `ReferenceEquals` early termination: if `SetValue` returns same reference, stop propagation
- [x] Remove separate mutable/immutable code paths — unified via `SetValue`
- [x] Simplify `PropagateImmutableEdit` to chain `SetValue` calls upward through nesting path
- [x] **Regression test**: run all existing `PropertyGridDecompositionTests` — all must pass
- [x] Unit tests: mutable path — edit stops at mutated object (early termination)
- [x] Unit tests: immutable path — edit propagates to root, fires `OnRootChanged`
- [x] Unit tests: mixed path — immutable nested inside mutable stops at mutable ancestor
- [x] Unit tests: 3+ level deep nesting with mixed mutability

#### 0B.5 PropertyGridComponent Migration
- [x] Update `PropertyGridComponent.RenderProperty()` to accept `FieldDescriptor`
- [x] Update template delegates (`PropertyRowTemplate`, `PropertyLabelTemplate`) to accept `FieldDescriptor`
- [x] Update category grouping logic to use `FieldDescriptor.Category`
- [x] Update search/filter to use `FieldDescriptor.DisplayName` and `FieldDescriptor.Name`
- [x] Update DSL (`Factories.cs`) to work with `FieldDescriptor`
- [x] **Regression test**: run all existing `PropertyGridComponentTests` — all must pass
- [x] Unit tests: component renders properties from `FieldDescriptor` list
- [x] Unit tests: template delegates receive `FieldDescriptor` with correct properties

#### 0B.6 PropertyGridElement Migration
- [x] Update `PropertyGridElement` to reference `FieldDescriptor` (not `PropertyDescriptor`)
- [x] Ensure all public API surface uses `FieldDescriptor`
- [x] Update `PropertyGridDefaults` for `FieldDescriptor`
- [x] **Regression test**: run all existing PropertyGrid tests — complete suite must pass

#### 0B.7 Remove PropertyDescriptor
- [x] Delete `Reactor/PropertyGrid/PropertyDescriptor.cs`
- [x] Remove all remaining references to `PropertyDescriptor` in the codebase
- [x] Verify no compilation errors across the entire solution
- [x] **Regression test**: run full test suite — all tests must pass

#### 0B.8 PropertyGrid Migration — Comprehensive Regression
- [x] Run all `PropertyGridComponentTests` — pass
- [x] Run all `PropertyGridTypeRegistryTests` — pass
- [x] Run all `PropertyGridArrayTests` — pass
- [x] Run all `PropertyGridDecompositionTests` — pass
- [ ] Manual selfhost test: mutable POCO object — edit string, bool, int, enum, float properties
- [ ] Manual selfhost test: immutable record — edit property, verify new object created
- [ ] Manual selfhost test: nested immutable records (2+ levels) — edit leaf, verify root changes
- [ ] Manual selfhost test: array property — add, remove, reorder items
- [ ] Manual selfhost test: observable object — external mutation reflects in grid
- [ ] Manual selfhost test: category expand/collapse state preserved across edits
- [ ] Manual selfhost test: search/filter works on property names
- [ ] Manual selfhost test: read-only properties are non-editable
- [ ] Manual selfhost test: custom editor via `TypeRegistry.Register<T>()` still works
- [ ] Manual selfhost test: custom editor via `[PropertyEditor]` attribute still works
- [ ] Performance test: PropertyGrid with 100+ properties renders without perceptible lag

#### 0B.9 "..." Expand Affordance for FullEditor
- [x] When `FullEditor` is registered on a type, show a small "..." button next to the inline editor
- [x] Clicking "..." opens `FullEditor` in a `Flyout` anchored to the cell/field
- [x] Button appears automatically — no consumer configuration needed
- [x] Button hidden when `FullEditor` is null
- [x] Unit tests: "..." button visible when `FullEditor` registered
- [x] Unit tests: "..." button hidden when `FullEditor` is null
- [x] Unit tests: clicking "..." opens flyout with `FullEditor` content

---

### 0C. Core Data Access Types

#### 0C.1 RowKey
- [x] Create `Reactor/Data/RowKey.cs`
- [x] Define `readonly record struct RowKey(string Value)`
- [x] Implement implicit conversions from `string`, `int`, `Guid`
- [x] Override `ToString()` to return `Value`
- [x] Unit tests: construction from string, int, Guid
- [x] Unit tests: equality and hash code (record struct semantics)
- [x] Unit tests: implicit conversion operators

#### 0C.2 DataPage<T>
- [x] Create `Reactor/Data/DataPage.cs`
- [x] Define `DataPage<T>` record with `Items` (required), `ContinuationToken`, `TotalCount`
- [x] Unit tests: construction, `with` expressions, null continuation means last page

#### 0C.3 Sort and Filter Descriptors
- [x] Create `Reactor/Data/SortDescriptor.cs` — record with `Field`, `Direction`
- [x] Create `Reactor/Data/FilterDescriptor.cs` — record with `Field`, `Operator`, `Value`, `ValueTo`
- [x] Define `SortDirection` enum: `Ascending`, `Descending`
- [x] Define `FilterOperator` enum: `Equals`, `NotEquals`, `Contains`, `StartsWith`, `EndsWith`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Between`, `In`, `IsNull`, `IsNotNull`
- [x] Unit tests: descriptor construction and equality
- [x] Unit tests: `FilterDescriptor` with `Between` uses `ValueTo`

#### 0C.4 DataRequest
- [x] Create `Reactor/Data/DataRequest.cs`
- [x] Define `DataRequest` record with `PageSize` (default 50), `ContinuationToken`, `Sort`, `Filters`, `SearchQuery`, `Select`
- [x] Unit tests: default values, `with` expressions for building requests

#### 0C.5 IDataSource<T> and Capabilities
- [x] Create `Reactor/Data/IDataSource.cs`
- [x] Define `IDataSource<T>` interface: `GetPageAsync`, `GetRowKey`, `Capabilities`
- [x] Define `DataSourceCapabilities` flags enum: `None`, `ServerSort`, `ServerFilter`, `ServerSearch`, `ServerCount`, `ServerSelect`, `Mutate`, `Refresh`
- [x] Unit tests: capabilities flags composition and checking

#### 0C.6 Extension Interfaces
- [x] Define `IMutableDataSource<T>` : `IDataSource<T>` with `CreateAsync`, `UpdateAsync`, `DeleteAsync`
- [x] Define `IObservableDataSource<T>` : `IDataSource<T>` with `DataChanged` event
- [x] Define `IKeyedDataSource<T>` : `IDataSource<T>` with `GetByKeyAsync`, `GetByKeysAsync`
- [x] Unit tests: interface hierarchy compiles correctly (implementation tests in 0D)

---

### 0D. In-Memory Data Providers

#### 0D.1 ListDataSource<T>
- [x] Create `Reactor/Data/Providers/ListDataSource.cs`
- [x] Implement `IDataSource<T>` and `IMutableDataSource<T>`
- [x] Declare capabilities: `ServerSort | ServerFilter | ServerSearch | ServerCount | Mutate`
- [x] Implement `GetPageAsync`: apply filters, apply sorts, compute offset from continuation token, return page
- [x] Implement filter application for all `FilterOperator` values (client-side LINQ)
- [x] Implement sort application with multi-field priority ordering
- [x] Implement text search across string fields (case-insensitive contains)
- [x] Implement `CreateAsync`, `UpdateAsync`, `DeleteAsync` mutations
- [x] Unit tests: empty source returns empty page
- [x] Unit tests: paging through items with continuation tokens
- [x] Unit tests: `TotalCount` reflects filtered count, not full count
- [x] Unit tests: sort ascending and descending by string field
- [x] Unit tests: sort ascending and descending by numeric field
- [x] Unit tests: multi-field sort (primary + secondary)
- [x] Unit tests: filter `Equals`, `NotEquals` on string field
- [x] Unit tests: filter `Contains`, `StartsWith`, `EndsWith` on string field
- [x] Unit tests: filter `GreaterThan`, `LessThan`, `Between` on numeric field
- [x] Unit tests: filter `In` with multiple values
- [x] Unit tests: filter `IsNull`, `IsNotNull`
- [x] Unit tests: multiple filters AND-ed together
- [x] Unit tests: text search matches across string fields
- [x] Unit tests: `CreateAsync` adds item, visible in subsequent page
- [x] Unit tests: `UpdateAsync` modifies item by key
- [x] Unit tests: `DeleteAsync` removes item by key
- [x] Unit tests: `GetRowKey` returns stable key for item
- [ ] Performance test: 100k items — paging completes in <50ms
- [ ] Performance test: 100k items — sort + filter completes in <200ms

#### 0D.2 ObservableListDataSource<T>
- [x] Create `Reactor/Data/Providers/ObservableListDataSource.cs`
- [x] Implement `IObservableDataSource<T>` extending `ListDataSource<T>`
- [x] Subscribe to `ObservableCollection<T>.CollectionChanged`
- [x] Fire `DataChanged` on add, remove, move, reset
- [x] Support INPC on individual items: subscribe on visible items, unsubscribe on scroll-out
- [x] Implement `IDisposable` to clean up all subscriptions
- [x] Unit tests: adding item to collection fires `DataChanged`
- [x] Unit tests: removing item fires `DataChanged`
- [x] Unit tests: item INPC property change fires `DataChanged`
- [x] Unit tests: dispose cleans up all subscriptions
- [x] Unit tests: re-fetching after `DataChanged` reflects new state

---

### 0E. FormField FieldDescriptor Overload (D6)

#### 0E.1 Auto-Wired FormField
- [x] Add `FormField(FieldDescriptor field, object value, Action<object> onChange)` overload
- [x] Auto-resolve editor from `TypeRegistry` using `FieldDescriptor.FieldType`
- [x] Auto-set label from `FieldDescriptor.DisplayName ?? FieldDescriptor.Name`
- [x] Auto-set description from `FieldDescriptor.Description`
- [x] Auto-detect required from `FieldDescriptor.Validators` containing `RequiredValidator`
- [x] Auto-wire validators from `FieldDescriptor.Validators` and `FieldDescriptor.AsyncValidators`
- [x] Unit tests: FormField renders label from DisplayName
- [x] Unit tests: FormField shows required indicator when validator list includes Required
- [x] Unit tests: FormField renders resolved editor for string type
- [x] Unit tests: FormField renders resolved editor for bool type
- [x] Unit tests: FormField renders resolved editor for enum type
- [x] Unit tests: validation fires on value change

---

### 0F. Phase 0 Cross-Cutting

#### 0F.1 Stability & Error Handling
- [x] All data access operations accept `CancellationToken` and honor cancellation
- [x] `ListDataSource.GetPageAsync` handles concurrent modification gracefully (snapshot semantics)
- [x] `FieldDescriptor.SetValue` null-guards: calling on read-only field throws `InvalidOperationException`
- [x] Unit tests: cancellation token cancels in-flight operation
- [x] Unit tests: concurrent add during page read doesn't throw

#### 0F.2 Phase 0 Integration Test
- [x] Selfhost test: PropertyGrid editing a mutable object backed by `ListDataSource<T>`
- [x] Selfhost test: FormField auto-wired from `FieldDescriptor` with validation
- [x] Selfhost test: end-to-end — create `FieldDescriptor` from reflection, use in PropertyGrid, verify same behavior as pre-migration

---

## Phase 1: VirtualList Component (2-3 weeks)

### 1A. VirtualList Core

#### 1A.1 VirtualListElement<T>
- [x] Create `Reactor/Virtualization/VirtualListElement.cs`
- [x] Define `VirtualListElement<T>` record: `ItemCount` (required), `RenderItem` (required `Func<int, Element>`), `GetItemKey`, `ItemHeight`, `EstimatedItemHeight` (default 40)
- [x] Create DSL factory: `VirtualList(int itemCount, Func<int, Element> renderItem, ...)`
- [x] Unit tests: element construction with required properties

#### 1A.2 VirtualListComponent — ItemsRepeater Integration (D12)
- [x] Create `Reactor/Virtualization/VirtualListComponent.cs`
- [x] Compose with WinUI `ItemsRepeater` via existing `ElementFactory` reconciler bridge
- [x] Configure `StackLayout` (vertical) on ItemsRepeater
- [x] Wire `ItemCount` to ItemsRepeater item source
- [x] Wire `RenderItem` through `ElementFactory` element generation
- [x] Wire `GetItemKey` for reconciler stable identity
- [x] Unit tests: component creates ItemsRepeater with correct item count
- [x] Unit tests: RenderItem called for visible items only

#### 1A.3 Fixed-Height Fast Path (D11)
- [x] When `ItemHeight` is set, configure ItemsRepeater with uniform layout
- [x] O(1) offset calculation — no per-item measurement
- [x] Unit tests: fixed height mode uses uniform sizing
- [ ] Performance test: 100k items, fixed height — scroll at 60fps

#### 1A.4 Variable-Height Mode (D4)
- [x] When `ItemHeight` is null, use `EstimatedItemHeight` for initial layout
- [x] Let ItemsRepeater measure each item naturally
- [x] Scroll thumb sizing adjusts as items are measured
- [x] Unit tests: variable height items render at natural size
- [x] Unit tests: estimated height used for unmeasured items

#### 1A.5 Scroll Operations
- [x] Implement `ScrollToIndex(int index)` — programmatic scroll to item
- [x] Implement scroll position save/restore for state preservation
- [x] Handle scroll events for viewport tracking
- [x] Unit tests: ScrollToIndex scrolls to correct position
- [x] Unit tests: scroll position save and restore

---

### 1B. VirtualList Standalone Usage
- [ ] File list example (1M files, fixed height)
- [ ] Log viewer example (variable height, auto-scroll to bottom)
- [ ] Chat history example (variable height, scroll-to-bottom on new message)
- [ ] Performance benchmark: 100k rows at 60fps scroll target

---

## Phase 2: Basic DataGrid (4-6 weeks)

### 2A. DataGrid Core

#### 2A.1 DataGridState — Headless Core
- [x] Create `Reactor/DataGrid/DataGridState.cs` — headless state machine (D6.1 TanStack pattern)
- [x] Manage sort state, filter state, selection, editing, column order/sizing/visibility, scroll position
- [x] Pure logic, no UI dependencies — fully testable without rendering
- [x] Unit tests: state transitions for sort, filter, selection

#### 2A.2 DataGridElement
- [x] Create `Reactor/DataGrid/DataGridElement.cs`
- [x] Define `DataGridElement<T>` record per spec §6.3: `Source`, `Columns`, `Registry`, `ColumnOverrides`
- [x] Define selection props: `SelectionMode` (None/Single/Multiple), `OnSelectionChanged`
- [x] Define editing props: `OnRowChanged`, `EditMode` (Cell/Row), `Editable`
- [x] Define layout props: `RowHeight`, `EstimatedRowHeight`, `AllowColumnReorder`, `AllowColumnResize`
- [x] Define template overrides: `CellTemplate`, `RowTemplate`, `HeaderTemplate`, `LoadingTemplate`, `EmptyTemplate`
- [x] Define `SelectionMode`, `EditMode`, `CellContext<T>`, `RowContext<T>`, `HeaderContext` records
- [x] Create DSL factory: `DataGrid<T>(IDataSource<T> source, ...)` with explicit columns
- [x] Create DSL factory: `DataGrid<T>(IDataSource<T> source, TypeRegistry registry)` with reflection auto-columns
- [x] Unit tests: element construction with all property combinations

#### 2A.3 Column DSL
- [x] Create `Reactor/DataGrid/Factories.cs` — `Column<T>(name, accessor, ...)` helper (spec Appendix A)
- [x] Support `editable`, `displayName`, `format`, `width`, `pin` parameters
- [x] Support `.Validate(...)` chaining for per-column validators
- [x] Auto-generate columns from `TypeRegistry` + reflection when `Columns` is null
- [x] Unit tests: Column DSL produces correct FieldDescriptor
- [x] Unit tests: reflection auto-column generation

#### 2A.4 DataGridComponent
- [x] Create `Reactor/DataGrid/DataGridComponent.cs`
- [x] Compose `VirtualList` for the row area
- [x] Header row with sort indicators (fixed, above the virtualizing area)
- [x] Row rendering as FlexRow of cells, keyed by RowKey
- [x] Cell rendering via `TypeRegistry` cell renderers / formatters
- [x] Unit tests: component renders header and rows
- [x] Unit tests: cell renderers resolved from TypeRegistry

#### 2A.5 Column Resize and Reorder
- [x] Column resize via drag on header borders
- [x] Column reorder via drag-and-drop on header cells
- [x] Persist column widths across re-renders
- [x] Unit tests: resize changes column width
- [x] Unit tests: reorder changes column order

#### 2A.6 Keyboard Navigation
- [x] Arrow keys move cell focus
- [x] Enter activates edit on focused cell
- [x] Escape cancels edit
- [x] Tab moves to next cell
- [x] Home/End for first/last column
- [x] Unit tests: keyboard navigation behavior

#### 2A.7 Selection
- [x] Single row selection (click)
- [x] Multi-select via Ctrl+click
- [x] Range select via Shift+click
- [x] Selection state in `DataGridState`, exposed via `OnSelectionChanged`
- [x] Unit tests: selection modes
- [x] Unit tests: selection state management

### 2B. Sort and Filter Integration
- [x] Header click toggles sort (none → asc → desc → none)
- [x] Hybrid push-down: auto from `DataSourceCapabilities`, developer override (D3)
- [x] Client-side fallback when source lacks `ServerSort`/`ServerFilter`
- [x] Sort indicator UI in header cells
- [x] Integration with `TypeRegistry` for cell renderers
- [x] Unit tests: sort toggling
- [x] Unit tests: push-down vs client-side sort
- [x] Unit tests: capabilities-based auto-detection

---

## Phase 3: Inline Editing + Validation (3-4 weeks)

### 3A. Editing Modes

#### 3A.1 Cell Mode Editing (D10)
- [x] Click/Enter on cell activates editor
- [x] Editor resolved from `TypeRegistry` (same editors as PropertyGrid)
- [x] Return-new-owner `SetValue` for immutable record editing (D9)
- [x] Escape reverts, Enter/Tab commits
- [x] Unit tests: cell mode edit lifecycle

#### 3A.2 Row Mode Editing (D10)
- [x] Edit button activates full-row editing
- [x] All editable cells switch to editors simultaneously
- [x] Save/Cancel buttons for the row
- [x] Unit tests: row mode edit lifecycle

### 3B. Validation
- [x] Cell-level validation via `ValidationContext`
- [x] Row-level validation via `ValidationContext`
- [x] Per-row validation visualizer
- [x] Unit tests: cell validation
- [x] Unit tests: row validation

### 3C. Async Commit
- [x] `OnRowChanged` async commit with optimistic updates (D14)
- [x] Revert + error display on async failure
- [x] Loading indicator during commit
- [x] Unit tests: optimistic update
- [x] Unit tests: async failure revert

---

## Phase 4: Server-Side Data Sources (3-4 weeks)

### 4A. Block Cache
- [x] Create `Reactor/Data/DataPageCache.cs` — `DataPageCache<T>` with block-based caching
- [x] LRU eviction when cache exceeds configured block limit
- [x] Transparent re-fetch on cache miss during scroll
- [x] Invalidation on sort/filter change (clear cache, re-fetch from page 0)
- [x] Server-side sort/filter push-down via `DataSourceCapabilities`
- [x] Loading states: skeleton rows while blocks load, spinner for initial fetch
- [x] Unit tests: cache hit returns existing data
- [x] Unit tests: cache miss triggers fetch
- [x] Unit tests: LRU eviction drops oldest blocks
- [x] Unit tests: sort/filter change invalidates cache

### 4B. MS-Graph Provider
- [ ] `GraphDataSource<T>` — MS-Graph/OData provider
- [ ] OData query parameter mapping from `DataRequest`
- [ ] Pagination via `@odata.nextLink`
- [ ] Unit tests: query translation
- [ ] Integration tests: Graph API calls

### 4C. Entity Framework Provider
- [ ] `EfDataSource<T>` — Entity Framework provider
- [ ] IQueryable translation from `DataRequest`
- [ ] Server-side sort and filter via LINQ expressions
- [ ] Unit tests: query translation
- [ ] Integration tests: EF Core queries

---

## Phase 5: Advanced Features (4-6 weeks)

### 5A. Column Pinning
- [x] Frozen columns (left/right) + scrollable center (WinUI.TableView pattern)
- [x] Separate FrozenCellsPanel (left) / ScrollableCellsPanel (center) / FrozenCellsPanel (right)
- [x] Synchronized vertical scroll between frozen and scrollable panels
- [x] `FieldDescriptor.Pin` (PinPosition.Left/Right) drives column placement
- [x] Unit tests: pinned column rendering
- [x] Unit tests: scroll synchronization between panels

### 5B. Column Filters
- [x] Per-column filter popups (D8)
- [x] Template-based for future customization
- [x] Filter state management
- [x] Unit tests: filter popup interaction

### 5C. Column Visibility
- [x] Hide/show columns via header context menu
- [x] Column visibility state management
- [x] Unit tests: column visibility toggling

### 5D. Text Search
- [x] Global filter / text search
- [x] Highlight matched cells
- [x] Unit tests: search filtering

### 5E. Row Details
- [x] Optional detail view below a row
- [x] Expand/collapse toggle
- [x] Unit tests: row detail rendering

---

## Phase 6: Providers + Polish (3-4 weeks)

### 6A. Additional Providers
- [ ] `GraphQLDataSource<T>` — GraphQL with Relay connections
- [ ] `CosmosDataSource<T>` — Azure CosmosDB

### 6B. Integration
- [ ] Detail panel integration (DataGrid + PropertyGrid side-by-side)

### 6C. Quality
- [ ] Accessibility audit (ARIA grid roles, screen reader testing)
- [ ] Performance optimization (column virtualization, render batching)

---

## Future

- Grouping / tree data
- Pivot mode
- Export (CSV, Excel)
- Print layout
- Row drag-and-drop reordering
- Master-detail (expandable rows)
- Clipboard (copy/paste cells)
- Undo/redo editing
