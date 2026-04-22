# DataGrid Grouping & TreeGrid Spec

## Overview

This spec covers two related but distinct features:

1. **DataGrid Grouping** — adding group-by capability to the existing `DataGrid<T>` control
2. **TreeGrid** — a new control for displaying inherently hierarchical data

These are separate controls because the data contracts are fundamentally different:
- **Grouping** operates on **flat data** — the grid computes groups from field values
- **TreeGrid** operates on **hierarchical data** — parent-child relationships are intrinsic to the data model

The slippery slope of "just add grouping" → "nested grouping" → "custom hierarchy" is avoided by drawing a hard line: if the data *is* a tree, use `TreeGrid`. If the data is flat and you want to *view it* grouped, use `DataGrid` with `GroupBy`.

---

## Part 1: DataGrid Grouping

### Motivation

Business applications frequently need to group flat tabular data by one or more fields (e.g., orders by status, tasks by assignee). This is a view-level concern — the underlying data is flat, and grouping is a presentation transformation applied on top of sort/filter.

### Data Source Impact

Grouping extends the existing `DataRequest` / `DataSourceCapabilities` / `DataPage<T>` contracts.

#### New Types

```csharp
// ── Data/GroupDescriptor.cs ────────────────────────────────────

namespace Microsoft.UI.Reactor.Data;

/// <summary>
/// Describes a grouping operation on a field.
/// </summary>
/// <param name="Field">The field name to group by.</param>
/// <param name="Direction">Sort direction of group keys.</param>
public record GroupDescriptor(
    string Field,
    SortDirection Direction = SortDirection.Ascending);
```

#### Changes to Existing Types

```csharp
// ── DataSourceCapabilities gains ServerGroup ───────────────────

[Flags]
public enum DataSourceCapabilities
{
    // ... existing flags ...
    ServerGroup = 1 << 7,
}
```

#### Grouped Row Stream Model

**Important:** Grouping introduces synthetic rows (group headers/footers) that don't exist in `DataPage<T>.Items`. Rather than overloading `DataPage<T>` with side-band group boundary data, the grouped DataGrid builds an internal **visible row stream** from the materialized data:

```csharp
// ── Internal to DataGrid, not part of the public data contract ─

/// <summary>
/// A row in the grouped virtual list — either a data row or a synthetic group row.
/// </summary>
internal abstract record GroupedRow<T>;

internal record DataRow<T>(T Item, RowKey Key, GroupPath GroupPath) : GroupedRow<T>;

internal record GroupHeaderRow<T>(
    GroupPath Path,
    object? GroupKey,
    string Field,
    int ItemCount,
    bool IsExpanded) : GroupedRow<T>;

internal record GroupFooterRow<T>(
    GroupPath Path,
    object? GroupKey,
    string Field,
    int ItemCount) : GroupedRow<T>;
```

The `GroupedRowIndex` (internal) transforms materialized data + group descriptors into this visible row stream, handling collapse/expand of groups. This keeps the public `IDataSource<T>` / `DataPage<T>` contract unchanged.

#### Group Path (Composite Identity)

For nested grouping, a group's identity is its full path, not just a single key value. "Bug" under `Status=Open` is different from "Bug" under `Status=Closed`:

```csharp
/// <summary>
/// Identifies a group by its full path through the group hierarchy.
/// </summary>
public record GroupPath(IReadOnlyList<GroupPathSegment> Segments)
{
    public static GroupPath Root => new([]);
}

/// <summary>A single level in a group path.</summary>
public record GroupPathSegment(string Field, object? Key);
```

Expansion state is tracked by `GroupPath`, not by `object GroupKey` alone.

#### V1 Constraint: Materialized Sources Only

**Client-side grouping is only supported for fully in-memory data sources** (i.e., sources where all items matching current filters are loaded). This is because:
- Grouping requires knowing all items to compute group boundaries
- Continuation-token paging is range/index-oriented and cannot guarantee group-coherent pages
- Silently loading all data from a large remote source would be a performance trap

**Enforcement:**
- `ListDataSource<T>` supports grouping natively (it already holds all data)
- For sources with `ServerGroup` capability, the server handles grouping (future work — the server returns pre-grouped pages)
- For other sources, setting `GroupBy` throws `NotSupportedException` at configuration time with a clear message

Server-side grouped paging (where the server returns pre-grouped, collapsible pages) is deferred to a future version that can design a proper grouped page contract.

#### Sort Semantics with Grouping

When grouping is active, sort descriptors are partitioned:

