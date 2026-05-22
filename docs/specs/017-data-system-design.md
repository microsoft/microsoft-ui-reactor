# Reactor Data System — Design Specification

**Date:** April 2026
**Status:** Draft / Brainstorm
**Context:** Designing a unified data editing family — data access abstraction,
data providers, DataGrid, and integration with the existing PropertyGrid,
FormField, and Validation subsystems.

---

## Table of Contents

1. [Vision](#1-vision)
2. [Existing Foundation](#2-existing-foundation)
3. [Data Access Layer](#3-data-access-layer)
4. [Data Providers](#4-data-providers)
5. [Shared Metadata Model — FieldDescriptor](#5-shared-metadata-model--fielddescriptor)
6. [The DataGrid Component](#6-the-datagrid-component)
7. [The Unified Editing Family](#7-the-unified-editing-family)
8. [Research Summary](#8-research-summary)
9. [Design Decisions (D1–D17)](#9-design-decisions)
10. [Implementation Phases](#10-implementation-phases)

---

## 1. Vision

Three UI components form a family that covers the full spectrum of data editing:

| Component | Scope | Use Case |
|-----------|-------|----------|
| **FormField** | Single value | Edit one field with label, validation, description |
| **PropertyGrid** | Single record | Edit all properties of an object |
| **DataGrid** | Collection | Browse, edit, filter, sort a set of records |

Today, PropertyGrid has its own metadata model (`PropertyDescriptor`, `TypeMetadata`,
`TypeRegistry`) and the Validation system is independent. The vision is to **unify
these** so all three components share:

- **Field metadata** — name, display name, type, category, description, order, read-only
- **Editors** — same type-to-editor registry for all three contexts
- **Validation** — same `IValidator` / `IAsyncValidator` pipeline, same `ValidationContext`
- **Data access** — a common abstraction for async, paginated, key-identified data

The DataGrid adds a fourth concern that PropertyGrid and FormField don't need:
**collection-level operations** — sorting, filtering, paging, virtualization, selection.
These are expressed through a `DataSource<T>` abstraction that decouples the grid from
where data lives.

---

## 2. Existing Foundation

### 2.1 PropertyGrid Metadata Model

The current PropertyGrid has a clean metadata-driven architecture:

**PropertyDescriptor** (`Reactor/PropertyGrid/PropertyDescriptor.cs`) — describes a single
property with name, display name, type, getter/setter, category, description, order,
and read-only flag.

**TypeMetadata** (`Reactor/PropertyGrid/TypeMetadata.cs`) — describes how to edit values
of a given type:
- `Editor`: `Func<object, Action<object>, Element>` — creates an editor element
- `Decompose`: `Func<object, IReadOnlyList<PropertyDescriptor>>` — breaks into sub-properties
- `Compose`: `Func<object, IReadOnlyDictionary<string, object>, object>` — reconstructs
  immutable types from decomposed parts

**TypeRegistry** (`Reactor/PropertyGrid/TypeRegistry.cs`) — maps CLR types to `TypeMetadata`,
with fallback chain: exact match → enum → primitive → array/IList → reflection.

**ReflectionTypeMetadataProvider** — auto-generates metadata from CLR type reflection,
respecting `[PropertyCategory]`, `[PropertyDescription]`, `[PropertyDisplayName]`,
`[PropertyHidden]`, `[PropertyReadOnly]`, `[PropertyOrder]` attributes plus
`System.ComponentModel` fallbacks.

### 2.2 Validation System

The validation system (`Reactor/Validation/`) provides:

- **ValidationContext** — thread-safe message store with field registration, touched/dirty
  tracking, initial/current value comparison, reset
- **IValidator / IAsyncValidator** — synchronous and async validation interfaces
- **Built-in validators** — Required, MinLength, MaxLength, Range, Match, Email, Url,
  Must, MustAsync, MustBeTrue, EqualTo
- **ValidateExtensions** — `.Validate()` fluent extension that attaches validators to
  elements via `ValidationAttached`
- **ValidationVisualizer** — error bubbling with Inline/Summary/InfoBar/Custom styles
- **FormField** — wraps a control with label, required indicator, description/error area

### 2.3 Key Insight: What's Reusable

The `Editor` function signature `Func<object, Action<object>, Element>` is already
generic enough for all three contexts:
- **FormField**: one editor for one field
- **PropertyGrid**: editors resolved per-property via TypeRegistry
- **DataGrid**: same editor used for inline cell editing

The `PropertyDescriptor` is close to what we need for column definitions — it has name,
display name, type, getter/setter. It needs extension for grid-specific concerns (width,
sortability, filterability) but the core identity is shared.

---

## 3. Data Access Layer

### 3.1 Design Principles

Research across Relay, Apollo, TanStack, MS-Graph, Salesforce, GraphQL, and .NET
precedent reveals consistent patterns:

1. **Rows always identified by key** — not index. Every framework (Relay's `id`,
   MS-Graph's GUID, Salesforce's 18-char Id, TanStack Table's `getRowId`) uses stable
   keys. Indices shift under sort/filter/insert/delete; keys don't.

2. **Cursor-based pagination as primary model** — cursor tokens (opaque, stable under
   mutations, sequential) with offset as a degenerate case. Matches Relay connections,
   OData `@odata.nextLink`, Salesforce `nextRecordsUrl`, Azure SDK continuation tokens.

3. **Sort/filter as descriptor objects, not lambdas** — so they can be serialized to
   server queries, displayed in UI (sort arrows, filter chips), persisted as user
   preferences. `IQueryable`'s expression trees are the most powerful version, but
   descriptors are simpler and sufficient for grid UX.

4. **Capability-based push-down** — the data source declares what it can handle
   server-side (like `IBindingList.SupportsSorting`). The grid can then decide whether
   to sort client-side or push to the server.

5. **Pages, not streams** — grids need random access within a page, total counts, and
   has-more signals. `IAsyncEnumerable<T>` is great for sequential consumption but
   insufficient for grid UX. Azure SDK's `AsyncPageable<T>` (dual-level: item iteration
   + page iteration) is the closest .NET precedent.

### 3.2 Core Interfaces

```csharp
namespace Microsoft.UI.Reactor.Data;

/// <summary>
/// A unique, stable identifier for a row. Can wrap a string, int, Guid, or
/// composite key. Rows MUST be identifiable by key — the data system never
/// uses positional indices as identity.
/// </summary>
public readonly record struct RowKey(string Value)
{
    public static implicit operator RowKey(string s) => new(s);
    public static implicit operator RowKey(int i) => new(i.ToString());
    public static implicit operator RowKey(Guid g) => new(g.ToString());
    public override string ToString() => Value;
}

/// <summary>
/// A page of data returned by a data source.
/// </summary>
public record DataPage<T>
{
    /// <summary>The items in this page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// Opaque continuation token for fetching the next page.
    /// Null means this is the last page.
    /// </summary>
    public string? ContinuationToken { get; init; }

    /// <summary>
    /// Total number of rows across all pages, if known.
    /// Null when the source cannot provide a count efficiently.
    /// Used for scroll thumb sizing and "showing 1-20 of N" UI.
    /// </summary>
    public int? TotalCount { get; init; }
}

/// <summary>
/// Describes a sort operation. Serializable to OData, SOQL, SQL, GraphQL, etc.
/// </summary>
public record SortDescriptor(string Field, SortDirection Direction = SortDirection.Ascending);

public enum SortDirection { Ascending, Descending }

/// <summary>
/// Describes a filter operation. Serializable to query languages.
/// </summary>
public record FilterDescriptor(
    string Field,
    FilterOperator Operator,
    object? Value)
{
    /// <summary>Optional second value for Between operations.</summary>
    public object? ValueTo { get; init; }
}

public enum FilterOperator
{
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    EndsWith,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Between,
    In,
    IsNull,
    IsNotNull,
}

/// <summary>
/// A request for a page of data. Captures sort, filter, and pagination state
/// as serializable descriptors.
/// </summary>
public record DataRequest
{
    /// <summary>Maximum items to return in this page.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>
    /// Continuation token from a previous DataPage. Null for the first page.
    /// </summary>
    public string? ContinuationToken { get; init; }

    /// <summary>Active sort descriptors (ordered by priority).</summary>
    public IReadOnlyList<SortDescriptor> Sort { get; init; } = [];

    /// <summary>Active filter descriptors (ANDed together).</summary>
    public IReadOnlyList<FilterDescriptor> Filters { get; init; } = [];

    /// <summary>
    /// Optional text search query (for full-text / fuzzy search).
    /// Separate from column filters because many APIs expose search
    /// as a distinct parameter ($search in OData, SOSL in Salesforce).
    /// </summary>
    public string? SearchQuery { get; init; }

    /// <summary>
    /// Optional column projection — which fields to load.
    /// Null means load all. Used by OData ($select) and GraphQL.
    /// </summary>
    public IReadOnlyList<string>? Select { get; init; }
}
```

### 3.3 The DataSource Interface

```csharp
/// <summary>
/// Async, paginated data source. The fundamental abstraction that decouples
/// data consumers (DataGrid, PropertyGrid, FormField) from data producers
/// (in-memory list, MS-Graph, SQL, GraphQL, etc.).
///
/// DataSource is the "IQueryable for UI" — it accepts sort/filter descriptors
/// and returns pages of keyed data.
/// </summary>
public interface IDataSource<T>
{
    /// <summary>
    /// Fetches a page of data matching the given request.
    /// </summary>
    Task<DataPage<T>> GetPageAsync(DataRequest request, CancellationToken ct = default);

    /// <summary>
    /// Extracts the unique key from a row. Every row MUST have a stable identity.
    /// </summary>
    RowKey GetRowKey(T item);

    /// <summary>
    /// Declares what this data source supports. The grid uses this to decide
    /// whether to handle operations client-side or push them to the source.
    /// </summary>
    DataSourceCapabilities Capabilities { get; }
}

/// <summary>
/// Capability flags that a data source declares. The grid queries these
/// to decide whether to perform operations client-side or delegate.
/// Follows the IBindingList.SupportsSorting pattern from System.ComponentModel.
/// </summary>
[Flags]
public enum DataSourceCapabilities
{
    None            = 0,
    ServerSort      = 1 << 0,   // Source handles sorting
    ServerFilter    = 1 << 1,   // Source handles filtering
    ServerSearch    = 1 << 2,   // Source handles text search
    ServerCount     = 1 << 3,   // Source can return total count efficiently
    ServerSelect    = 1 << 4,   // Source supports column projection
    Mutate          = 1 << 5,   // Source supports create/update/delete
    Refresh         = 1 << 6,   // Source supports change notification / delta
}
```

### 3.4 Mutation Interface (Optional Extension)

```csharp
/// <summary>
/// Extended data source that supports mutations. Not all data sources are
/// mutable — read-only sources just implement IDataSource&lt;T&gt;.
/// </summary>
public interface IMutableDataSource<T> : IDataSource<T>
{
    /// <summary>Creates a new row. Returns the created item with server-assigned fields.</summary>
    Task<T> CreateAsync(T item, CancellationToken ct = default);

    /// <summary>Updates an existing row identified by key.</summary>
    Task<T> UpdateAsync(RowKey key, T item, CancellationToken ct = default);

    /// <summary>Deletes a row by key.</summary>
    Task DeleteAsync(RowKey key, CancellationToken ct = default);
}
```

### 3.5 Observable/Refreshable Interface (Optional Extension)

```csharp
/// <summary>
/// Data source that can notify consumers when data has changed externally.
/// Used for live data scenarios (real-time dashboards, collaborative editing).
/// Maps to OData delta queries, GraphQL subscriptions, SignalR, etc.
/// </summary>
public interface IObservableDataSource<T> : IDataSource<T>
{
    /// <summary>
    /// Fires when the data source detects that cached data may be stale.
    /// The consumer should re-fetch visible pages.
    /// </summary>
    event Action? DataChanged;
}
```

### 3.6 Row-by-Key Access

```csharp
/// <summary>
/// Data source that supports fetching individual rows by key.
/// Useful for detail views, expand-on-click, and edit-then-refresh patterns.
/// Not all sources support this (some only support paged access).
/// </summary>
public interface IKeyedDataSource<T> : IDataSource<T>
{
    /// <summary>Fetches a single row by key.</summary>
    Task<T?> GetByKeyAsync(RowKey key, CancellationToken ct = default);

    /// <summary>Fetches multiple rows by key (batch).</summary>
    Task<IReadOnlyList<T>> GetByKeysAsync(
        IReadOnlyList<RowKey> keys, CancellationToken ct = default);
}
```

### 3.7 Why Not IQueryable?

`IQueryable<T>` is the most powerful server push-down abstraction in .NET — it captures
arbitrary LINQ expressions as trees and translates them via `IQueryProvider`. But it is
a poor fit for a UI grid data source because:

1. **No pagination metadata.** `IQueryable` yields `IEnumerable` or `Task<List<T>>` —
   no `hasNextPage`, no `totalCount`, no continuation token.
2. **No capability discovery.** You cannot ask "does this provider support OrderBy on
   field X?" — you call it and get a runtime exception if unsupported.
3. **Lambda-based filters cannot be serialized** to URLs, stored as user preferences,
   or displayed in filter chips.
4. **No key concept.** `IQueryable` is index-agnostic — there is no `GetRowId`.
5. **No mutation contract.** `IQueryable` is read-only.

However, `IQueryable<T>` is excellent as an *implementation detail* inside a provider.
The `SqlDataSource<T>` provider can use EF Core's `IQueryable` internally while exposing
`IDataSource<T>` to the grid.

### 3.8 Why Not IAsyncEnumerable?

`IAsyncEnumerable<T>` is the natural .NET async iteration primitive, but:

1. **No random access within a page.** Grids need `items[i]`, not sequential enumeration.
2. **No metadata.** No total count, no has-more flag, no continuation token.
3. **No restart/re-fetch.** Once consumed, you need a new enumerator. Grids re-fetch
   when sort/filter changes.

Like `IQueryable`, `IAsyncEnumerable` is useful *inside* providers (e.g., streaming
database results) but is not the right surface for the grid.

### 3.9 Comparison with Existing .NET Patterns

| Pattern | Async | Paginated | Key-based | Sort/Filter Push-down | Mutations | UI-Ready |
|---------|-------|-----------|-----------|----------------------|-----------|----------|
| `IAsyncEnumerable<T>` | Yes | No | No | No | No | No |
| `IQueryable<T>` | Via EF | Partial (Skip/Take) | No | Yes (expressions) | No | No |
| `IBindingList` | No | No | No | Client-side | Yes | Partial |
| `ICollectionView` | No | No | No | Client-side | No | Partial |
| `IItemsRangeInfo` | Partial | Index-range | No (index-based) | No | No | Yes |
| `AsyncPageable<T>` (Azure SDK) | Yes | Yes (continuation) | No | No | No | Partial |
| **`IDataSource<T>` (proposed)** | **Yes** | **Yes (cursor)** | **Yes** | **Yes (descriptors)** | **Optional** | **Yes** |

---

## 4. Data Providers

Each provider implements `IDataSource<T>` and translates `DataRequest` into the
appropriate query for its backend.

### 4.1 In-Memory List Provider

The simplest and most common provider. Wraps `IList<T>`, `ObservableCollection<T>`,
or any `IEnumerable<T>`.

```csharp
public class ListDataSource<T> : IDataSource<T>, IMutableDataSource<T>
{
    private readonly IList<T> _items;
    private readonly Func<T, RowKey> _getKey;

    public ListDataSource(IList<T> items, Func<T, RowKey> getKey)
    {
        _items = items;
        _getKey = getKey;
    }

    public DataSourceCapabilities Capabilities =>
        DataSourceCapabilities.ServerSort
        | DataSourceCapabilities.ServerFilter
        | DataSourceCapabilities.ServerSearch
        | DataSourceCapabilities.ServerCount
        | DataSourceCapabilities.Mutate;

    public RowKey GetRowKey(T item) => _getKey(item);

    public Task<DataPage<T>> GetPageAsync(DataRequest request, CancellationToken ct)
    {
        IEnumerable<T> query = _items;

        // Apply filters client-side (we claim ServerFilter capability)
        foreach (var filter in request.Filters)
            query = ApplyFilter(query, filter);

        // Apply sort client-side
        query = ApplySort(query, request.Sort);

        var total = query.Count();
        var offset = ParseOffset(request.ContinuationToken);
        var items = query.Skip(offset).Take(request.PageSize).ToList();

        return Task.FromResult(new DataPage<T>
        {
            Items = items,
            TotalCount = total,
            ContinuationToken = offset + items.Count < total
                ? (offset + items.Count).ToString()
                : null,
        });
    }

    // ... mutation methods, filter/sort helpers
}
```

**Observable variant:** `ObservableListDataSource<T>` subscribes to
`ObservableCollection<T>.CollectionChanged` and implements `IObservableDataSource<T>`,
firing `DataChanged` when items are added/removed externally.

### 4.2 MS-Graph Provider

Translates `DataRequest` to OData query parameters and follows `@odata.nextLink` for
pagination.

```csharp
public class GraphDataSource<T> : IDataSource<T>
{
    private readonly GraphServiceClient _client;
    private readonly string _entityPath;       // e.g., "/users"
    private readonly Func<T, RowKey> _getKey;

    public DataSourceCapabilities Capabilities =>
        DataSourceCapabilities.ServerSort
        | DataSourceCapabilities.ServerFilter
        | DataSourceCapabilities.ServerSearch
        | DataSourceCapabilities.ServerCount
        | DataSourceCapabilities.ServerSelect;

    public async Task<DataPage<T>> GetPageAsync(DataRequest request, CancellationToken ct)
    {
        // If we have a continuation token, it's a full nextLink URL
        if (request.ContinuationToken is { } nextLink)
            return await FetchNextLink(nextLink, ct);

        // Build OData query
        var query = new QueryString()
            .With("$top", request.PageSize)
            .With("$orderby", ToODataSort(request.Sort))
            .With("$filter", ToODataFilter(request.Filters))
            .With("$search", request.SearchQuery)
            .With("$select", request.Select)
            .With("$count", "true");

        var response = await _client.GetAsync<ODataPage<T>>(
            _entityPath + query, ct);

        return new DataPage<T>
        {
            Items = response.Value,
            TotalCount = response.Count,
            ContinuationToken = response.NextLink,
        };
    }
}
```

**Key translation points:**
- `SortDescriptor` → `$orderby=displayName asc,mail desc`
- `FilterDescriptor(Equals)` → `$filter=department eq 'Engineering'`
- `FilterDescriptor(Contains)` → `$filter=contains(displayName,'smith')`
- `FilterDescriptor(StartsWith)` → `$filter=startsWith(mail,'alice')`
- `ContinuationToken` → `@odata.nextLink` URL (opaque, includes `$skiptoken`)

### 4.3 SQL / Entity Framework Provider

Translates `DataRequest` to EF Core LINQ, leveraging `IQueryable<T>` internally.

```csharp
public class EfDataSource<T> : IDataSource<T>, IMutableDataSource<T> where T : class
{
    private readonly DbContext _db;
    private readonly Func<T, RowKey> _getKey;

    public DataSourceCapabilities Capabilities =>
        DataSourceCapabilities.ServerSort
        | DataSourceCapabilities.ServerFilter
        | DataSourceCapabilities.ServerCount
        | DataSourceCapabilities.Mutate;

    public async Task<DataPage<T>> GetPageAsync(DataRequest request, CancellationToken ct)
    {
        IQueryable<T> query = _db.Set<T>();

        // Translate filter descriptors to LINQ Where clauses
        foreach (var filter in request.Filters)
            query = ApplyFilter(query, filter);  // builds Expression<Func<T, bool>>

        // Translate sort descriptors to LINQ OrderBy
        query = ApplySort(query, request.Sort);   // builds OrderBy/ThenBy chain

        var total = await query.CountAsync(ct);
        var offset = ParseOffset(request.ContinuationToken);
        var items = await query.Skip(offset).Take(request.PageSize).ToListAsync(ct);

        return new DataPage<T>
        {
            Items = items,
            TotalCount = total,
            ContinuationToken = offset + items.Count < total
                ? (offset + items.Count).ToString()
                : null,
        };
    }
}
```

The `ApplyFilter` and `ApplySort` methods use `System.Linq.Expressions` to build
expression trees from `FilterDescriptor` and `SortDescriptor` — EF Core then translates
these to SQL. This is where `IQueryable`'s power is leveraged internally without
exposing its complexity to the grid consumer.

### 4.4 GraphQL Provider

Translates `DataRequest` to a GraphQL query with Relay-style connection pagination.

```csharp
public class GraphQLDataSource<T> : IDataSource<T>
{
    private readonly HttpClient _http;
    private readonly string _connectionField;   // e.g., "users"
    private readonly string _nodeFragment;      // fields to select
    private readonly Func<T, RowKey> _getKey;

    public async Task<DataPage<T>> GetPageAsync(DataRequest request, CancellationToken ct)
    {
        var query = $$"""
            query($first: Int, $after: String, $orderBy: {{_connectionField}}Order, $filter: {{_connectionField}}Filter) {
              {{_connectionField}}(first: $first, after: $after, orderBy: $orderBy, filter: $filter) {
                edges {
                  node { {{_nodeFragment}} }
                  cursor
                }
                pageInfo { hasNextPage endCursor }
                totalCount
              }
            }
            """;

        var variables = new {
            first = request.PageSize,
            after = request.ContinuationToken,
            orderBy = ToGraphQLSort(request.Sort),
            filter = ToGraphQLFilter(request.Filters),
        };

        var response = await ExecuteQuery<ConnectionResponse<T>>(query, variables, ct);
        var connection = response.Data;

        return new DataPage<T>
        {
            Items = connection.Edges.Select(e => e.Node).ToList(),
            TotalCount = connection.TotalCount,
            ContinuationToken = connection.PageInfo.HasNextPage
                ? connection.PageInfo.EndCursor
                : null,
        };
    }
}
```

### 4.5 Azure Table / CosmosDB Provider

```csharp
public class CosmosDataSource<T> : IDataSource<T>, IMutableDataSource<T>
{
    private readonly Container _container;

    public async Task<DataPage<T>> GetPageAsync(DataRequest request, CancellationToken ct)
    {
        var sql = BuildCosmosSql(request);  // SELECT ... FROM c WHERE ... ORDER BY ... OFFSET ... LIMIT ...
        var queryDef = new QueryDefinition(sql);

        using var iterator = _container.GetItemQueryIterator<T>(
            queryDef,
            continuationToken: request.ContinuationToken,
            requestOptions: new() { MaxItemCount = request.PageSize });

        var response = await iterator.ReadNextAsync(ct);

        return new DataPage<T>
        {
            Items = response.Resource.ToList(),
            ContinuationToken = response.ContinuationToken,
            // CosmosDB does not return total count in queries
        };
    }
}
```

### 4.6 Provider Summary

| Provider | Sort | Filter | Search | Count | Mutate | Observe | Key Source |
|----------|------|--------|--------|-------|--------|---------|-----------|
| `ListDataSource<T>` | Client | Client | Client | Yes | Yes | No | User-supplied `Func<T, RowKey>` |
| `ObservableListDataSource<T>` | Client | Client | Client | Yes | Yes | Yes | User-supplied |
| `GraphDataSource<T>` | Server | Server | Server | Yes | Partial | Delta queries | Entity `id` |
| `EfDataSource<T>` | Server | Server | Partial | Yes | Yes | No | User-supplied |
| `GraphQLDataSource<T>` | Server | Server | Depends | Depends | Depends | Subscriptions | Node `id` |
| `CosmosDataSource<T>` | Server | Server | No | No | Yes | Change feed | Partition key + id |

---

## 5. Shared Metadata Model — FieldDescriptor

### 5.1 The Problem

Currently, `PropertyDescriptor` serves only the PropertyGrid. It has the right shape
for describing a field (name, type, getter/setter, metadata), but it is bound to the
PropertyGrid namespace and lacks grid-specific concerns (width, sortability, filter type).

For the unified family, we need a **shared metadata type** that works for all three
contexts. Each context then adds its own specifics via extension or specialization.

### 5.2 Proposed: FieldDescriptor

```csharp
namespace Microsoft.UI.Reactor.Data;

/// <summary>
/// Describes a single data field across the editing family.
/// Used by FormField, PropertyGrid, and DataGrid.
/// This is the successor to PropertyGrid.PropertyDescriptor, generalized
/// for all contexts.
/// </summary>
public record FieldDescriptor
{
    // ── Identity ──────────────────────────────────────────────────

    /// <summary>Field name (used as key, matches property name or column key).</summary>
    public required string Name { get; init; }

    /// <summary>Display label shown in UI.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The CLR type of this field's value.</summary>
    public required Type FieldType { get; init; }

    // ── Access ────────────────────────────────────────────────────

    /// <summary>
    /// Gets the current value of this field from the owner object.
    /// Takes the owner as a parameter (not a closure) so the descriptor
    /// can be reused across multiple owner instances (e.g., across rows
    /// in a DataGrid).
    /// </summary>
    public required Func<object, object?> GetValue { get; init; }

    /// <summary>
    /// Sets this field's value on the owner and returns the (possibly new) owner.
    /// 
    /// Return-new-owner pattern (Decision D9):
    /// - Mutable objects: mutates in place, returns the same reference.
    /// - Immutable records: returns a new object via constructor invocation
    ///   (equivalent to a `with` expression). The original is unchanged.
    /// 
    /// Null when the field is truly read-only (no setter, no reconstruct).
    /// </summary>
    public Func<object, object?, object>? SetValue { get; init; }

    /// <summary>Whether this field is read-only.</summary>
    public bool IsReadOnly { get; init; }

    // ── Metadata ──────────────────────────────────────────────────

    /// <summary>Category for grouping (PropertyGrid categories, DataGrid column groups).</summary>
    public string? Category { get; init; }

    /// <summary>Help text shown as tooltip.</summary>
    public string? Description { get; init; }

    /// <summary>Declaration order for stable sorting.</summary>
    public int Order { get; init; }

    // ── Editing ───────────────────────────────────────────────────

    /// <summary>
    /// Editor override for this specific field. When null, the TypeRegistry
    /// resolves an editor by FieldType.
    /// </summary>
    public Func<object, Action<object>, Element>? Editor { get; init; }

    // ── Validation ────────────────────────────────────────────────

    /// <summary>Validators for this field.</summary>
    public IReadOnlyList<IValidator>? Validators { get; init; }

    /// <summary>Async validators for this field.</summary>
    public IReadOnlyList<IAsyncValidator>? AsyncValidators { get; init; }

    // ── Grid-Specific (ignored by PropertyGrid/FormField) ─────────

    /// <summary>Default width in pixels. Null = auto.</summary>
    public double? Width { get; init; }

    /// <summary>Minimum width in pixels.</summary>
    public double? MinWidth { get; init; }

    /// <summary>Maximum width in pixels.</summary>
    public double? MaxWidth { get; init; }

    /// <summary>Flex grow factor for auto-sizing. Null = no flex.</summary>
    public double? Flex { get; init; }

    /// <summary>Whether this column is sortable in a grid context.</summary>
    public bool Sortable { get; init; } = true;

    /// <summary>Whether this column is filterable in a grid context.</summary>
    public bool Filterable { get; init; } = true;

    /// <summary>Pin position for fixed columns.</summary>
    public PinPosition Pin { get; init; } = PinPosition.None;

    /// <summary>
    /// Custom cell renderer for grid display (read-only view).
    /// When null, uses a default text renderer based on FieldType.
    /// Different from Editor: this is the passive display, not the edit control.
    /// </summary>
    public Func<object, Element>? CellRenderer { get; init; }

    /// <summary>
    /// Custom value formatter for display text (grid cells, summary rows).
    /// </summary>
    public Func<object?, string>? FormatValue { get; init; }
}

public enum PinPosition { None, Left, Right }
```

### 5.3 Migration from PropertyDescriptor (Decision D1)

`PropertyDescriptor` is **replaced** by `FieldDescriptor` as part of this work.
No coexistence, no bridge type — one type for all three contexts.

**What changes in PropertyGrid:**
- `ReflectionTypeMetadataProvider.CreateMetadata()` returns `FieldDescriptor` lists
- `TypeMetadata.Decompose` signature becomes `Func<object, IReadOnlyList<FieldDescriptor>>`
- `PropertyGridComponent.RenderProperty()` accepts `FieldDescriptor`
- Template delegates (`PropertyRowTemplate`, etc.) accept `FieldDescriptor`
- `EditChain` works with `FieldDescriptor`
- All `[Property*]` attributes remain (they're read by the reflection provider)

**What stays the same:**
- `TypeMetadata` (Editor, Decompose) — unchanged
- `TypeMetadata.Compose` — **kept as optimization** for batch multi-field updates,
  but no longer the primary mutation mechanism (see 5.4)
- `TypeRegistry` — extended, not replaced
- `ArrayTypeMetadata` — unchanged
- The reflection provider logic — same attribute reading, output type changes

### 5.4 Immutable Editing Model (Decision D9)

The PropertyGrid already supports editing immutable records via `EditChain` +
`TypeMetadata.Compose`. This design generalizes that pattern across all three
components via the **return-new-owner** `SetValue` signature.

#### The Core Principle

`FieldDescriptor.SetValue` has signature `Func<object, object?, object>?`:
- Input: `(owner, newFieldValue)`
- Output: the (possibly new) owner with the field updated

For **mutable** objects (class with public setter):
```csharp
// Generated by reflection provider:
SetValue = (owner, val) =>
{
    property.SetValue(owner, val);  // mutates in place
    return owner;                   // returns same reference
}
```

For **immutable** records (init-only properties):
```csharp
// Generated by reflection provider (uses constructor invocation):
SetValue = (owner, val) =>
{
    // Reconstruct: copy all properties, replace this one
    var args = new object?[ctorParams.Length];
    for (int i = 0; i < ctorParams.Length; i++)
    {
        var paramName = ctorParams[i].Name;
        args[i] = paramName == propertyName
            ? val
            : properties[paramName].GetValue(owner);
    }
    return ctor.Invoke(args);  // returns NEW object
}
```

This is the same constructor-invocation logic as `BuildCompose` in the current
`ReflectionTypeMetadataProvider`, but packaged per-field instead of per-type.

#### Deep Nesting: EditChain Simplification

The EditChain still exists for nested immutable editing (e.g., `contact.Address.City`),
but it simplifies — it just calls `SetValue` at each level:

```csharp
// Editing contact.Address.City where Contact and Address are both records
// EditChain path: [(addressField, currentContact)]

// Step 1: newAddress = cityField.SetValue(currentAddress, "Portland")
//         → new Address { City = "Portland", State = "CA" }
//
// Step 2: newContact = addressField.SetValue(currentContact, newAddress)
//         → new Contact { Name = "Alice", Address = newAddress }
//
// Step 3: onRootChanged(newContact)
```

**Early termination for mutable paths:** If `SetValue` returns the same reference
(detected via `ReferenceEquals`), the mutation happened in place and propagation stops:

```csharp
public void PropagateEdit(FieldDescriptor leafField, object leafOwner, object newValue)
{
    object currentChild = leafField.SetValue!(leafOwner, newValue);

    // Walk upward through the nesting path
    for (int i = _path.Count - 1; i >= 0; i--)
    {
        var (field, currentOwner) = _path[i];
        var newOwner = field.SetValue!(currentOwner, currentChild);

        if (ReferenceEquals(newOwner, currentOwner))
            return;  // mutable path — mutation happened in place, stop

        currentChild = newOwner;
    }

    // Reached the root — deliver the new root object
    _onRootChanged?.Invoke(currentChild);
}
```

This recovers the current PropertyGrid behavior (stop at the first mutable ancestor)
but through a single unified code path instead of separate mutable/immutable branches.

#### DataGrid: Immutable Record Editing

**Cell-level editing** of `contacts[3].Email`:
```
1. User edits cell → newValue = "new@example.com"
2. newContact = emailField.SetValue(contacts[3], "new@example.com")
   → For mutable: mutates contact.Email in place, returns same Contact
   → For immutable: returns new Contact { Email = "new@example.com", ... }
3. OnRowChanged(rowKey, newContact) fires
4. Data source or parent replaces the row in the collection
```

**Row-level editing** (multiple cells, then commit):
```
1. User edits Name, Email, Phone in row edit mode
2. Grid accumulates pending values: { "Name": "Alice", "Email": "new@x.com", "Phone": "555" }
3. On commit, chains SetValue calls:
   var newRow = originalRow;
   foreach (var (field, value) in pendingEdits)
       newRow = field.SetValue(newRow, value);
   // For records: each call returns a new object (N small allocations — fine)
   // For mutable: each call mutates in place and returns same reference
4. OnRowChanged(rowKey, newRow)
```

**FormField** with immutable state:
```csharp
var (contact, setContact) = UseState(new Contact());

FormField(emailField, contact, newContact => setContact((Contact)newContact))
// Internally:
// newContact = emailField.SetValue(contact, "new@x.com")
// → new Contact with Email changed
// → setContact(newContact) updates state → re-render
```

#### Why Compose Still Exists (But Is Secondary)

`TypeMetadata.Compose` remains as an **optimization for batch multi-field updates**.
When multiple fields change at once (row-level edit commit, drag-and-drop of multiple
values), Compose creates one new object instead of N intermediate objects:

```csharp
// N intermediate objects (via chained SetValue):
var row = original;
row = nameField.SetValue(row, "Alice");    // intermediate 1
row = emailField.SetValue(row, "a@x.com"); // intermediate 2
row = phoneField.SetValue(row, "555");     // intermediate 3 (final)

// 1 object (via Compose):
var row = meta.Compose(original, new Dictionary<string, object>
{
    ["Name"] = "Alice", ["Email"] = "a@x.com", ["Phone"] = "555"
});
```

For small records (3-10 fields), the difference is negligible. But Compose is available
for performance-sensitive paths. The reflection provider still generates it.

### 5.5 Attribute-Driven Metadata for DataGrid Columns

The existing `[PropertyCategory]`, `[PropertyDescription]`, etc. attributes work for
shared metadata. For grid-specific concerns, new attributes:

```csharp
[AttributeUsage(AttributeTargets.Property)]
public class ColumnWidthAttribute(double width) : Attribute
{
    public double Width { get; } = width;
    public double? MinWidth { get; init; }
    public double? MaxWidth { get; init; }
}

[AttributeUsage(AttributeTargets.Property)]
public class ColumnPinAttribute(PinPosition position) : Attribute
{
    public PinPosition Position { get; } = position;
}

[AttributeUsage(AttributeTargets.Property)]
public class NotSortableAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class NotFilterableAttribute : Attribute { }
```

The reflection-based metadata provider reads these alongside the existing property
attributes to produce `FieldDescriptor` instances.

### 5.6 TypeRegistry Evolution

The `TypeRegistry` already maps types to `TypeMetadata` (editor, decompose, compose).
It extends naturally:

```csharp
// Existing - unchanged
registry.Register<RgbColor>(new TypeMetadata
{
    Editor = (val, onChange) => ...,
    Decompose = val => ...,
    Compose = (val, updates) => ...,
});

// New - grid cell renderer registration
registry.RegisterCellRenderer<RgbColor>((val) =>
    Border(null).Width(20).Height(20).Background(ToColor(val)));

// New - value formatter registration
registry.RegisterFormatter<RgbColor>(val => $"#{val.R:X2}{val.G:X2}{val.B:X2}");
```

The TypeRegistry resolves renderers/editors/formatters with the same fallback chain:
field-level override → type-level registration → built-in default.

### 5.7 Editor Tiers: Compact, Standard, and Full

Different contexts have different space budgets for editing a value. A color in a
grid cell needs a tiny swatch; in a PropertyGrid it gets a hex text field; in a
dialog it gets a full HSL/RGB picker. Rather than forcing one editor to adapt to
all sizes, `TypeMetadata` supports **three optional editor tiers**:

```csharp
public record TypeMetadata
{
    /// <summary>
    /// Standard editor — the default for PropertyGrid and FormField.
    /// Falls back to built-in primitive editors if null.
    /// </summary>
    public Func<object, Action<object>, Element>? Editor { get; init; }

    /// <summary>
    /// Compact editor — used in DataGrid cells where space is constrained.
    /// Falls back to Editor if null.
    /// </summary>
    public Func<object, Action<object>, Element>? CompactEditor { get; init; }

    /// <summary>
    /// Full editor — opened via a "..." affordance in any context.
    /// When registered, the consuming component shows a "..." button next to
    /// the inline editor that opens the full editor in a Flyout or Dialog.
    /// Null means no expanded editing available.
    /// </summary>
    public Func<object, Action<object>, Element>? FullEditor { get; init; }

    // ... Decompose, Compose unchanged
}
```

**Resolution by context:**

| Context | Resolves | Fallback |
|---------|----------|----------|
| DataGrid cell editing | `CompactEditor` | `Editor` → built-in |
| PropertyGrid inline | `Editor` | built-in |
| FormField | `Editor` | built-in |
| "..." button / expand | `FullEditor` | (button hidden if null) |

**When tiers matter (custom types):**

```csharp
registry.Register<RgbColor>(new TypeMetadata
{
    // Grid cell: small color swatch that opens a picker on click
    CompactEditor = (val, onChange) =>
        ColorSwatch((RgbColor)val, color => onChange(color)),

    // PropertyGrid / FormField: hex text field
    Editor = (val, onChange) =>
    {
        var c = (RgbColor)val;
        return TextField(c.ToString(), s => onChange(RgbColor.Parse(s)));
    },

    // Dialog: full HSL/RGB picker with preview
    FullEditor = (val, onChange) =>
        ColorPickerFull((RgbColor)val, color => onChange(color)),

    // PropertyGrid expand: R/G/B sub-properties (existing pattern)
    Decompose = val => ...,
    Compose = (val, updates) => ...,
});
```

**When tiers don't matter (built-in primitives):**

For `string`, `int`, `bool`, `enum` — the editor is the same at every size (TextField,
NumberBox, ToggleSwitch, ComboBox). Only `Editor` is set. `CompactEditor` and
`FullEditor` are null, and the fallback produces the right behavior.

**The "..." affordance:**

When a type has `FullEditor` registered, the consuming component automatically shows
a small "..." button next to the inline editor. Clicking it opens the `FullEditor` in
a `Flyout` anchored to the cell/field. This is a common pattern in property editors
(Visual Studio Properties window, Unity Inspector, Blend).

The "..." button is purely automatic — it appears when `FullEditor` is registered and
disappears when it's not. No configuration needed from the consumer.

**Field-level override:**

`FieldDescriptor.Editor` still overrides everything for a specific field. It doesn't
participate in the tier system — it's a "I know exactly what I want here" escape hatch.
If you need a specific compact editor for one grid column, set `Editor` on that
column's `FieldDescriptor`.

---

## 6. The DataGrid Component

### 6.1 Architecture: Headless Core + Rendered Shell

Following the TanStack Table pattern (strongest architecture across all researched
frameworks), the DataGrid separates into:

1. **DataGridState** — headless state machine (sort, filter, selection, editing, column
   order/sizing/visibility, scroll position). Pure logic, fully testable without UI.
2. **DataGridComponent** — Microsoft.UI.Reactor (Reactor) component that renders the grid using VirtualList
   composition, consuming DataGridState for all logic.

### 6.2 DataGrid DSL

```csharp
// Simple: columns from FieldDescriptor list
DataGrid(
    source: new ListDataSource<Product>(products, p => p.Id.ToString()),
    columns: [
        Column<Product>("Name", p => p.Name, editable: true)
            .Validate(Required()),
        Column<Product>("Price", p => p.Price, editable: true, format: "C2")
            .Validate(Range(0.01, 99999)),
        Column<Product>("Category", p => p.Category),
        Column<Product>("InStock", p => p.InStock),
    ],
    onRowChanged: async (key, product) => await SaveProduct(product),
    selectionMode: SelectionMode.Multiple
)

// Reflection: auto-generate columns from type
DataGrid(
    source: graphDataSource,
    registry: registry,   // same TypeRegistry as PropertyGrid
    selectionMode: SelectionMode.Single
)

// Hybrid: reflection + overrides
DataGrid(
    source: efDataSource,
    registry: registry,
    columnOverrides: col => col.Name switch
    {
        "Id" => col with { Pin = PinPosition.Left, Width = 80, IsReadOnly = true },
        "Email" => col with { Validators = [Validate.Required(), Validate.Email()] },
        _ => col,
    }
)
```

### 6.3 DataGrid Element

```csharp
public record DataGridElement<T> : Element
{
    /// <summary>The data source providing rows.</summary>
    public required IDataSource<T> Source { get; init; }

    /// <summary>Column definitions. If null, auto-generated from TypeRegistry + reflection.</summary>
    public IReadOnlyList<FieldDescriptor>? Columns { get; init; }

    /// <summary>Type registry for editor/renderer resolution. If null, uses built-in defaults.</summary>
    public TypeRegistry? Registry { get; init; }

    /// <summary>Column override function for reflection-generated columns.</summary>
    public Func<FieldDescriptor, FieldDescriptor>? ColumnOverrides { get; init; }

    /// <summary>Selection mode.</summary>
    public SelectionMode SelectionMode { get; init; } = SelectionMode.None;

    /// <summary>Callback when selection changes.</summary>
    public Action<IReadOnlySet<RowKey>>? OnSelectionChanged { get; init; }

    /// <summary>Callback when a row is edited and committed.</summary>
    public Func<RowKey, T, Task>? OnRowChanged { get; init; }

    /// <summary>
    /// Fixed row height. When set, all rows have this exact height and the
    /// virtualizer uses O(1) offset calculation. When null, rows are measured
    /// and EstimatedRowHeight is used as the initial estimate (D4, D11).
    /// </summary>
    public double? RowHeight { get; init; } = 40;

    /// <summary>
    /// Estimated row height for variable-height mode (when RowHeight is null).
    /// Used for initial scroll thumb sizing before rows are measured.
    /// </summary>
    public double EstimatedRowHeight { get; init; } = 40;

    /// <summary>Editing mode: Cell (one cell at a time) or Row (whole row) (D10).</summary>
    public EditMode EditMode { get; init; } = EditMode.Cell;

    /// <summary>Whether to show column headers.</summary>
    public bool ShowHeaders { get; init; } = true;

    /// <summary>Whether to show the search/filter bar.</summary>
    public bool ShowFilterBar { get; init; }

    /// <summary>Whether rows are editable (enables inline editing).</summary>
    public bool Editable { get; init; }

    /// <summary>Whether columns can be reordered via drag.</summary>
    public bool AllowColumnReorder { get; init; } = true;

    /// <summary>Whether columns can be resized.</summary>
    public bool AllowColumnResize { get; init; } = true;

    // ── Template overrides ────────────────────────────────────────

    /// <summary>Custom cell template override.</summary>
    public Func<CellContext<T>, Element>? CellTemplate { get; init; }

    /// <summary>Custom row template override.</summary>
    public Func<RowContext<T>, Element>? RowTemplate { get; init; }

    /// <summary>Custom header template override.</summary>
    public Func<HeaderContext, Element>? HeaderTemplate { get; init; }

    /// <summary>Element to show when data is loading.</summary>
    public Element? LoadingTemplate { get; init; }

    /// <summary>Element to show when data is empty.</summary>
    public Element? EmptyTemplate { get; init; }
}

public enum SelectionMode { None, Single, Multiple }

public enum EditMode { Cell, Row }

public record CellContext<T>(
    T Row,
    RowKey Key,
    FieldDescriptor Column,
    object? Value,
    bool IsEditing,
    Action<object?> SetValue);

public record RowContext<T>(
    T Row,
    RowKey Key,
    int RowIndex,
    bool IsSelected,
    bool IsEditing,
    IReadOnlyList<Element> Cells);

public record HeaderContext(
    FieldDescriptor Column,
    SortDirection? CurrentSort,
    Action ToggleSort,
    Action<double> Resize);
```

### 6.4 Virtualization Engine (Decision D12)

Based on analysis of WinUI.TableView and the existing Reactor reconciler, the DataGrid
delegates row virtualization to **WinUI's ItemsRepeater** rather than building a custom
virtualizer.

#### Why ItemsRepeater

WinUI.TableView inherits from `ListView` and gets virtualization for free via
`ItemsStackPanel`. Reactor already has `ItemsRepeater` integration via
`ElementFactory` (used in the reconciler). ItemsRepeater is the more modern and
flexible choice:

- **Element recycling:** ItemsRepeater creates/recycles elements as they enter/leave
  the viewport. The Reactor reconciler's `ElementFactory` already bridges this to
  the component lifecycle.
- **Layout flexibility:** `StackLayout` (vertical) for rows, but could swap to
  `UniformGridLayout` for grid views.
- **Viewport tracking:** ItemsRepeater + ScrollViewer handle all scroll events,
  viewport calculations, and overscan internally.
- **Variable heights:** ItemsRepeater measures each element naturally. No prefix-sum
  or binary search needed — WinUI's layout engine handles it.

#### VirtualList Standalone Component (Decision D2)

The standalone `VirtualList` wraps ItemsRepeater integration into a Reactor component:

```csharp
/// <summary>
/// Virtualizing list that renders only visible items. Wraps WinUI ItemsRepeater.
/// Usable standalone (file lists, log viewers, chat) or composed by DataGrid.
/// </summary>
public record VirtualListElement<T> : Element
{
    /// <summary>Total item count (for scroll thumb sizing).</summary>
    public required int ItemCount { get; init; }

    /// <summary>Renders an item at the given index.</summary>
    public required Func<int, Element> RenderItem { get; init; }

    /// <summary>Stable key for each index (for reconciler identity).</summary>
    public Func<int, string>? GetItemKey { get; init; }

    /// <summary>Fixed item height. Null = measured (variable height).</summary>
    public double? ItemHeight { get; init; }

    /// <summary>Estimated height for unmeasured items.</summary>
    public double EstimatedItemHeight { get; init; } = 40;
}
```

#### How DataGrid Uses It

The DataGrid composes VirtualList for the row area. The DataGrid component manages:
- **Header row** — rendered above the VirtualList (fixed, not virtualized)
- **Row rendering** — `RenderItem` returns a FlexRow of cells per visible row
- **Frozen columns** — separate panels with synchronized vertical scroll
  (same pattern as WinUI.TableView's `FrozenCellsPanel` / `ScrollableCellsPanel`)
- **Data fetching** — the DataPageCache loads blocks as the virtualizer requests
  items near page boundaries

```
┌──────────────────────────────────────────────┐
│  Header Row (fixed, FlexRow of column headers) │
├──────────┬───────────────────────────────────┤
│ Frozen   │  ScrollViewer                     │
│ Columns  │   └─ ItemsRepeater               │
│ (fixed)  │       ├─ Row 0 (FlexRow of cells) │
│          │       ├─ Row 1                    │
│          │       ├─ ...                      │
│          │       └─ Row N                    │
│ (sync'd  │                                   │
│  v-scroll)│                                   │
└──────────┴───────────────────────────────────┘
```

#### What We Don't Build

- No custom prefix-sum virtualizer — ItemsRepeater handles it
- No scroll position calculation — WinUI layout engine handles it
- No element recycling pool — ItemsRepeater + ElementFactory handle it
- No measurement tracking — WinUI measure/arrange handles it

The DataGrid's complexity budget goes to **data, editing, and state management**
instead of reinventing virtualization.

### 6.5 Row Block Cache

For server-side data sources, the grid maintains a block cache (inspired by AG Grid's
Server-Side Row Model):

```csharp
/// <summary>
/// Caches pages of data from IDataSource, keyed by sort+filter state + page index.
/// Evicts blocks outside the viewport via LRU when max capacity is reached.
/// </summary>
internal class DataPageCache<T>
{
    private readonly IDataSource<T> _source;
    private readonly int _maxBlocks;

    /// <summary>Current sort/filter state. Changing this invalidates all blocks.</summary>
    public DataRequest BaseRequest { get; set; }

    /// <summary>
    /// Get the block containing the given row index.
    /// Returns cached data or initiates async fetch.
    /// </summary>
    public async ValueTask<CacheBlock<T>> GetBlockAsync(int blockIndex, CancellationToken ct);

    /// <summary>Invalidate all cached blocks (e.g., after sort/filter change).</summary>
    public void Invalidate();

    /// <summary>Total known row count (from last response's TotalCount).</summary>
    public int? TotalCount { get; }
}

internal record CacheBlock<T>(
    int BlockIndex,
    IReadOnlyList<T> Items,
    BlockStatus Status);

internal enum BlockStatus { Loading, Loaded, Failed }
```

The cache uses a **pull model** (matching Compose Paging 3): accessing a row index that
falls in an unloaded block triggers the fetch. The virtualizer tells the cache which
blocks are needed; the cache returns immediately for loaded blocks and initiates async
loads for missing blocks, returning placeholder status.

### 6.6 Selection State

```csharp
/// <summary>
/// Compact, key-based selection state. Supports single, multi-select, and
/// shift-click range selection.
/// </summary>
public class SelectionState
{
    public SelectionMode Mode { get; }
    public IReadOnlySet<RowKey> SelectedKeys { get; }

    /// <summary>Anchor for shift-click range selection.</summary>
    public RowKey? AnchorKey { get; }

    /// <summary>Currently focused row (for keyboard navigation).</summary>
    public RowKey? FocusedKey { get; }

    // Operations
    public void Select(RowKey key);
    public void Deselect(RowKey key);
    public void Toggle(RowKey key);
    public void SelectRange(RowKey from, RowKey to, IReadOnlyList<RowKey> visibleOrder);
    public void SelectAll(IReadOnlyList<RowKey> allKeys);
    public void Clear();
}
```

### 6.7 Inline Editing (Decision D10)

The DataGrid supports two editing modes: **Cell** and **Row**.

#### Cell Mode (EditMode.Cell)

Single-cell editing. Click/double-click/Enter activates one cell at a time.

```
User double-clicks cell (or presses Enter/F2 on focused cell)
  → Grid enters edit mode for (rowKey, columnId)
  → Cell renderer is replaced with cell editor (from TypeRegistry)
  → User edits value
  → User presses Enter or Tab (or clicks away)
    → Validators run (sync then async)
    → If valid:
      → newRow = field.SetValue(row, newValue)  // return-new-owner (D9)
      → Show new value immediately (optimistic) (D14)
      → OnRowChanged(key, newRow) fires async
      → If async fails: revert cell, show error via ValidationContext
      → If Tab: focus moves to next editable cell
    → If invalid:
      → Validation messages appear on the cell
      → Cell stays in edit mode
  → User presses Escape
    → Edit cancelled, original value restored
```

#### Row Mode (EditMode.Row)

Entire row enters edit mode. All editable cells become editors simultaneously.

```
User triggers row edit (Enter, double-click, or edit button)
  → All editable cells in the row switch to editors
  → User edits multiple cells
  → User commits (Enter, click Save, or click away)
    → All field validators run across the row
    → If all valid:
      → Chain SetValue for all changed fields:
        var newRow = originalRow;
        foreach (var (field, value) in pendingEdits)
            newRow = field.SetValue(newRow, value);  // works for both mutable and immutable (D9)
      → Show new values immediately (optimistic) (D14)
      → OnRowChanged(key, newRow) fires async
      → If async fails: revert row, show errors
    → If invalid:
      → Errors appear on individual cells + row-level visualizer
      → Row stays in edit mode
  → User presses Escape
    → All edits cancelled, entire row reverts
```

#### Editing State

```csharp
internal record EditingState
{
    /// <summary>Cell mode: which single cell is being edited.</summary>
    public (RowKey Row, string Column)? ActiveCell { get; init; }

    /// <summary>Row mode: which row is being edited (all editable cells active).</summary>
    public RowKey? ActiveRow { get; init; }

    /// <summary>Pending values for uncommitted edits (row mode accumulates multiple).</summary>
    public IReadOnlyDictionary<string, object?>? PendingValues { get; init; }

    /// <summary>Validation errors on active cells.</summary>
    public IReadOnlyList<ValidationMessage>? CellErrors { get; init; }
}
```

### 6.8 Keyboard Navigation

Full keyboard grid navigation following ARIA grid patterns:

| Key | Action |
|-----|--------|
| Arrow keys | Move focus between cells |
| Tab / Shift+Tab | Move to next/prev editable cell |
| Enter | Start editing focused cell (or commit edit and move down) |
| Escape | Cancel editing |
| F2 | Start editing (alternative to Enter) |
| Space | Toggle selection on focused row |
| Ctrl+A | Select all |
| Page Up/Down | Scroll by viewport height |
| Home/End | Move to first/last column |
| Ctrl+Home/End | Move to first/last row |

The grid uses `role="grid"`, `role="row"`, `role="gridcell"` with `aria-rowindex` and
`aria-colindex` for accessibility with virtualized rows.

### 6.9 Column Operations

| Operation | How |
|-----------|-----|
| **Sort** | Click header → toggle `Ascending → Descending → None`. Multi-sort via Ctrl+Click. Sort state passed to `DataRequest`. |
| **Filter** | Filter icon in header → filter popup per column type. Text → contains/startsWith. Number → range. Enum → multi-select. Boolean → checkbox. Date → date range. |
| **Resize** | Drag column header border. Minimum 40px. Double-click auto-sizes to content width. |
| **Reorder** | Drag column header. Drop indicator shows new position. Column order stored in state. |
| **Pin** | Right-click header → "Pin Left" / "Pin Right" / "Unpin". Pinned columns render in separate fixed containers with synchronized vertical scroll. |
| **Hide** | Right-click header → "Hide Column". Column visibility menu in grid options. |

---

## 7. The Unified Editing Family

### 7.1 How the Pieces Fit Together

```
                          ┌─────────────────┐
                          │  TypeRegistry    │
                          │                  │
                          │  Type → Editor   │
                          │  Type → Renderer │
                          │  Type → Format   │
                          └────────┬─────────┘
                                   │ resolves editors
                    ┌──────────────┼──────────────┐
                    │              │              │
              ┌─────▼─────┐ ┌─────▼─────┐ ┌─────▼─────┐
              │ FormField  │ │ Property  │ │ DataGrid  │
              │            │ │ Grid      │ │           │
              │ single     │ │ record    │ │ collection│
              │ field edit │ │ edit      │ │ edit      │
              └─────┬─────┘ └─────┬─────┘ └─────┬─────┘
                    │              │              │
                    └──────────────┼──────────────┘
                                   │ validated by
                          ┌────────▼─────────┐
                          │ ValidationContext │
                          │                  │
                          │ IValidator        │
                          │ IAsyncValidator   │
                          │ Touched/Dirty    │
                          └──────────────────┘
```

**TypeRegistry** is the shared editor resolution engine with **tiered editors** (5.7):
- DataGrid cell editing: resolves `CompactEditor ?? Editor ?? built-in` — space-efficient
- PropertyGrid inline: resolves `Editor ?? built-in` — standard size
- FormField: resolves `Editor ?? built-in` — standard size
- "..." expand in any context: resolves `FullEditor` — opens in flyout/dialog
- For built-in types (string, int, bool, enum), all tiers resolve to the same control

**FieldDescriptor** is the shared metadata:
- FormField consumes one FieldDescriptor (label, editor, validators)
- PropertyGrid consumes a list of FieldDescriptors (one per property) via Decompose
- DataGrid consumes a list of FieldDescriptors (one per column) + grid extensions (width, sort, pin)

**ValidationContext** is the shared validation engine:
- FormField: validation context from parent, field-level errors
- PropertyGrid: validation context per-record, property-level errors
- DataGrid: validation context per-editing-row, cell-level errors

### 7.2 Example: One Model, Three Views

```csharp
// Shared model with metadata
public record Contact
{
    [PropertyDisplayName("Full Name")]
    [PropertyCategory("Personal")]
    [ColumnWidth(200)]
    public string Name { get; init; } = "";

    [PropertyDescription("Primary email address")]
    [PropertyCategory("Contact")]
    [ColumnWidth(250)]
    public string Email { get; init; } = "";

    [PropertyCategory("Contact")]
    public string Phone { get; init; } = "";

    [PropertyCategory("Work")]
    [NotSortable]
    public string Notes { get; init; } = "";
}

// Shared registry
var registry = new TypeRegistry();
// Built-in: string → TextField, bool → ToggleSwitch, etc.

// Shared validators
var nameRequired = Validate.Required();
var emailValidator = Validate.Email();

// ── FormField: edit one contact field ─────────────────────────
FormField("Email",
    required: true,
    content: TextField(contact.Email, v => setContact(contact with { Email = v }))
        .Validate("email", contact.Email, emailValidator))

// ── PropertyGrid: edit one contact record ─────────────────────
PropertyGrid(selectedContact, registry, onRootChanged: c => setSelected((Contact)c))

// ── DataGrid: edit a collection of contacts ───────────────────
DataGrid(
    source: new ListDataSource<Contact>(contacts, c => c.Name),
    registry: registry,
    editable: true,
    selectionMode: SelectionMode.Multiple,
    onRowChanged: async (key, contact) => await SaveContact(contact))
```

All three use the same `TypeRegistry` to resolve editors. The PropertyGrid's reflection
provider reads `[PropertyDisplayName]` and `[PropertyCategory]`. The DataGrid's reflection
provider reads those plus `[ColumnWidth]` and `[NotSortable]`. Same metadata, different
interpretations.

### 7.3 Possible Future: AutoForm

Once the shared metadata model is stable, an `AutoForm` component generates a complete
form from a type's FieldDescriptors:

```csharp
// Generates FormFields for each FieldDescriptor with validators
AutoForm(contact, setContact,
    registry: registry,
    onSubmit: HandleSubmit,
    visualizer: VisualizerStyle.Summary)
```

This is the forms-data-entry-ideas proposal's "3C. AutoForm" concept, now built on the
shared metadata model rather than a separate system.

### 7.4 Detail View Integration

A common pattern: select a row in the DataGrid, show a PropertyGrid (or form) for the
selected record in a side panel:

```csharp
var (selectedKey, setSelectedKey) = UseState<RowKey?>(null);
var (selectedContact, setSelectedContact) = UseState<Contact?>(null);

FlexRow(
    // Left: DataGrid
    DataGrid(
        source: dataSource,
        registry: registry,
        selectionMode: SelectionMode.Single,
        onSelectionChanged: keys =>
        {
            setSelectedKey(keys.FirstOrDefault());
            // Fetch full record for detail view
            if (keys.Any())
                LoadContact(keys.First()).ContinueWith(t => setSelectedContact(t.Result));
        }
    ).Flex(grow: 2),

    // Right: PropertyGrid for selected record
    selectedContact is { } contact
        ? PropertyGrid(contact, registry, onRootChanged: c =>
          {
              var updated = (Contact)c;
              setSelectedContact(updated);
              _ = dataSource.UpdateAsync(selectedKey!.Value, updated);
          }).Flex(grow: 1)
        : Text("Select a contact to edit").Flex(grow: 1)
)
```

---

## 8. Research Summary

### 8.1 Data Access Patterns — Key Findings

**Pagination:** Industry consensus is cursor-based pagination as the primary model.
Relay connections, MS-Graph `@odata.nextLink`, Salesforce `nextRecordsUrl`, Azure SDK
`ContinuationToken` all use opaque cursor tokens. Offset-based (`$skip/$top`) exists
as a simpler alternative but is fragile under concurrent mutations.

**Row Identity:** Every framework requires stable key-based row identity. Relay uses
`id`, MS-Graph uses GUID, Salesforce uses 18-char Id, TanStack Table uses `getRowId`.
Index-based identity only appears in the low-level `IItemsRangeInfo` WinUI interface.

**Sort/Filter Descriptors:** The cleanest architectures (TanStack Table, WPF
`SortDescription`) represent sort/filter as serializable data objects, not lambdas.
This is necessary for server push-down, UI display, and preference persistence.

**Capability Discovery:** The `manual*` flag pattern (TanStack Table) and
`SupportsSorting` (IBindingList) allow the consumer to query what the data source
can handle server-side, avoiding runtime surprises.

**Closest .NET Precedent:** Azure SDK's `AsyncPageable<T>` provides the closest
existing pattern — dual-level (item + page) iteration with continuation tokens.
Our `IDataSource<T>` builds on this, adding sort/filter descriptors and key identity.

### 8.2 Data Grid Implementations — Key Findings

**Architecture:**
- **TanStack Table** (headless) is the strongest architecture for a composition
  framework. Separates state/logic from rendering, fully testable, composable features.
- **AG Grid** (SSRM) has the best server-side data model — block-based cache with
  LRU eviction, lazy group expansion.
- **react-virtuoso** has the best variable-height virtualization — ResizeObserver +
  Fenwick tree, scroll correction to prevent jumpiness.

**Virtualization:**
- DOM recycling with absolute positioning (AG Grid) or `transform: translateY`
- Overscan of 3-5 rows for keyboard navigation, 1-2 for touch scrolling
- Fixed heights are O(1) and strongly preferred; measured heights require prefix-sum
  binary search
- Column virtualization is essential for wide grids (>15 columns)

**Editing:**
- AG Grid: cell editor mounted in-place, `getValue()` contract, popup or inline
- TanStack Table: headless — developer manages editing state
- MUI DataGrid: `renderEditCell` with async `processRowUpdate`
- Best pattern for Reactor: lifecycle-based (Idle → Editing → Validating → Committed)
  integrating with the existing ValidationContext

**Selection:**
- Key-based `Set<RowKey>` with anchor for shift-click range selection
- Keyboard: Space toggles, Ctrl+A selects all, Shift+Arrow for range

**Column Operations:**
- Three-region viewport for pinned columns (left | center scrollable | right)
- Column order as `string[]`, pinning as `{ left: string[], right: string[] }`
- Resize via drag with minimum width constraint
- Sort state as serializable descriptors

### 8.3 Framework Comparisons

**SwiftUI:** `LazyVStack` creates views on appear, discards on disappear. No explicit
virtualization control. `Table` (macOS) has sorted columns via `SortDescriptor` binding
but limited to macOS and lacks editing/pinning/reordering.

**Compose Paging 3:** Pull-based loading (accessing `lazyPagingItems[index]` triggers
page loads). `PagingSource` + `Pager` + `LazyColumn` integration. Good architecture
but the three-layer composition adds complexity.

**AG Grid SSRM:** Block-based cache with `maxBlocksInCache`, configurable block size.
`getRows(IServerSideGetRowsRequest)` is essentially our `IDataSource<T>.GetPageAsync`.
Group expansion via `groupKeys` is elegant.

**MUI DataGrid:** `renderEditCell` + `processRowUpdate(newRow, oldRow)` async handler
is a clean editing contract. Built-in validation via `preProcessEditCellProps`.

**WinUI.TableView:** The closest prior art on our platform. Inherits from `ListView`
for automatic row virtualization via `ItemsStackPanel`. Abstract `TableViewColumn` with
typed derivatives (`TextColumn`, `CheckBoxColumn`, etc.) each implementing
`GenerateElement()` / `GenerateEditingElement()`. Cell-level editing. Internal
`CollectionView` wraps the user's `ItemsSource` for filtering/sorting without modifying
the original data. Frozen columns via separate panels with synchronized scroll.
2D cell selection ranges. Key lesson: **delegate virtualization to WinUI**, focus effort
on data model and editing.

---

## 9. Design Decisions

Decisions made during design review, with rationale.

### D1: FieldDescriptor replaces PropertyDescriptor

**Decision:** FieldDescriptor is the single unified type. PropertyDescriptor is removed
as part of this work. No bridge, no coexistence — one type for all three contexts.

**Implication:** PropertyGrid, its reflection provider, attributes, and all tests are
updated to use FieldDescriptor. The `Reactor.PropertyGrid` namespace types that reference
`PropertyDescriptor` are migrated. Template delegates (`PropertyRowTemplate`, etc.) are
updated to accept `FieldDescriptor`.

### D2: Virtualizer ships as a general Reactor component

**Decision:** Ship `VirtualList` and `VirtualGrid` as first-class standalone components
in `Reactor.Virtualization`. DataGrid composes them. They are independently useful for
file lists, log viewers, chat histories, or any large-list scenario.

### D3: Server/client sort/filter — hybrid with automatic default

**Decision:** The grid defaults to automatic capability-based behavior: checks
`DataSourceCapabilities` and pushes to the server when the source declares support,
falls back to client-side otherwise. The developer can override per-operation
(e.g., force client-side sort even when the server supports it, or vice versa).

### D4: Virtualizer supports variable height from day one

**Decision:** Build the virtualizer with measured/variable-height support from the
start. Don't waste effort on a fixed-only implementation that gets thrown away.

The virtualizer always accepts an `estimatedItemSize` for initial layout. When items
render, they report measured sizes. The prefix-sum cache and scroll correction logic
are part of the initial implementation, not a later upgrade.

Fixed-height mode is a degenerate case where all items measure the same — the
virtualizer optimizes this path internally (skip the binary search, use O(1) division)
without exposing a separate API.

### D5: No grouping/tree in v1

**Decision:** DataGrid v1 is a flat list. Grouping and tree expansion are a separate
TreeGrid component in the future. The `DataRequest` type is designed to be extensible
(e.g., `GroupKeys` can be added later as an optional property) but v1 does not
implement it.

### D6: FormField auto-wiring from FieldDescriptor

**Decision:** Yes. FormField gets an overload that takes a FieldDescriptor and
auto-resolves the editor, label, description, and validators:

```csharp
// Auto-wired from FieldDescriptor
FormField(fieldDescriptors["Email"], email, setEmail)

// Equivalent to:
FormField("Email",
    required: fieldDescriptors["Email"].Validators?.Any(v => v is RequiredValidator) ?? false,
    description: fieldDescriptors["Email"].Description,
    content: resolvedEditor.Validate("Email", email, ...validators...))
```

### D7: Observable collections and objects "just work" via data source

**Decision:** The `ObservableListDataSource<T>` handles both:
- `ObservableCollection<T>.CollectionChanged` → add/remove/move rows
- Individual item INPC → re-render only affected visible rows

The grid subscribes to INPC on visible items only (subscribe on scroll-in, unsubscribe
on scroll-out), similar to `IItemsRangeInfo`. This means external mutations to in-memory
objects (e.g., a background thread updating a price) automatically reflect in the grid
without the developer writing any observation code.

### D8: Column filter UI — popup default, customizable

**Decision:** Default is per-column filter popups (AG Grid style). The filter UI is
template-based, so developers can replace the popup with a filter bar or any custom
rendering in the future. v1 ships popups only.

### D9: Return-new-owner SetValue for immutable record support

**Decision:** `FieldDescriptor.SetValue` is `Func<object, object?, object>?` —
takes (owner, newFieldValue), returns (possibly new) owner. This unifies mutable and
immutable editing into a single code path.

See **Section 5.4** for the full immutable editing model, including:
- How the reflection provider generates SetValue for both mutable and immutable properties
- How EditChain simplifies to calling SetValue at each level with early termination
- How DataGrid cell-level and row-level editing work with immutable records
- Why Compose still exists as an optimization for batch updates

### D10: Both cell-level and row-level editing modes

**Decision:** DataGrid supports both modes via an `EditMode` property:

- **Cell** (default for in-memory data sources): Click a cell to edit it. Commit on
  Enter/Tab/blur. One cell at a time. Excel-like feel. Best for quick in-place edits.

- **Row** (default for remote data sources): Enter edit mode for an entire row. All
  editable cells become editors simultaneously. Commit or cancel the whole row
  atomically. Better for remote data sources where you want to validate and persist
  the complete row change in one round-trip.

```csharp
public enum EditMode { Cell, Row }

// Cell mode: OnRowChanged fires per cell commit
DataGrid(source: localSource, editMode: EditMode.Cell, ...)

// Row mode: OnRowChanged fires once per row commit
DataGrid(source: remoteSource, editMode: EditMode.Row, ...)
```

The row-level mode integrates with validation: all field validators run on row commit,
and errors prevent the commit (same as the PropertyGrid model where all fields are
always visible and editable).

### D11: RowHeight property for uniform-height fast path

**Decision:** DataGrid exposes both `RowHeight` and `EstimatedRowHeight`:

- `RowHeight` (double?): When set, all rows have this exact height. The virtualizer
  uses O(1) offset calculation. No measurement needed. This is the common case for
  data grids and should be the fastest path.

- `EstimatedRowHeight` (double, default 40): When `RowHeight` is null, the virtualizer
  measures each row and uses this as the initial estimate for unmeasured rows.

```csharp
// Fixed height — O(1) virtualization, most common
DataGrid(source: ..., rowHeight: 40)

// Variable height — measured, for rich content
DataGrid(source: ..., estimatedRowHeight: 60)
```

The virtualizer supports both modes from day one (D4). `RowHeight` is the fast path
that avoids measurement overhead entirely.

### D12: WinUI virtualization strategy — leverage ListView

**Decision:** After analyzing WinUI.TableView, the answer is clear: **inherit from or
compose with ListView/ItemsRepeater** for row virtualization, rather than building a
custom virtualizer from scratch.

**WinUI.TableView analysis findings:**
- `TableView` inherits from `ListView`, getting automatic row virtualization via
  `ItemsStackPanel` for free
- Row recycling is handled by `GetContainerForItemOverride()` /
  `PrepareContainerForItemOverride()` — standard WinUI item container pattern
- `ScrollViewer` obtained via `GetTemplateChild("ScrollViewer")` in `OnApplyTemplate()`
- Uses `ScrollViewer.ViewChanged` event for scroll tracking + `ChangeView()` for
  programmatic scrolling
- Frozen columns use separate panels: `ScrollableCellsPanel` and `FrozenCellsPanel`
  with synchronized scroll offset
- Internal `CollectionView` wraps the user's `ItemsSource`, providing filtering/sorting
  without modifying the original data

**Implication for Reactor DataGrid:** Two viable approaches:

**Option A — Reconciler-driven ListView composition:** The DataGrid element renders into
a WinUI `ListView` (or `ItemsRepeater`), and the Reactor reconciler generates
`TableViewRow`-like containers. This leverages native virtualization but requires
integrating with the reconciler's element lifecycle.

**Option B — Pure Reactor virtualizer:** Build the virtualizer as a Reactor component that
manages a `ScrollViewer` + absolutely-positioned children. More control, but reimplements
what `ItemsStackPanel` already does.

**Lean:** Option A for v1 — compose with `ItemsRepeater` (modern, more flexible than
ListView) and let WinUI handle element recycling and viewport tracking. The Reactor
reconciler already has `ItemsRepeater` integration via `ElementFactory`. The
`VirtualList` standalone component (D2) wraps this integration for general use.

**The standalone VirtualList (D2) still ships** — it wraps the ItemsRepeater integration
into a convenient Reactor component. But the hard virtualization work is delegated to
WinUI, not reimplemented.

### D13: No column virtualization in v1

**Decision:** Column virtualization is not needed for v1. Most enterprise grids have
10-15 columns. All columns are rendered for every visible row. This simplifies the
rendering model significantly. Column virtualization can be added later as an
optimization for wide-table scenarios.

### D14: Optimistic updates

**Decision:** Show the edited value immediately (optimistic). If the async
`OnRowChanged` handler throws, revert the cell to its original value and surface the
error via the row's `ValidationContext` (e.g., as an external validation message).

```
User commits edit → cell shows new value immediately
  → OnRowChanged fires async (server persistence)
  → If success: done
  → If failure: cell reverts to original value
                 + error message on the row (via ctx.AddExternal)
```

This matches the behavior users expect from modern grids and integrates naturally
with the existing validation visualizer system.

### D15: EditMode defaults to Cell

**Decision:** `EditMode` defaults to `Cell` in all cases. The developer explicitly
chooses `EditMode.Row` when appropriate (typically for remote data sources where
atomic row commits are preferred). No auto-detection from data source type.

### D16: No undo/redo history

**Decision:** Not in scope. The immutable record pattern naturally produces snapshots,
but building an undo stack adds state management complexity without clear user demand.
Can be added later as an opt-in feature if needed.

### D17: Editor tiers — Compact, Standard, Full

**Decision:** `TypeMetadata` gains two optional editor slots alongside the existing
`Editor`: `CompactEditor` (for grid cells) and `FullEditor` (for "..." dialog/flyout).
See Section 5.7 for details.

- Built-in primitives don't use tiers (same editor at all sizes)
- Custom types opt in by registering additional editors
- The "..." button appears automatically when `FullEditor` is registered
- `FieldDescriptor.Editor` overrides everything for a specific field (escape hatch)

---

## 10. Implementation Phases

### Phase 0: Shared Foundation (2-3 weeks)

- Define `FieldDescriptor` in `Reactor.Data` namespace
- **Migrate PropertyGrid from `PropertyDescriptor` to `FieldDescriptor`** (D1)
  - Update `ReflectionTypeMetadataProvider`, `TypeMetadata.Decompose`, `EditChain`
  - Update `PropertyGridComponent`, templates, DSL
  - Update all PropertyGrid tests
- Extend `TypeRegistry` with cell renderer, formatter, and **editor tiers** (D17)
  - Add `CompactEditor` and `FullEditor` slots to `TypeMetadata`
  - Tiered resolution in PropertyGrid and FormField
- Return-new-owner `SetValue` on `FieldDescriptor` for immutable editing (D9)
  - Update EditChain to use SetValue with ReferenceEquals early termination
- Core data access types: `RowKey`, `DataPage<T>`, `DataRequest`, `SortDescriptor`,
  `FilterDescriptor`, `IDataSource<T>`, `DataSourceCapabilities`
- `ListDataSource<T>` and `ObservableListDataSource<T>` implementations (D7)
- FormField overload that auto-wires from `FieldDescriptor` (D6)

### Phase 1: VirtualList Component (2-3 weeks)

- `VirtualList<T>` Reactor component wrapping **ItemsRepeater** (D2, D12)
  - Reconciler integration via existing `ElementFactory`
  - Fixed-height fast path via `RowHeight` prop (D11)
  - Variable-height mode via measured layout (D4)
  - Scroll-to-index, scroll position restoration
- Standalone usage (file lists, log viewers, chat histories)
- Performance benchmarks (target: 100k rows at 60fps scroll)

### Phase 2: Basic DataGrid (4-6 weeks)

- `DataGridComponent` composing `VirtualList` for row area
- Header row with sort indicators (fixed, above the virtualizing area)
- Row rendering as FlexRow of cells, keyed by RowKey
- Column resize (drag) and reorder (drag)
- Keyboard navigation (arrow keys, Enter, Escape, Tab)
- Integration with `TypeRegistry` for cell renderers
- Selection (single, multi, shift-click range) (D13: no column virtualization)
- **Hybrid sort/filter push-down** — auto from capabilities, developer override (D3)

### Phase 3: Inline Editing + Validation (3-4 weeks)

- **Cell mode** and **Row mode** editing (D10)
- Editor resolution from `TypeRegistry` (same editors as PropertyGrid)
- Return-new-owner `SetValue` for immutable record editing (D9)
- Cell-level and row-level validation via `ValidationContext`
- Per-row validation visualizer
- `OnRowChanged` async commit with **optimistic updates** (D14)
- Revert + error display on async failure

### Phase 4: Server-Side Data Sources (3-4 weeks)

- `DataPageCache<T>` block cache with LRU eviction
- Server-side sort/filter push-down
- Loading states (skeleton rows, spinner)
- `GraphDataSource<T>` — MS-Graph/OData provider
- `EfDataSource<T>` — Entity Framework provider

### Phase 5: Advanced Features (4-6 weeks)

- Column pinning (frozen columns + scrollable center, same pattern as WinUI.TableView)
- **Column filter popups** — per-column, template-based for future customization (D8)
- Column visibility management (hide/show via header context menu)
- Text search / global filter
- Row details / expand (optional detail view below a row)

### Phase 6: Providers + Polish (3-4 weeks)

- `GraphQLDataSource<T>` — GraphQL with Relay connections
- `CosmosDataSource<T>` — Azure CosmosDB
- Detail panel integration (DataGrid + PropertyGrid side-by-side)
- Accessibility audit (ARIA grid roles, screen reader testing)
- Performance optimization (column virtualization, render batching)

### Future

- Grouping / tree data
- Pivot mode
- Export (CSV, Excel)
- Print layout
- Row drag-and-drop reordering
- Master-detail (expandable rows)
- Clipboard (copy/paste cells)
- Undo/redo editing

---

## Appendix A: Column DSL Helper

```csharp
public static class Factories
{
    /// <summary>
    /// Creates a column definition for a typed property accessor.
    /// </summary>
    public static FieldDescriptor Column<T>(
        string name,
        Func<T, object?> accessor,
        bool editable = false,
        string? displayName = null,
        string? format = null,
        double? width = null,
        PinPosition pin = PinPosition.None) =>
        new()
        {
            Name = name,
            DisplayName = displayName ?? name,
            FieldType = typeof(T).GetProperty(name)?.PropertyType ?? typeof(object),
            GetValue = obj => accessor((T)obj),
            SetValue = editable
                ? (obj, val) => SetProperty((T)obj, name, val)
                : null,
            IsReadOnly = !editable,
            Width = width,
            Pin = pin,
            FormatValue = format is not null
                ? val => string.Format($"{{0:{format}}}", val)
                : null,
        };
}
```

## Appendix B: External Framework Cross-Reference

| Concept in Reactor | Relay | TanStack | AG Grid | MS-Graph | SwiftUI | Compose |
|-----------------|-------|----------|---------|----------|---------|---------|
| `IDataSource<T>` | Connection resolver | `queryFn` | `IServerSideDatasource` | Graph client | `@FetchRequest` | `PagingSource` |
| `DataPage<T>` | Connection response | `useInfiniteQuery` page | Block | OData response | N/A | `LoadResult.Page` |
| `DataRequest` | Connection args | Query key + variables | `IServerSideGetRowsRequest` | OData `$` params | `NSPredicate` + `NSSortDescriptor` | `LoadParams` |
| `RowKey` | Node `id` | `getRowId` | `getRowId` | Entity `id` | `\.id` keypath | `key` lambda |
| `SortDescriptor` | GraphQL `orderBy` | `SortingState` | `SortModel` | `$orderby` | `SortDescriptor` | N/A (ViewModel) |
| `FilterDescriptor` | GraphQL `filter` | `ColumnFiltersState` | `FilterModel` | `$filter` | `NSPredicate` | N/A (ViewModel) |
| `TypeRegistry` | N/A | Column `cell` renderer | `cellRenderer` + `cellEditor` | N/A | N/A | N/A |
| `FieldDescriptor` | N/A | `ColumnDef` | `ColDef` | N/A | `TableColumn` | N/A |
| `Virtualizer` | N/A | `@tanstack/react-virtual` | Built-in DOM recycling | N/A | `LazyVStack` | `LazyColumn` |
| `ValidationContext` | N/A | N/A | Cell validation | N/A | N/A | N/A |