- **Group ordering:** Fields that appear in `GroupBy` determine the order of groups. Each `GroupDescriptor` has its own `Direction`.
- **Within-group ordering:** Fields in `DataRequest.Sort` that are NOT in `GroupBy` determine the order of items within each group.
- **Conflict rule:** A field cannot have contradictory sort directions in `GroupBy` vs `Sort`. If a grouped field also appears in `Sort`, the `GroupBy` direction wins and the redundant `Sort` entry is ignored.

#### Client-Side Grouping in ListDataSource

When the source does NOT have `ServerGroup` capability, `ListDataSource<T>` applies grouping client-side:

1. Apply filters
2. Apply search
3. Apply sorts
4. **Group by** the requested fields (stable sort by group key, preserving sort order within groups)
5. Build the internal `GroupedRowIndex` from the sorted, grouped results

**Important constraint:** Client-side grouping requires all data to be in memory. `ListDataSource` already loads everything, so this works naturally. For server-backed sources without `ServerGroup`, the DataGrid should warn or require all data to be loaded first (this is the same constraint that client-side sort/filter already has).

### DSL API

```csharp
// ── New parameters on DataGrid<T> ──────────────────────────────

public static Element DataGrid<T>(
    IDataSource<T> source,
    IReadOnlyList<FieldDescriptor> columns,
    // ... existing parameters ...

    // Grouping
    IReadOnlyList<GroupDescriptor>? groupBy = null,
    bool groupsCollapsedByDefault = false,
    Func<GroupHeaderContext, Element>? groupHeaderTemplate = null,
    Func<GroupFooterContext, Element>? groupFooterTemplate = null)
```

#### Context Records

```csharp
/// <summary>Context for rendering a group header row.</summary>
public record GroupHeaderContext(
    GroupDescriptor Group,
    GroupPath Path,
    object? GroupKey,
    int ItemCount,
    int Depth,          // 0 for first group-by, 1 for nested, etc.
    bool IsExpanded,
    Action ToggleExpand);

/// <summary>Context for rendering a group footer row.</summary>
public record GroupFooterContext(
    GroupDescriptor Group,
    GroupPath Path,
    object? GroupKey,
    int ItemCount,
    int Depth);
```

### Usage Example

```csharp
DataGrid(
    ordersSource,
    columns: [
        Column<Order>("Customer", o => o.CustomerName),
        Column<Order>("Status", o => o.Status),
        Column<Order>("Amount", o => o.Amount, format: "C2"),
        Column<Order>("Date", o => o.OrderDate, format: "d"),
    ],
    groupBy: [new GroupDescriptor("Status")],
    groupHeaderTemplate: ctx =>
        HStack(
            Text($"{ctx.GroupKey}").Bold(),
            Text($"({ctx.ItemCount} items)").Opacity(0.6)
        ).Padding(8, 4),
    selectionMode: SelectionMode.Multiple
);
```

### State Machine Impact (DataGridState<T>)

`DataGridState<T>` gains:

- `_groupDescriptors: List<GroupDescriptor>` — current grouping config
- `_collapsedGroups: HashSet<GroupKey>` — which groups are collapsed (tracked by composite key: field + value)
- `ToggleGroup(GroupKey)`, `ExpandAllGroups()`, `CollapseAllGroups()`
- Group headers and footers are **synthetic rows** injected into the virtual list between data rows
- Selection skips group header/footer rows (only data rows are selectable)
- Keyboard navigation: Enter on a group header toggles expand/collapse

### Rendering

Group headers/footers are rendered as full-width rows spanning all columns. They participate in virtualization (they are items in the flat list). The virtual list sees a sequence of:

```
[GroupHeader: Status=Pending]    ← synthetic, full-width
  Row: Order-001                 ← data row
  Row: Order-003                 ← data row
[GroupFooter: Status=Pending]    ← synthetic, optional
[GroupHeader: Status=Shipped]    ← synthetic
  Row: Order-002                 ← data row
  ...
```

When a group is collapsed, its data rows and footer are removed from the virtual list (same pattern as tree expand/collapse).

### Nested Grouping

Multiple `GroupDescriptor` entries create nested groups. The outer group is listed first:

```csharp
groupBy: [new GroupDescriptor("Status"), new GroupDescriptor("Region")]
```

Produces:
```
[GroupHeader: Status=Pending, Depth=0]
  [GroupHeader: Region=West, Depth=1]
    Row: Order-001
    Row: Order-003
  [GroupHeader: Region=East, Depth=1]
    Row: Order-005
```

Nested grouping is limited to 3 levels to keep the UX manageable. Beyond that, the developer should use `TreeGrid`.

---

## Part 2: TreeGrid

### Motivation

Applications that work with inherently hierarchical data — file trees, org charts, call stacks, resource hierarchies, nested categories — need a control that understands parent-child relationships natively. This is different from grouping: every row is a real data item, depth is arbitrary, and the hierarchy is intrinsic to the data model.

### Data Source Contract

TreeGrid introduces its own data source interface family, separate from `IDataSource<T>`. The flat `GetPageAsync` model doesn't map to tree traversal — trees need per-node child access.

#### ITreeDataSource<T> — Unified Interface

TreeGrid uses a single async-first interface. Sync in-memory sources use a provided adapter.

```csharp
// ── Data/ITreeDataSource.cs ────────────────────────────────────

namespace Microsoft.UI.Reactor.Data;

/// <summary>
/// A tree data source where children are fetched on demand.
/// Supports cursor-based pagination at each level independently.
/// 
/// For in-memory trees, use the provided adapters (ListTreeDataSource,
/// RecursiveTreeDataSource) which implement this interface with synchronous
/// Task.FromResult semantics.
/// </summary>
public interface ITreeDataSource<T>
{
    /// <summary>Fetches a page of root-level items.</summary>
    Task<TreePage<T>> GetRootsAsync(
        TreePageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a page of children for a parent item.</summary>
    /// <param name="parentKey">
    /// The stable key of the parent item. Uses RowKey rather than T
    /// so that identity survives object replacement across refreshes.
    /// </param>
    Task<TreePage<T>> GetChildrenAsync(
        RowKey parentKey,
        TreePageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether an item may have children. Used to show expand affordance
    /// before children are loaded. May return true optimistically (it's OK
    /// if GetChildrenAsync later returns an empty page).
    /// </summary>
    bool HasChildren(T item);

    /// <summary>Gets the stable identity key for an item.</summary>
    RowKey GetRowKey(T item);

    /// <summary>Declares capabilities of this tree data source.</summary>
    TreeDataSourceCapabilities Capabilities { get; }
}

/// <summary>
/// Optional: fired when the tree structure changes (items added, removed, moved).
/// </summary>
public interface INotifyTreeChanged
{
    event Action? TreeChanged;
}
```

**Design note:** `GetChildrenAsync` takes `RowKey parentKey` rather than `T parent`. This ensures identity is stable across refreshes — if the parent object is replaced with a new instance (e.g., after a data reload), the key still matches. This follows the same pattern as `IKeyedDataSource<T>.GetByKeyAsync`.

#### Supporting Types

```csharp
/// <summary>
/// A page of tree items (roots or children of a specific parent).
/// </summary>
public record TreePage<T>(
    IReadOnlyList<T> Items,
    string? ContinuationToken = null,
    int? TotalCount = null);

/// <summary>
/// Request for a page of tree items.
/// </summary>
public record TreePageRequest
{
    /// <summary>Number of items per page. Default 50.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Token for fetching the next page of siblings. Null for first page.</summary>
    public string? ContinuationToken { get; init; }

    /// <summary>Sort operations to apply within this level.</summary>
    public IReadOnlyList<SortDescriptor>? Sort { get; init; }
}

/// <summary>
/// Capability flags for tree data sources.
/// </summary>
[Flags]
public enum TreeDataSourceCapabilities
{
    None = 0,
    ServerSort = 1 << 0,
    ServerCount = 1 << 1,
    Refresh = 1 << 2,
}
```

#### Built-In Implementations

```csharp
// ── Data/Providers/ListTreeDataSource.cs ───────────────────────

/// <summary>
/// In-memory tree data source built from a flat list with a parent accessor.
/// Implements ITreeDataSource with synchronous semantics (Task.FromResult).
/// </summary>
public class ListTreeDataSource<T> : ITreeDataSource<T>, INotifyTreeChanged
{
    public ListTreeDataSource(
        IReadOnlyList<T> items,
        Func<T, RowKey> getKey,
        Func<T, RowKey?> getParentKey); // null = root item
}

/// <summary>
/// In-memory tree data source built from pre-structured recursive data.
/// Implements ITreeDataSource with synchronous semantics (Task.FromResult).
/// </summary>
public class RecursiveTreeDataSource<T> : ITreeDataSource<T>, INotifyTreeChanged
{
    public RecursiveTreeDataSource(
        IReadOnlyList<T> roots,
        Func<T, IReadOnlyList<T>> getChildren,
        Func<T, RowKey> getKey);
}
```

### FlatTreeIndex (Internal Engine)

The core of TreeGrid is the **flat tree index** — a data structure that maintains a flattened view of the visible tree. Only expanded nodes contribute their children to the flat list. This is what the virtualizer iterates.

```
Internal type — not exposed in public API.

FlatRowEntry {
    T Item
    RowKey Key
    int Depth          // 0 = root, 1 = child of root, ...
    int ParentIndex    // index of parent in flat list (-1 for roots)
    bool HasChildren
    bool IsExpanded
}
```

**Operations:**
- `Expand(index)` — inserts children after the item, recursively for already-expanded descendants
- `Collapse(index)` — removes all descendant entries
- `Rebuild()` — reconstructs flat list from data source + expansion state
- Expansion state is tracked by **RowKey** (survives rebuild/sort)

For `IAsyncTreeDataSource`, expand triggers an async fetch. A loading indicator is shown for the expanding node until children arrive. Children may arrive in pages — a "load more" affordance is shown after the last loaded child.

### DSL API

```csharp
// ── Controls/TreeGrid/TreeGridDsl.cs ───────────────────────────

namespace Microsoft.UI.Reactor.Controls;

public static class TreeGridDsl
{
    /// <summary>
    /// Creates a TreeGrid from a tree data source (sync or async).
    /// </summary>
    public static Element TreeGrid<T>(
        ITreeDataSource<T> source,
        IReadOnlyList<FieldDescriptor> columns,
        SelectionMode selectionMode = SelectionMode.None,
        Action<IReadOnlySet<RowKey>>? onSelectionChanged = null,
        double? rowHeight = 32,
        double indentWidth = 20,
        bool expandedByDefault = false,
        int? expandToDepth = null,            // auto-expand first N levels
        Func<CellContext<T>, Element>? cellTemplate = null,
        Func<TreeRowContext<T>, Element>? rowTemplate = null,
        Func<HeaderContext, Element>? headerTemplate = null,
        Element? loadingTemplate = null,
        Element? emptyTemplate = null);

    /// <summary>
    /// Creates a TreeGrid with auto-generated columns.
    /// </summary>
    public static Element TreeGrid<T>(
        ITreeDataSource<T> source,
        TypeRegistry registry,
        Func<FieldDescriptor, FieldDescriptor>? columnOverrides = null,
        /* same optional parameters */);
}
```

#### Tree-Specific Context Records

```csharp
/// <summary>Context for rendering a tree row (extends row context with depth info).</summary>
public record TreeRowContext<T>(
    T Row,
    RowKey Key,
    int RowIndex,
    int Depth,
    bool HasChildren,
    bool IsExpanded,
    bool IsSelected,
    Action ToggleExpand,
    IReadOnlyList<Element> Cells);
```

Note: `CellContext<T>` and `HeaderContext` are reused from DataGrid — they already have everything needed. The first column in a tree grid gets automatic indentation + expand/collapse chevron.

### Usage Example

```csharp
// ── Recursive data model ───────────────────────────────────────

record FileNode(string Name, long Size, DateTime Modified, List<FileNode> Children);

// ── Build data source ──────────────────────────────────────────

var source = new RecursiveTreeDataSource<FileNode>(
    roots: fileSystem.RootFolders,
    getChildren: n => n.Children,
    getKey: n => n.Name);

// ── Render ─────────────────────────────────────────────────────

TreeGrid(
    source,
    columns: [
        Column<FileNode>("Name", n => n.Name),
        Column<FileNode>("Size", n => n.Size, format: "N0"),
        Column<FileNode>("Modified", n => n.Modified, format: "g"),
    ],
    selectionMode: SelectionMode.Multiple,
    expandToDepth: 1,     // auto-expand roots
    indentWidth: 24
);
```

#### Async Example

```csharp
// ── Remote API-backed tree ─────────────────────────────────────

class OrgChartDataSource : ITreeDataSource<Employee>
{
    public async Task<TreePage<Employee>> GetRootsAsync(TreePageRequest req, CancellationToken ct)
        => await _api.GetTopLevelManagers(req.PageSize, req.ContinuationToken, ct);

    public async Task<TreePage<Employee>> GetChildrenAsync(
        RowKey parentKey, TreePageRequest req, CancellationToken ct)
        => await _api.GetDirectReports(parentKey.Value, req.PageSize, req.ContinuationToken, ct);

    public bool HasChildren(Employee e) => e.DirectReportCount > 0;
    public RowKey GetRowKey(Employee e) => e.Id;
    public TreeDataSourceCapabilities Capabilities => TreeDataSourceCapabilities.ServerSort;
}

TreeGrid(
    new OrgChartDataSource(api),
    columns: [
        Column<Employee>("Name", e => e.FullName),
        Column<Employee>("Title", e => e.JobTitle),
        Column<Employee>("Department", e => e.Department),
    ],
    selectionMode: SelectionMode.Single,
    rowHeight: 40
);
```

### State Machine (TreeGridState<T>)

Parallel to `DataGridState<T>`, but with tree-specific state:

```csharp
class TreeGridState<T>
{
    // ── Expansion ──────────────────────────────────────────────
    HashSet<RowKey> _expandedKeys;
    void ToggleExpand(RowKey key);
    void ExpandTo(RowKey key);        // expand all ancestors
    void ExpandAll();
    void CollapseAll();

    // ── Flat index ─────────────────────────────────────────────
    FlatTreeIndex<T> _flatIndex;      // the flattened visible tree
    int VisibleRowCount { get; }

    // ── Sort (per-level) ───────────────────────────────────────
    List<SortDescriptor> _sorts;
    void ToggleSort(string field, bool additive);
    // Sorting is applied per-level: siblings are sorted among themselves
    // Parent-child relationships are never broken by sorting

    // ── Selection (reused pattern from DataGridState) ──────────
    HashSet<RowKey> _selectedKeys;
    RowKey? AnchorKey;
    RowKey? FocusedKey;
    int SelectionVersion;

    // ── Async loading ──────────────────────────────────────────
    HashSet<RowKey> _loadingNodes;    // nodes currently fetching children
    Dictionary<RowKey, Exception?> _nodeErrors; // nodes that failed to load
    bool IsNodeLoading(RowKey key);
    bool HasNodeError(RowKey key);
    void RetryLoad(RowKey key);       // clear error and re-fetch
}
```

#### Node Loading & Error States

When a node is expanded and its children must be fetched asynchronously:

1. **Loading state:** The node shows a loading indicator (spinner) below it in the tree. `_loadingNodes` tracks which nodes are currently fetching.
2. **Success:** Children are inserted into the flat index. If more pages exist, a "load more" row appears after the last child.
3. **Error:** The error is stored in `_nodeErrors`. The node shows an error indicator with a "retry" affordance. The node remains expanded (so the user can see the error and retry).
4. **Cancellation:** If the user collapses a node while its children are loading, the `CancellationToken` is cancelled. Rapid expand/collapse coalesces — only the final state matters.

#### Selection Behavior on Collapse

When a parent node is collapsed and some of its descendants are selected:

- **Selected descendants remain selected** (their keys stay in `_selectedKeys`)
- The selection count reflects all selected items, including hidden descendants
- When the parent is re-expanded, the previously selected descendants reappear as selected
- `Ctrl+A` only selects currently **visible** rows (not hidden descendants)

#### Expansion State Stability

Expansion state is tracked by `RowKey`, not by object reference or index. This means:
- Expansion survives data source refresh (`INotifyTreeChanged`)
- Expansion survives sort changes (the tree is re-sorted but expanded nodes stay expanded)
- If an expanded node's key disappears from the data, its expansion state is silently discarded

### Keyboard Navigation

Same as DataGrid for vertical navigation, with tree-specific additions:

| Key | Action |
|-----|--------|
| ↑/↓ | Move focus to previous/next visible row |
| → | If collapsed and has children: expand. If expanded: move to first child |
| ← | If expanded: collapse. If collapsed: move to parent |
| Enter | Toggle expand/collapse |
| Home/End | First/last visible row |
| * (numpad) | Expand all children of focused node |
| Ctrl+A | Select all visible rows |

### Column Behavior

TreeGrid reuses `FieldDescriptor` for column definitions and the same `Column<T>()` builder from `ColumnDsl`. The first column is special:

- **Indentation**: padded by `Depth × IndentWidth` pixels on the left
- **Expand chevron**: a `▶`/`▼` toggle before the cell content (only if `HasChildren`)
- If the first column has a `CellRenderer`, it receives the raw value — indentation and chevron are added by the framework, not the developer

Other columns behave identically to DataGrid columns (width modes, resize, reorder, sort indicators, pin).

---

## Part 3: Shared Infrastructure

### What's Shared (Reused As-Is)

| Component | Used By | Notes |
|-----------|---------|-------|
| `FieldDescriptor` | DataGrid, TreeGrid, PropertyGrid, FormField | Already unified |
| `ColumnDsl` / `Column<T>()` | DataGrid, TreeGrid | Same fluent builder |
| `RowKey` | DataGrid, TreeGrid | Same identity type |
| `SortDescriptor` | DataGrid, TreeGrid, flat and tree data sources | Same type |
| `SelectionMode` | DataGrid, TreeGrid | Same enum |
| `HeaderContext` | DataGrid, TreeGrid | Same header rendering context |
| `CellContext<T>` | DataGrid, TreeGrid | Same cell rendering context |

### What's Separate

| Component | Reason |
|-----------|--------|
| `DataGridState<T>` vs `TreeGridState<T>` | Different state: pages vs flat tree index, group collapse vs tree expand |
| `IDataSource<T>` vs `ITreeDataSource<T>` / `IAsyncTreeDataSource<T>` | Fundamentally different access patterns |
| `DataGridComponent<T>` vs `TreeGridComponent<T>` | Different rendering logic (synthetic group rows vs indented tree rows) |
| `DataRequest` vs `TreePageRequest` | Flat paging vs per-node paging |

### What Could Be Extracted Later

If we find the two state machines diverging less than expected, we could extract:

- `SelectionState` — the selection logic (selected keys, anchor, range) is identical
- `ColumnLayoutState` — width resolution, resize, reorder is identical
- `SortState` — sort descriptor management is similar (tree adds per-level constraint)

This is a **refactor opportunity**, not a V1 requirement. Better to duplicate slightly and extract when the pattern is clear than to over-abstract up front.

---

## Design Decisions & Rationale

### Why Separate Controls (Not One Uber-Grid)?

1. **Data contract is fundamentally different.** `GetPageAsync(DataRequest)` vs `GetChild(parent, index)` — these can't be unified without one side carrying dead weight.

2. **Developer clarity.** "My data is flat, I want to group it" → `DataGrid + GroupBy`. "My data is hierarchical" → `TreeGrid`. No ambiguity.

3. **State complexity.** A combined control would need: flat paging OR tree index, group expansion OR tree expansion, per-level sort OR global sort, async page fetch OR per-node fetch. Every code path would need "if tree mode" branches.

4. **Migration cost is low.** If a developer starts with DataGrid+grouping and later needs a real tree:
   - Column definitions (`FieldDescriptor`) transfer directly
   - Selection mode and callbacks have the same signatures
   - Templates (`CellContext<T>`, `HeaderContext`) are the same types
   - Only the data source and control name change

### Why Grouping Is a DataGrid Feature (Not TreeGrid-Lite)

- Grouping is a **view transformation** on flat data, not a data model concern
- It fits naturally alongside sort/filter in `DataRequest`
- Group headers are synthetic (not real data items), unlike tree rows
- The collapse/expand for groups is simpler (1-3 levels, by field value)

### Per-Level Sort in TreeGrid

Sorting in a tree always sorts **siblings** — children of the same parent. Global sort across the entire tree is undefined (what does it mean to sort a child before its uncle?). This matches user expectations from file explorers and outline views.

### Async Tree: Per-Node Pagination

Each parent node independently fetches its children. This is essential for:
- Deep trees where loading everything is impractical
- APIs that expose children per-entity (REST: `/employees/{id}/reports`)
- Cursor-based pagination patterns (GraphQL Relay connections)

A node that is loading shows a spinner in place of its children. If a node has more children than one page, a "load more" row appears after the last loaded child.

---

## Open Questions / Future Work

1. **Editing in TreeGrid** — V1 is read-only. Should V2 support inline editing? Drag-to-reparent? This is complex and should be specced separately if needed.

2. **Aggregation in Group Headers** — Should `GroupHeaderContext` include aggregate values (sum, average, count by column)? This requires the data source to compute aggregates, adding to the contract. Defer to V2 unless demand is clear.

3. **Search in TreeGrid** — Should TreeGrid support search? If so, should it auto-expand to show matches? This is a "search in tree" problem that needs its own design.

4. **Accessibility** — Both controls need proper AutomationPeer implementations. Group headers should announce as group headers with expand/collapse state. Tree rows should announce depth and expand state.

5. **Column State Persistence** — DataGrid may already handle this; TreeGrid should support saving/restoring column widths, sort state, and expansion state.

6. **Drag and Drop** — Row reordering in DataGrid, drag-to-reparent in TreeGrid. Both are V2 features.
