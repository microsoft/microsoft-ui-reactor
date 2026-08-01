using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Controls.Validation;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// Headless state machine for the DataGrid. Manages sort state, selection,
/// column order/sizing/visibility, and editing state. Pure logic, no UI
/// dependencies — fully testable without rendering.
/// </summary>
public class DataGridState<T>
{
    /// <summary>
    /// Hard cap on the page size used for the client-fallback "load all
    /// rows" path. Apps mounting against a non-server-capable source that
    /// expect more rows should configure their backing store accordingly.
    /// TASK-097.
    /// </summary>
    public static int MaxClientFallbackPageSize { get; set; } = 100_000;

    // Reactivity of ctor-captured values (issue #872 audit):
    //  • _source    — captured once, but the DataGrid factory keys its component on the source
    //                 (.WithKey($"dg-{typeof(T).Name}-{source.GetHashCode()}")), so a changed key
    //                 (i.e. a source whose GetHashCode() differs) remounts the grid with a fresh
    //                 state ⇒ effectively reactive via remount. (Two distinct sources that hash to
    //                 the same value would not remount, but that is the general WithKey contract.)
    //  • _selectionMode — reactive: reconciled from the prop each render via SetSelectionMode.
    //  • _blockSize — captured once by design: it only sizes the initial DataPageCache; re-deriving
    //                 it (from RowHeight) would rebuild the cache and discard already-loaded rows.
    //  • _columns   — the visible grid (header/layout/cells) consumes the component's fresh columns
    //                 arg each render, so what is on screen reacts to prop column changes. The
    //                 internal _columns list below (and _columnIndexByName) additionally backs
    //                 column reorder/hide/resize AND keyboard navigation + edit/commit column
    //                 resolution; those internal paths are captured at construction and are
    //                 intentionally NOT re-synced from prop column changes (reconciling them with
    //                 user-driven column state is a separate concern beyond #872).
    private readonly IDataSource<T> _source;
    private SelectionMode _selectionMode;
    private readonly int _blockSize;

    // ── Sort state ────────────────────────────────────────────────

    private List<SortDescriptor> _sorts = new();
    private List<FilterDescriptor> _filters = new();

    /// <summary>Current sort descriptors (ordered by priority).</summary>
    public IReadOnlyList<SortDescriptor> Sorts => _sorts;

    /// <summary>Current filter descriptors.</summary>
    public IReadOnlyList<FilterDescriptor> Filters => _filters;

    // Version counters + O(1) lookup maps kept in sync with _sorts / _filters. They let the
    // component memoize sort/filter-derived render output (the sort key, the DataRequest) and
    // let header rendering look up per-column sort/filter state without re-running LINQ each
    // render. _sorts is mutated only in ToggleSort; _filters only in Set/Clear/ClearAll — every
    // one of those bumps the matching version and refreshes the matching map.
    private int _sortVersion;
    private int _filterVersion;
    private readonly Dictionary<string, SortDirection> _sortDirByField = new();
    private readonly Dictionary<string, FilterDescriptor> _filterByField = new();

    /// <summary>Monotonically increasing version, bumped whenever sort state changes.</summary>
    internal int SortVersion => _sortVersion;

    /// <summary>Monotonically increasing version, bumped whenever filter state changes.</summary>
    internal int FilterVersion => _filterVersion;

    // ── Selection state ──────────────────────────────────────────

    private readonly HashSet<RowKey> _selectedKeys = new();
    private int _selectionVersion;

    /// <summary>Currently selected row keys.</summary>
    public IReadOnlySet<RowKey> SelectedKeys => _selectedKeys;

    /// <summary>Monotonically increasing version number, bumped on every selection change.</summary>
    public int SelectionVersion => _selectionVersion;

    /// <summary>
    /// The active selection mode. Reconciled from the <c>SelectionMode</c> prop each render via
    /// <see cref="SetSelectionMode"/> (issue #872), so it stays in sync after the first mount.
    /// </summary>
    public SelectionMode SelectionMode => _selectionMode;

    /// <summary>Anchor key for shift-click range selection.</summary>
    public RowKey? AnchorKey { get; private set; }

    /// <summary>Currently focused row key (for keyboard navigation).</summary>
    public RowKey? FocusedKey { get; private set; }

    // ── Focus state (cell-level) ─────────────────────────────────

    private int _focusedRowIndex = -1;
    private int _focusedColIndex = -1;

    /// <summary>Row index of the focused cell, or -1 if no focus.</summary>
    public int FocusedRowIndex => _focusedRowIndex;

    /// <summary>Column index of the focused cell, or -1 if no focus.</summary>
    public int FocusedColIndex => _focusedColIndex;

    // ── Editing state ────────────────────────────────────────────

    private RowKey? _editingRowKey;
    private string? _editingColumnName;
    private object? _editingValue;
    private int _editingVersion;

    // Row-mode editing: pending values for all editable columns in the row.
    private Dictionary<string, object?>? _rowEditValues;
    private bool _isRowEditing;

    // Validation context for the currently editing cell/row.
    private ValidationContext? _editValidation;

    /// <summary>Row key of the cell currently being edited, or null.</summary>
    public RowKey? EditingRowKey => _editingRowKey;

    /// <summary>Column name of the cell currently being edited, or null.</summary>
    public string? EditingColumnName => _editingColumnName;

    /// <summary>The pending (uncommitted) value for the cell being edited.</summary>
    public object? EditingValue => _editingValue;

    /// <summary>Whether a cell is currently in edit mode.</summary>
    public bool IsEditing => _editingRowKey is not null;

    /// <summary>Whether the entire row is in edit mode (vs single cell).</summary>
    public bool IsRowEditing => _isRowEditing;

    /// <summary>
    /// One-shot guard, set synchronously when Tab is pressed while a cell is being edited. An
    /// editing-Tab moves real focus out of the single-tab-stop grid, which fires the grid's LostFocus
    /// handler — but the editing-Tab flow (HandleKeyDown) itself commits the current cell and reopens
    /// the editor on the next cell. Without this guard the LostFocus safety-net would commit a second
    /// time and tear down that just-reopened editor. Consumed (cleared) synchronously by the next
    /// LostFocus event handler, before it schedules its deferred commit check.
    /// </summary>
    internal bool SuppressNextLostFocusCommit { get; set; }

    /// <summary>Gets the pending row-edit value for a specific column, or null if not in row edit.</summary>
    public object? GetRowEditValue(string columnName)
        => _rowEditValues?.TryGetValue(columnName, out var v) == true ? v : null;

    /// <summary>Monotonically increasing version, bumped on every editing state change.</summary>
    public int EditingVersion => _editingVersion;

    /// <summary>The validation context for the current edit, or null if not editing.</summary>
    public ValidationContext? EditValidation => _editValidation;

    /// <summary>Whether the current edit has validation errors.</summary>
    public bool HasValidationErrors => _editValidation is not null && !_editValidation.IsValid();

    // ── Async commit state ─────────────────────────────────────────

    private readonly Dictionary<RowKey, T> _pendingCommitOriginals = new();
    private readonly HashSet<RowKey> _committingRows = new();
    private readonly Dictionary<RowKey, string> _commitErrors = new();

    /// <summary>Whether a specific row is currently being committed asynchronously.</summary>
    public bool IsCommitting(RowKey key) => _committingRows.Contains(key);

    /// <summary>Gets the commit error for a row, or null if no error.</summary>
    public string? GetCommitError(RowKey key) => _commitErrors.TryGetValue(key, out var err) ? err : null;

    /// <summary>Whether any rows are currently being committed.</summary>
    public bool HasPendingCommits => _committingRows.Count > 0;

    // ── Column state ─────────────────────────────────────────────

    private List<FieldDescriptor> _columns;
    private readonly Dictionary<string, double> _columnWidths = new();
    private readonly HashSet<string> _hiddenColumns = new();

    // Bumped on every column add/remove/reorder/resize/pin/hide/show. Drives the visible-column
    // cache and the per-render column-layout cache (GetColumnLayout), and lets the component
    // treat column-derived render output as stable while it is unchanged.
    private int _columnVersion;

    // O(1) name -> index map over the full _columns list. First-wins, mirroring the prior
    // FirstOrDefault/FindIndex(c => c.Name == name) semantics. Rebuilt only when column
    // membership/order changes (ctor + ReorderColumn); pin replaces a descriptor in place so the
    // index is unaffected.
    private readonly Dictionary<string, int> _columnIndexByName = new();

    // Cached visible-column projection (excludes hidden). Rebuilt lazily when _columnVersion
    // advances past the version it was last built at — avoids a Where + ToList on every access.
    private List<FieldDescriptor>? _visibleColumns;
    private int _visibleColumnsBuiltVersion = -1;

    // Per-render column-layout cache: pixel widths + a shared GridDefinition reused by the header
    // row and every data row. Keyed on _columnVersion + the layout "shape" bitfield. The stable
    // GridDefinition reference lets the reconciler skip re-applying ColumnDefinitions each render.
    private static readonly string[] RowDefStar = { "*" };
    private double[]? _cachedColWidths;
    private GridDefinition? _cachedGridDef;
    private int _layoutCacheVersion = -1;
    private int _layoutCacheShape = -1;
    // The column list the cache was built against. _columnVersion only tracks internal mutations
    // (resize/hide/show/reorder/pin); the caller-supplied columns (el.Columns / auto-generated)
    // can change independently, so reference identity is part of the cache key to avoid returning
    // a layout sized for a stale column set.
    private IReadOnlyList<FieldDescriptor>? _layoutCacheColumns;
    private int _layoutCacheColumnCount = -1;

    // Per-(row, slot) stabilized modifier-handler caches (#671). The static, virtualized
    // RenderRow attaches three routed-input modifier handlers per realized row — the row
    // .OnPointerPressed (select/range/toggle), the cell .OnTapped (click-to-edit) and the
    // expand .OnTapped (row-detail toggle). Rebuilt fresh each render, their delegate identity
    // churned, so post-#665 (per-slot ReferenceEquals modifier comparison) every cell/row failed
    // ShallowEquals and could never hit the reconciler's Update-free skip path. These caches hand
    // RenderRow a REFERENCE-STABLE delegate per (rowKey[, column]) so unchanged cells/rows skip.
    //
    // CAPTURE SAFETY (#721 bug class): the cached delegates capture only the stable rowKey (and
    // column NAME) — never the per-render row INDEX or the per-render element props. They resolve
    // the LIVE row index via GetRowIndex(key) and the live column index via _columnIndexByName at
    // INVOCATION time, and route the post-edit commit through CommitDispatcher (refreshed each
    // render from el.OnRowChanged). So after a data mutation / sort / filter that moves a row, a
    // cached handler still fires against that row's CURRENT position, not a stale one. A handler
    // for a since-removed key resolves to index -1 and no-ops, so a stale entry is harmless.
    //
    // LIFETIME: handlers persist across renders (that reference-stability IS the optimization) and
    // are NOT cleared on data-value churn or reload — the per-tick mutation path keeps the same
    // keys, so clearing would needlessly defeat the skip. They are bounded instead: a cache that
    // grows past StabilizedHandlerCacheCap (a grid scrolled across far more distinct keys than any
    // realistic working set) is dropped wholesale and lazily rebuilt for the currently-visible rows
    // — a one-render skip reset, then steady-state stable again. ClearStabilizedHandlerCaches()
    // also drops them explicitly.
    private const int StabilizedHandlerCacheCap = 50_000;
    private Dictionary<RowKey, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>>? _rowPointerHandlerCache;
    private Dictionary<RowKey, Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs>>? _expandHandlerCache;
    private Dictionary<(RowKey Key, string Column), Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs>>? _cellEditHandlerCache;

    /// <summary>Monotonically increasing version, bumped whenever column order/size/visibility changes.</summary>
    internal int ColumnVersion => _columnVersion;

    /// <summary>Current column definitions (in display order), excluding hidden columns.</summary>
    public IReadOnlyList<FieldDescriptor> Columns
    {
        get
        {
            // Cache the visible-column snapshot per column-version so repeated reads within a render
            // don't re-run the Where+ToList. On invalidation a *fresh* list is built (never an
            // in-place Clear/refill, and never the internal _columns list itself) so any reference
            // previously handed out keeps the contents it was observed with — preserving the
            // snapshot semantics of the old Where(...).ToList() across later column mutations
            // (ReorderColumn/PinColumn mutate _columns in place).
            if (_visibleColumns is null || _visibleColumnsBuiltVersion != _columnVersion)
            {
                var snapshot = new List<FieldDescriptor>(_columns.Count);
                for (int i = 0; i < _columns.Count; i++)
                {
                    if (_hiddenColumns.Count == 0 || !_hiddenColumns.Contains(_columns[i].Name))
                        snapshot.Add(_columns[i]);
                }
                _visibleColumns = snapshot;
                _visibleColumnsBuiltVersion = _columnVersion;
            }
            return _visibleColumns;
        }
    }

    /// <summary>All column definitions, including hidden ones.</summary>
    public IReadOnlyList<FieldDescriptor> AllColumns => _columns;

    /// <summary>Set of currently hidden column names.</summary>
    public IReadOnlySet<string> HiddenColumns => _hiddenColumns;

    // ── Data state ───────────────────────────────────────────────

    private List<T> _loadedItems = new();
    private string[] _rowKeyCache = Array.Empty<string>();
    private int? _totalCount;
    private bool _isLoading;

    // ── Incremental paging (Phase 4) ─────────────────────────────
    private DataPageCache<T>? _cache;
    private readonly Dictionary<int, T> _mutations = new();
    private readonly Dictionary<int, string> _keyOverrides = new();

    // ── Hook-based paging (Phase 3 migration, feature-flagged) ───
    // When set, data accessors read from this resource instead of the legacy _cache /
    // _loadedItems fields. Populated by DataGridComponent under ReactorFeatureFlags.UseHookBasedPaging.
    // The resource is owned by the UseInfiniteResource hook slot — DataGridState neither
    // disposes it nor drives its fetches directly.
    private InfiniteResource<T>? _hookResource;

    /// <summary>The hook-owned <see cref="InfiniteResource{T}"/> when running under
    /// <c>ReactorFeatureFlags.UseHookBasedPaging</c>, or null in legacy mode.</summary>
    public InfiniteResource<T>? HookResource => _hookResource;

    /// <summary>
    /// Attach (or replace) the hook-owned <see cref="InfiniteResource{T}"/> backing this
    /// grid's data. When attached, the legacy <see cref="DataPageCache{T}"/> path is
    /// bypassed — <see cref="ItemCount"/>, <see cref="GetItemAt"/>, <see cref="GetRowKeyAt"/>,
    /// and <see cref="EnsureRangeLoaded"/> read from the resource.
    /// </summary>
    /// <remarks>
    /// Pass <c>null</c> to detach. Deps-change in the hook creates a new resource; call
    /// this each render so the latest reference is used. Re-attaching the same reference
    /// is a no-op.
    /// </remarks>
    public void SetHookResource(InfiniteResource<T>? resource)
    {
        if (ReferenceEquals(_hookResource, resource)) return;
        _hookResource = resource;
        // Deps change invalidated the old resource and reset the mutation overlay — any
        // carried-over row edits would now point at rows that no longer exist.
        _mutations.Clear();
        _keyOverrides.Clear();
    }

    /// <summary>
    /// Delegate installed by <see cref="DataGridComponent{T}"/> each render to
    /// route row-commit dispatch through a <c>UseMutation</c> hook. Static helpers in
    /// the component (<c>HandleKeyDown</c>, <c>RenderRow</c>) invoke it synchronously, on
    /// the thread that committed the edit, instead of spinning up their own <c>Task.Run</c>;
    /// the component's own delegate goes on to reach <c>OnRowChanged</c> on that same
    /// thread, but a delegate you install yourself is free to offload or marshal from
    /// there. When null — e.g. in headless unit tests — callers fall back to invoking
    /// <c>OnRowChanged</c> themselves on a thread-pool thread; see
    /// <c>DataGridComponent&lt;T&gt;.HandleAsyncCommit</c> for both threading contracts.
    /// </summary>
    /// <remarks>
    /// Installing a delegate of your own replaces that fallback outright. It is invoked
    /// synchronously, on the thread that committed the edit, with the row key, the post-edit
    /// item and the pre-edit item — and from there the grid does nothing further for that
    /// commit. The third argument is <c>T?</c> for a reason: the callers resolve it by row
    /// index just before committing, and it is <c>default</c> when there is no row being edited
    /// or the key no longer maps to a loaded row, so a delegate that snapshots it for an
    /// optimistic revert has to cope with that. The delegate also owns both halves the fallback
    /// used to handle: calling the element's <c>OnRowChanged</c>, and driving the row's commit
    /// lifecycle (<see cref="BeginAsyncCommit"/>, then <see cref="CompleteAsyncCommit"/> or
    /// <see cref="FailAsyncCommit"/>). Skip the lifecycle calls and the row never shows a
    /// committing state, an error banner, or an optimistic revert. Note that a grid rendered
    /// through <see cref="DataGridComponent{T}"/> reassigns this property on every
    /// render, so a custom dispatcher only survives on a state you drive yourself.
    /// </remarks>
    public Action<RowKey, T, T?>? CommitDispatcher { get; set; }

    /// <summary>
    /// Currently loaded items. In paged mode, returns items from all loaded cache blocks
    /// plus any mutations overlay. Prefer GetItemAt for index-based access.
    /// </summary>
    public IReadOnlyList<T> LoadedItems
    {
        get
        {
            if (_hookResource is not null)
            {
                // Hook-based paging: flatten the resource's sparse Items into loaded rows.
                var items = _hookResource.Items;
                var result = new List<T>();
                for (int i = 0; i < items.Count; i++)
                {
                    if (_mutations.TryGetValue(i, out var mutated))
                    {
                        result.Add(mutated);
                        continue;
                    }
                    var it = items[i];
                    if (it is not null) result.Add(it);
                }
                return result;
            }

            if (_cache is null) return _loadedItems;

            // Materialize items from loaded cache blocks + mutations.
            var total = _cache.TotalCount ?? 0;
            var blockSize = _cache.BlockSize;
            var cacheResult = new List<T>();
            var blockCount = (total + blockSize - 1) / blockSize;
            for (int b = 0; b < blockCount; b++)
            {
                if (!_cache.IsLoaded(b * blockSize)) continue;
                var block = _cache.GetBlock(b);
                if (block.Status != BlockStatus.Loaded) continue;
                for (int i = 0; i < block.Items.Count; i++)
                {
                    var rowIndex = b * blockSize + i;
                    if (_mutations.TryGetValue(rowIndex, out var mutated))
                        cacheResult.Add(mutated);
                    else
                        cacheResult.Add(block.Items[i]);
                }
            }
            return cacheResult;
        }
    }

    /// <summary>Pre-computed row key strings (legacy — prefer GetRowKeyAt for paginated access).</summary>
    public string[] RowKeyCache => _rowKeyCache;

    /// <summary>Total item count from the data source.</summary>
    public int? TotalCount => _hookResource is not null
        ? _hookResource.TotalCount
        : _totalCount;

    /// <summary>Whether data is currently being fetched.</summary>
    public bool IsLoading => _hookResource is not null
        ? _hookResource.LoadState is LoadState.Loading && _hookResource.TotalCount is null
        : _isLoading;

    /// <summary>
    /// Total number of items in the data set. In paged mode, this is the total count
    /// from the data source (even if not all pages are loaded yet). The VirtualList
    /// uses this for the full scrollbar extent. Unloaded items render as placeholders.
    /// </summary>
    public int ItemCount
    {
        get
        {
            if (_hookResource is not null)
            {
                // Stable count across load transitions. Before the first page completes,
                // TotalCount is null — report 0 so the grid shows the loading template
                // instead of a placeholder-only list whose count will jump on completion.
                // ItemsRepeater doesn't reliably re-realize across a big expansion like
                // 60 → 250 000, which otherwise leaves the data area blank.
                if (_hookResource.TotalCount is { } total) return total;
                if (_hookResource.LoadState is LoadState.Loading) return 0;
                return _hookResource.Items.Count;
            }
            return _cache?.TotalCount ?? _loadedItems.Count;
        }
    }

    /// <summary>The underlying page cache, or null if using legacy eager loading.</summary>
    public DataPageCache<T>? PageCache => _cache;

    /// <summary>
    /// Request that blocks covering the given row range be loaded.
    /// Prefetches one block before and one block after the visible range
    /// so that small scrolls don't hit loading placeholders.
    /// </summary>
    public void EnsureRangeLoaded(int firstRow, int lastRow)
    {
        if (_hookResource is not null)
        {
            // Resource tracks page size internally; it dedups already-loaded / in-flight pages.
            // Mirror the legacy prefetch-one-block-each-direction behaviour by widening the range.
            var total = _hookResource.TotalCount ?? _hookResource.Items.Count;
            if (total == 0) return;
            const int prefetch = 50; // conservative prefetch; resource coalesces per-page.
            var startRow = Math.Max(0, firstRow - prefetch);
            var endRow = Math.Min(lastRow + prefetch, total - 1);
            _hookResource.EnsureRange(startRow, endRow);
            return;
        }

        if (_cache is null) return;
        var blockSize = _cache.BlockSize;
        var total2 = _cache.TotalCount ?? 0;
        if (total2 == 0) return;

        // Expand range by one block in each direction for smooth scrolling
        var startRow2 = Math.Max(0, firstRow - blockSize);
        var endRow2 = Math.Min(lastRow + blockSize, total2 - 1);

        for (int b = startRow2 / blockSize; b <= endRow2 / blockSize; b++)
        {
            if (!_cache.IsLoaded(b * blockSize))
                _cache.RequestBlock(b);
        }
    }

    /// <summary>
    /// Get the item at a specific row index. Returns default(T) if the item's
    /// block hasn't been loaded yet. Does not trigger fetches — ItemCount is
    /// bounded to loaded items, so indices within range are always available.
    /// </summary>
    public T? GetItemAt(int index)
    {
        if (_hookResource is not null)
        {
            if (_mutations.TryGetValue(index, out var mutated))
                return mutated;
            if (index < 0 || index >= _hookResource.Items.Count) return default;
            return _hookResource.Items[index];
        }
        if (_cache is not null)
        {
            if (_mutations.TryGetValue(index, out var mutated))
                return mutated;
            return _cache.PeekItem(index);
        }
        if ((uint)index >= (uint)_loadedItems.Count) return default;
        return _loadedItems[index];
    }

    /// <summary>
    /// Get the row key string for a specific index. Returns null if the item
    /// is not yet loaded.
    /// </summary>
    public string? GetRowKeyAt(int index)
    {
        if (_hookResource is not null)
        {
            if (_keyOverrides.TryGetValue(index, out var overridden))
                return overridden;
            if (index < 0 || index >= _hookResource.Items.Count) return null;
            var item = _hookResource.Items[index];
            if (item is null) return null;
            return _source.GetRowKey(item).Value;
        }
        if (_cache is not null)
        {
            if (_keyOverrides.TryGetValue(index, out var overridden))
                return overridden;
            var item = _cache.PeekItem(index);
            if (item is null) return null;
            return _source.GetRowKey(item).Value;
        }
        if ((uint)index >= (uint)_rowKeyCache.Length) return null;
        return _rowKeyCache[index];
    }

    /// <summary>Whether the item at a specific row index is loaded.</summary>
    public bool IsItemLoaded(int index)
    {
        if (_hookResource is not null)
        {
            if (_mutations.ContainsKey(index)) return true;
            if (index < 0 || index >= _hookResource.Items.Count) return false;
            return _hookResource.Items[index] is not null;
        }
        if (_cache is not null)
            return _mutations.ContainsKey(index) || _cache.IsLoaded(index);
        return (uint)index < (uint)_loadedItems.Count;
    }

    /// <summary>Fires when state changes requiring a re-render.</summary>
    public event Action? StateChanged;

    /// <summary>
    /// Timestamp (Stopwatch ticks) of the last scroll event. Set by the
    /// onVisibleRangeChanged callback so the StateChanged handler can
    /// defer re-renders during active scrolling.
    /// </summary>
    public long LastScrollTick;

    /// <summary>
    /// Timestamp set when a deferred forceRender is dispatched. If
    /// LastScrollTick moves past this value before RefreshRealizedItems
    /// runs, the reconciliation is skipped (scroll restarted).
    /// </summary>
    public long RenderDispatchTick;

    /// <summary>
    /// Last visible range reported by onVisibleRangeChanged. Used to
    /// re-request blocks after scroll settles, in case the final position
    /// wasn't covered by requests made during rapid scrolling.
    /// </summary>
    public int LastVisibleFirst;
    public int LastVisibleLast;

    /// <param name="source">Data source feeding rows into the grid.</param>
    /// <param name="columns">Column descriptors defining accessor + editor + renderer per column.</param>
    /// <param name="selectionMode">Single-row, multi-row, or no selection.</param>
    /// <param name="blockSize">
    /// Page cache block size. When 0 (default), the cache uses its built-in default (50).
    /// Pass a viewport-derived value to ensure the first block fills the screen.
    /// </param>
    public DataGridState(IDataSource<T> source, IReadOnlyList<FieldDescriptor> columns, SelectionMode selectionMode, int blockSize = 0)
    {
        _source = source;
        _columns = new List<FieldDescriptor>(columns);
        _selectionMode = selectionMode;
        _blockSize = blockSize;
        RebuildColumnIndex();
    }

    // ── Sort operations ──────────────────────────────────────────

    /// <summary>
    /// Toggles sort on a column: None -> Ascending -> Descending -> None.
    /// When additive is true (Ctrl+click), adds to multi-sort. Otherwise replaces.
    /// </summary>
    public void ToggleSort(string field, bool additive = false)
    {
        var existing = _sorts.FindIndex(s => s.Field == field);

        if (!additive)
        {
            if (existing >= 0)
            {
                var current = _sorts[existing];
                _sorts.Clear();
                if (current.Direction == SortDirection.Ascending)
                    _sorts.Add(new SortDescriptor(field, SortDirection.Descending));
                // Descending -> None: list stays empty
            }
            else
            {
                _sorts.Clear();
                _sorts.Add(new SortDescriptor(field, SortDirection.Ascending));
            }
        }
        else
        {
            // Additive (multi-sort)
            if (existing >= 0)
            {
                var current = _sorts[existing];
                if (current.Direction == SortDirection.Ascending)
                    _sorts[existing] = new SortDescriptor(field, SortDirection.Descending);
                else
                    _sorts.RemoveAt(existing);
            }
            else
            {
                _sorts.Add(new SortDescriptor(field, SortDirection.Ascending));
            }
        }

        BumpSortVersion();
        StateChanged?.Invoke();
    }

    // Rebuilds the field -> direction map from _sorts. ToggleSort keeps at most one descriptor
    // per field, so last-write-wins here matches the prior FirstOrDefault(field).Direction.
    private void BumpSortVersion()
    {
        _sortVersion++;
        _sortDirByField.Clear();
        for (int i = 0; i < _sorts.Count; i++)
            _sortDirByField[_sorts[i].Field] = _sorts[i].Direction;
    }

    /// <summary>Gets the current sort direction for a column, or null if unsorted.</summary>
    public SortDirection? GetSortDirection(string field)
        => _sortDirByField.TryGetValue(field, out var dir) ? dir : null;

    // ── Filter operations ───────────────────────────────────────

    /// <summary>Set a filter on a column. Replaces any existing filter on the same field.</summary>
    public void SetFilter(FilterDescriptor filter)
    {
        _filters.RemoveAll(f => f.Field == filter.Field);
        _filters.Add(filter);
        BumpFilterVersion();
        StateChanged?.Invoke();
    }

    /// <summary>Remove the filter for a column.</summary>
    public void ClearFilter(string field)
    {
        if (_filters.RemoveAll(f => f.Field == field) > 0)
        {
            BumpFilterVersion();
            StateChanged?.Invoke();
        }
    }

    /// <summary>Remove all filters.</summary>
    public void ClearAllFilters()
    {
        if (_filters.Count > 0)
        {
            _filters.Clear();
            BumpFilterVersion();
            StateChanged?.Invoke();
        }
    }

    // Rebuilds the field -> filter map from _filters. SetFilter keeps at most one filter per
    // field, so last-write-wins here matches the prior FirstOrDefault(field).
    private void BumpFilterVersion()
    {
        _filterVersion++;
        _filterByField.Clear();
        for (int i = 0; i < _filters.Count; i++)
            _filterByField[_filters[i].Field] = _filters[i];
    }

    /// <summary>Gets the active filter for a column, or null.</summary>
    public FilterDescriptor? GetFilter(string field)
        => _filterByField.TryGetValue(field, out var filter) ? filter : null;

    // ── Search state ────────────────────────────────────────────

    private string? _searchQuery;

    /// <summary>Current text search query.</summary>
    public string? SearchQuery => _searchQuery;

    /// <summary>Set the text search query. Triggers a state change for data reload.</summary>
    public void SetSearchQuery(string? query)
    {
        _searchQuery = string.IsNullOrWhiteSpace(query) ? null : query;
        StateChanged?.Invoke();
    }

    // ── Selection operations ─────────────────────────────────────

    /// <summary>
    /// Updates the selection mode in response to a <c>SelectionMode</c> prop change (issue #872).
    /// When narrowing, trims the current selection so it stays valid for the new mode:
    /// <list type="bullet">
    /// <item><description><see cref="SelectionMode.None"/> — clears all selected keys and the anchor.</description></item>
    /// <item><description><see cref="SelectionMode.Single"/> — keeps at most one key (prefers the
    /// selection anchor, then the focused row, else any remaining key).</description></item>
    /// </list>
    /// Bumps <see cref="SelectionVersion"/> when the selected set actually changes, and raises
    /// <see cref="StateChanged"/>. No-ops when the mode is unchanged. The component reconciles this
    /// during render behind a <c>state.SelectionMode != el.SelectionMode</c> guard, so the deferred
    /// re-render it schedules cannot loop.
    /// </summary>
    public void SetSelectionMode(SelectionMode mode)
    {
        if (_selectionMode == mode)
            return;

        _selectionMode = mode;

        var selectionChanged = false;
        if (mode == SelectionMode.None)
        {
            if (_selectedKeys.Count > 0)
            {
                _selectedKeys.Clear();
                selectionChanged = true;
            }
            AnchorKey = null;
        }
        else if (mode == SelectionMode.Single && _selectedKeys.Count > 1)
        {
            // Narrowing Multiple -> Single: keep exactly one key. Prefer the selection anchor
            // (the last row the user acted on), then the focused row, else any remaining key.
            RowKey keep;
            if (AnchorKey is { } anchor && _selectedKeys.Contains(anchor))
                keep = anchor;
            else if (FocusedKey is { } focused && _selectedKeys.Contains(focused))
                keep = focused;
            else
            {
                keep = default;
                foreach (var k in _selectedKeys)
                {
                    keep = k;
                    break;
                }
            }

            _selectedKeys.Clear();
            _selectedKeys.Add(keep);
            AnchorKey = keep;
            selectionChanged = true;
        }

        if (selectionChanged)
            _selectionVersion++;

        StateChanged?.Invoke();
    }

    /// <summary>Handles a row click with optional modifier keys.</summary>
    public void HandleRowClick(RowKey key, bool ctrlKey = false, bool shiftKey = false, IReadOnlyList<RowKey>? visibleOrder = null)
    {
        if (_selectionMode == SelectionMode.None) return;

        if (_selectionMode == SelectionMode.Single)
        {
            _selectedKeys.Clear();
            _selectedKeys.Add(key);
            AnchorKey = key;
            FocusedKey = key;
            _selectionVersion++;
            StateChanged?.Invoke();
            return;
        }

        // Multiple selection mode. For range (shift) selection we need an ordering: prefer the
        // explicit visibleOrder, otherwise fall back to the internal row-key cache. The cache
        // fallback scans by index (SelectRangeByKeyCache) instead of materializing the entire
        // _rowKeyCache into a List<RowKey> — that materialization boxed/allocated one RowKey per
        // row (100k+ on large client-fallback loads) on every click, not just shift-clicks.
        if (shiftKey && AnchorKey is not null && (visibleOrder is not null || _rowKeyCache.Length > 0))
        {
            if (visibleOrder is not null)
                SelectRange(AnchorKey.Value, key, visibleOrder);
            else
                SelectRangeByKeyCache(AnchorKey.Value, key);
        }
        else if (ctrlKey)
        {
            if (!_selectedKeys.Remove(key))
                _selectedKeys.Add(key);
            AnchorKey = key;
        }
        else
        {
            _selectedKeys.Clear();
            _selectedKeys.Add(key);
            AnchorKey = key;
        }

        FocusedKey = key;
        _selectionVersion++;
        StateChanged?.Invoke();
    }

    /// <summary>Select a range of rows between two keys based on visible order.</summary>
    public void SelectRange(RowKey from, RowKey to, IReadOnlyList<RowKey> visibleOrder)
    {
        var fromIndex = -1;
        var toIndex = -1;
        for (int i = 0; i < visibleOrder.Count; i++)
        {
            if (visibleOrder[i].Equals(from)) fromIndex = i;
            if (visibleOrder[i].Equals(to)) toIndex = i;
        }

        if (fromIndex < 0 || toIndex < 0) return;

        var start = Math.Min(fromIndex, toIndex);
        var end = Math.Max(fromIndex, toIndex);

        _selectedKeys.Clear();
        for (int i = start; i <= end; i++)
            _selectedKeys.Add(visibleOrder[i]);

        _selectionVersion++;
        StateChanged?.Invoke();
    }

    // Range-select against the internal _rowKeyCache (string[]) without materializing a
    // List<RowKey>. Behaviourally identical to SelectRange(from, to, _rowKeyCache.Select(k =>
    // new RowKey(k)).ToList()): RowKey is a record struct whose equality is its ordinal Value
    // string, so comparing cache strings is the same as comparing the constructed RowKeys, and
    // last-occurrence-wins matches SelectRange's loop. No-ops (no version bump) when either key
    // is absent, exactly as SelectRange does.
    private void SelectRangeByKeyCache(RowKey from, RowKey to)
    {
        var fromValue = from.Value;
        var toValue = to.Value;
        var fromIndex = -1;
        var toIndex = -1;
        for (int i = 0; i < _rowKeyCache.Length; i++)
        {
            if (_rowKeyCache[i] == fromValue) fromIndex = i;
            if (_rowKeyCache[i] == toValue) toIndex = i;
        }

        if (fromIndex < 0 || toIndex < 0) return;

        var start = Math.Min(fromIndex, toIndex);
        var end = Math.Max(fromIndex, toIndex);

        _selectedKeys.Clear();
        for (int i = start; i <= end; i++)
            _selectedKeys.Add(new RowKey(_rowKeyCache[i]));

        _selectionVersion++;
        StateChanged?.Invoke();
    }

    /// <summary>Select all provided keys.</summary>
    public void SelectAll(IReadOnlyList<RowKey> allKeys)
    {
        if (_selectionMode != SelectionMode.Multiple) return;
        _selectedKeys.Clear();
        foreach (var key in allKeys)
            _selectedKeys.Add(key);
        _selectionVersion++;
        StateChanged?.Invoke();
    }

    /// <summary>Clear all selection.</summary>
    public void ClearSelection()
    {
        _selectedKeys.Clear();
        AnchorKey = null;
        _selectionVersion++;
        StateChanged?.Invoke();
    }

    /// <summary>Check if a row is selected.</summary>
    public bool IsSelected(RowKey key) => _selectedKeys.Contains(key);

    // ── Column operations ────────────────────────────────────────

    /// <summary>Gets the effective width for a column.</summary>
    public double GetColumnWidth(string columnName)
    {
        if (_columnWidths.TryGetValue(columnName, out var width))
            return width;

        var col = _columnIndexByName.TryGetValue(columnName, out var i) ? _columns[i] : null;
        return col?.Width ?? 120;
    }

    /// <summary>Resize a column and trigger a re-render.</summary>
    public void ResizeColumn(string columnName, double newWidth)
    {
        var col = _columnIndexByName.TryGetValue(columnName, out var i) ? _columns[i] : null;
        var minWidth = col?.MinWidth ?? 40;
        var maxWidth = col?.MaxWidth ?? double.MaxValue;
        _columnWidths[columnName] = Math.Clamp(newWidth, minWidth, maxWidth);
        _columnVersion++;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Returns the cached per-column pixel widths and the shared <see cref="GridDefinition"/> for
    /// the current visible columns + layout shape. Stable across renders (same array and
    /// definition references) until a column is added/removed/reordered/resized/pinned/hidden/
    /// shown — tracked by <see cref="ColumnVersion"/>. The header row and every data row share the
    /// one returned definition so the reconciler can skip re-applying ColumnDefinitions.
    /// </summary>
    /// <remarks>
    /// The cache treats the external <paramref name="columns"/> list by REFERENCE IDENTITY
    /// (<see cref="object.ReferenceEquals(object, object)"/>) — the same reference-identity
    /// convention the framework's <c>ShallowEquals</c> uses for element children/refs. Per
    /// Reactor's immutable-props contract, callers must pass a NEW list reference when the visible
    /// column set changes (e.g. a fresh <c>el.Columns</c>); the per-column widths themselves always
    /// come from the version-tracked <see cref="GetColumnWidth"/>, so an internal
    /// resize/hide/show/reorder/pin (which bumps <see cref="ColumnVersion"/>) invalidates correctly.
    /// Mutating the SAME list reference IN PLACE is UNSUPPORTED: a count change is still caught (the
    /// <c>columns.Count</c> guard), but a same-count in-place reorder of the passed list — with no
    /// internal mutator bumping <see cref="ColumnVersion"/> — will serve the prior layout for that
    /// reference. This is by design: detecting it would require per-render content hashing of the
    /// column list, which defeats the cache. See
    /// <c>GetColumnLayout_SameCount_InPlace_Reorder_Serves_Cached_Layout_By_Design</c>.
    /// </remarks>
    /// <param name="columns">Current visible columns (i.e. <see cref="Columns"/>).</param>
    /// <param name="hasRowDetailColumn">Whether a 24px row-detail expander column leads the grid.</param>
    /// <param name="hasSelectColumn">Whether a 40px selection column leads the grid.</param>
    /// <param name="hasRowEditActionsColumn">Whether an Auto-width row-edit actions column trails the grid.</param>
    internal (double[] ColWidths, GridDefinition GridDef) GetColumnLayout(
        IReadOnlyList<FieldDescriptor> columns,
        bool hasRowDetailColumn,
        bool hasSelectColumn,
        bool hasRowEditActionsColumn)
    {
        int shape = (hasRowDetailColumn ? 1 : 0)
                  | (hasSelectColumn ? 2 : 0)
                  | (hasRowEditActionsColumn ? 4 : 0);

        // Cache key relies on reference-identity of `columns` (per the immutable-props
        // convention — see <remarks>): a NEW list reference or a count change rebuilds; a
        // same-count in-place reorder of the SAME reference is intentionally NOT detected.
        if (_cachedColWidths is not null && _cachedGridDef is not null
            && _layoutCacheVersion == _columnVersion && _layoutCacheShape == shape
            && ReferenceEquals(_layoutCacheColumns, columns)
            && _layoutCacheColumnCount == columns.Count)
        {
            return (_cachedColWidths, _cachedGridDef);
        }

        int colCount = columns.Count;
        var colWidths = new double[colCount];
        for (int c = 0; c < colCount; c++)
            colWidths[c] = GetColumnWidth(columns[c].Name);

        int gridColCount = colCount
            + (hasRowDetailColumn ? 1 : 0)
            + (hasSelectColumn ? 1 : 0)
            + (hasRowEditActionsColumn ? 1 : 0);
        var gridColDefs = new string[gridColCount];
        int idx = 0;
        if (hasRowDetailColumn) gridColDefs[idx++] = "24";
        if (hasSelectColumn) gridColDefs[idx++] = "40";
        for (int c = 0; c < colCount; c++)
            gridColDefs[idx++] = colWidths[c].ToString(global::System.Globalization.CultureInfo.InvariantCulture);
        if (hasRowEditActionsColumn) gridColDefs[idx] = "Auto";

        var gridDef = new GridDefinition(gridColDefs, RowDefStar);

        _cachedColWidths = colWidths;
        _cachedGridDef = gridDef;
        _layoutCacheVersion = _columnVersion;
        _layoutCacheShape = shape;
        _layoutCacheColumns = columns;
        _layoutCacheColumnCount = columns.Count;
        return (colWidths, gridDef);
    }


    /// <summary>Reorder a column to a new position.</summary>
    public void ReorderColumn(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _columns.Count) return;
        if (toIndex < 0 || toIndex >= _columns.Count) return;
        if (fromIndex == toIndex) return;

        var col = _columns[fromIndex];
        _columns.RemoveAt(fromIndex);
        _columns.Insert(toIndex, col);
        RebuildColumnIndex();
        _columnVersion++;
        StateChanged?.Invoke();
    }

    // Rebuilds the name -> index map over _columns. First-wins so it mirrors the prior
    // FirstOrDefault/FindIndex(c => c.Name == name) behaviour for any duplicate names.
    private void RebuildColumnIndex()
    {
        _columnIndexByName.Clear();
        for (int i = 0; i < _columns.Count; i++)
        {
            if (!_columnIndexByName.ContainsKey(_columns[i].Name))
                _columnIndexByName[_columns[i].Name] = i;
        }
    }

    /// <summary>Hide a column.</summary>
    public void HideColumn(string columnName)
    {
        if (_hiddenColumns.Add(columnName))
        {
            _columnVersion++;
            StateChanged?.Invoke();
        }
    }

    /// <summary>Show a previously hidden column.</summary>
    public void ShowColumn(string columnName)
    {
        if (_hiddenColumns.Remove(columnName))
        {
            _columnVersion++;
            StateChanged?.Invoke();
        }
    }

    /// <summary>Toggle column visibility.</summary>
    public void ToggleColumnVisibility(string columnName)
    {
        if (!_hiddenColumns.Remove(columnName))
            _hiddenColumns.Add(columnName);
        _columnVersion++;
        StateChanged?.Invoke();
    }

    /// <summary>Check if a column is visible.</summary>
    public bool IsColumnVisible(string columnName) => !_hiddenColumns.Contains(columnName);

    /// <summary>Get visible columns grouped by pin position.</summary>
    public (IReadOnlyList<FieldDescriptor> Left, IReadOnlyList<FieldDescriptor> Center, IReadOnlyList<FieldDescriptor> Right) GetPinnedColumnGroups()
    {
        var visible = Columns;
        var left = new List<FieldDescriptor>();
        var center = new List<FieldDescriptor>();
        var right = new List<FieldDescriptor>();

        foreach (var col in visible)
        {
            switch (col.Pin)
            {
                case PinPosition.Left: left.Add(col); break;
                case PinPosition.Right: right.Add(col); break;
                default: center.Add(col); break;
            }
        }

        return (left, center, right);
    }

    /// <summary>Pin a column to a position at runtime.</summary>
    public void PinColumn(string columnName, PinPosition position)
    {
        if (!_columnIndexByName.TryGetValue(columnName, out var idx)) return;
        _columns[idx] = _columns[idx] with { Pin = position };
        // Pin replaces the descriptor in place — name/index unchanged, so _columnIndexByName stays
        // valid — but bump the version so the visible-column and layout caches pick up the new Pin.
        _columnVersion++;
        StateChanged?.Invoke();
    }

    // ── Row detail expand/collapse ──────────────────────────────

    private readonly HashSet<RowKey> _expandedRows = new();

    /// <summary>Set of currently expanded row keys.</summary>
    public IReadOnlySet<RowKey> ExpandedRows => _expandedRows;

    /// <summary>Check if a row is expanded.</summary>
    public bool IsExpanded(RowKey key) => _expandedRows.Contains(key);

    /// <summary>Toggle the expanded state of a row.</summary>
    public void ToggleRowExpansion(RowKey key)
    {
        if (!_expandedRows.Remove(key))
            _expandedRows.Add(key);
        StateChanged?.Invoke();
    }

    /// <summary>Expand a row.</summary>
    public void ExpandRow(RowKey key)
    {
        if (_expandedRows.Add(key))
            StateChanged?.Invoke();
    }

    /// <summary>Collapse a row.</summary>
    public void CollapseRow(RowKey key)
    {
        if (_expandedRows.Remove(key))
            StateChanged?.Invoke();
    }

    /// <summary>Collapse all expanded rows.</summary>
    public void CollapseAllRows()
    {
        if (_expandedRows.Count > 0)
        {
            _expandedRows.Clear();
            StateChanged?.Invoke();
        }
    }

    // ── Stabilized row/cell modifier handlers (#671) ──────────────────
    // Each factory returns a reference-stable delegate per (rowKey[, column]). RenderRow calls
    // these instead of allocating a fresh closure per render, so post-#665 unchanged cells/rows
    // hit the reconciler's Update-free skip path. The delegates resolve the live row/column index
    // at invocation (never capture the per-render index), so they always act on the row's CURRENT
    // position after a mutation/sort/filter — the #721 stale-closure-capture hazard does not apply.

    /// <summary>
    /// Reference-stable <c>.OnPointerPressed</c> handler for the row identified by
    /// <paramref name="key"/> (row select / shift-range / ctrl-toggle). Resolves the live row
    /// index via <see cref="GetRowIndex"/> at click time; commits any in-flight edit on a
    /// DIFFERENT row first. Returns the same delegate instance across renders so the row can skip.
    /// </summary>
    internal Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> GetRowPointerHandler(RowKey key)
    {
        _rowPointerHandlerCache ??= new();
        if (_rowPointerHandlerCache.TryGetValue(key, out var cached)) return cached;
        if (_rowPointerHandlerCache.Count >= StabilizedHandlerCacheCap) _rowPointerHandlerCache.Clear();

        var capturedKey = key;
        Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler = (sender, e) =>
        {
            var props = e.GetCurrentPoint(null).Properties;
            if (!props.IsLeftButtonPressed) return;

            var mods = e.KeyModifiers;
            var ctrl = mods.HasFlag(global::Windows.System.VirtualKeyModifiers.Control);
            var shift = mods.HasFlag(global::Windows.System.VirtualKeyModifiers.Shift);
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            {
                InvokeRowPointerClick(capturedKey, ctrl, shift);
            });
        };

        _rowPointerHandlerCache[key] = handler;
        return handler;
    }

    /// <summary>
    /// The resolved row-click action behind <see cref="GetRowPointerHandler"/>, split out so the
    /// live-index resolution + commit + focus + selection logic is testable without fabricating a
    /// WinUI <c>PointerRoutedEventArgs</c> (and without a dispatcher). Resolves the row's CURRENT
    /// index from <paramref name="key"/>, so it acts on the right row after a mutation/sort/filter.
    /// </summary>
    internal void InvokeRowPointerClick(RowKey key, bool ctrlKey, bool shiftKey)
    {
        var idx = GetRowIndex(key);
        if (idx < 0) return; // row no longer present (filtered/removed)

        // Commit any active edit when clicking a DIFFERENT row. Clicking within the same row is
        // handled by the cell's OnTapped handler (commit-then-begin); skipping it here prevents the
        // editing TextBox being dismissed when the user clicks to position the cursor.
        if (IsEditing)
        {
            var editingKey = EditingRowKey;
            if (editingKey is null || !editingKey.Value.Equals(key))
                CommitInFlightEditThroughDispatcher();
        }

        SetFocus(idx, _focusedColIndex >= 0 ? _focusedColIndex : 0);

        if (_selectionMode != SelectionMode.None)
            HandleRowClick(key, ctrlKey: ctrlKey, shiftKey: shiftKey);
    }

    /// <summary>
    /// Reference-stable expand/collapse <c>.OnTapped</c> handler for the row-detail toggle of
    /// <paramref name="key"/>. Index-free (<see cref="ToggleRowExpansion"/> keys on the row key),
    /// so it is inherently stable; cached so the toggle cell can skip across renders.
    /// </summary>
    internal Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> GetExpandHandler(RowKey key)
    {
        _expandHandlerCache ??= new();
        if (_expandHandlerCache.TryGetValue(key, out var cached)) return cached;
        if (_expandHandlerCache.Count >= StabilizedHandlerCacheCap) _expandHandlerCache.Clear();

        var capturedKey = key;
        Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> handler = (_, _) =>
        {
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            {
                ToggleRowExpansion(capturedKey);
            });
        };

        _expandHandlerCache[key] = handler;
        return handler;
    }

    /// <summary>
    /// Reference-stable click-to-edit <c>.OnTapped</c> handler for the cell at
    /// (<paramref name="key"/>, <paramref name="columnName"/>). Resolves the live row index via
    /// <see cref="GetRowIndex"/> and the live column index via the name→index map at click time,
    /// commits any in-flight edit first, then begins editing the resolved cell. Returns the same
    /// delegate instance across renders so the cell can skip.
    /// </summary>
    internal Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> GetCellEditHandler(RowKey key, string columnName)
    {
        _cellEditHandlerCache ??= new();
        var cacheKey = (key, columnName);
        if (_cellEditHandlerCache.TryGetValue(cacheKey, out var cached)) return cached;
        if (_cellEditHandlerCache.Count >= StabilizedHandlerCacheCap) _cellEditHandlerCache.Clear();

        var capturedKey = key;
        var capturedColumn = columnName;
        Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> handler = (sender, e) =>
        {
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            {
                InvokeCellEditClick(capturedKey, capturedColumn);
            });
        };

        _cellEditHandlerCache[cacheKey] = handler;
        return handler;
    }

    /// <summary>
    /// The resolved click-to-edit action behind <see cref="GetCellEditHandler"/>, split out so the
    /// live row/column resolution + commit + begin-edit logic is testable without a WinUI
    /// <c>TappedRoutedEventArgs</c> or a dispatcher. Resolves the CURRENT row index from
    /// <paramref name="key"/> and the CURRENT column index from <paramref name="columnName"/>, so
    /// it edits the right cell after a mutation/sort/filter or a column reorder/hide.
    /// </summary>
    internal void InvokeCellEditClick(RowKey key, string columnName)
    {
        var rowIdx = GetRowIndex(key);
        if (rowIdx < 0) return; // row no longer present
        if (!_columnIndexByName.TryGetValue(columnName, out var colIdx)) return; // column hidden/removed

        // Commit any in-flight edit BEFORE starting a new one — BeginEdit overwrites the pending
        // value with the new cell's current value, which would destroy an in-flight edit otherwise.
        if (IsEditing)
            CommitInFlightEditThroughDispatcher();

        SetFocus(rowIdx, colIdx);
        BeginEdit(rowIdx, colIdx);
    }

    /// <summary>
    /// Commits the in-flight edit (if any) and routes the result through the installed
    /// <see cref="CommitDispatcher"/> (the same path <c>HandleAsyncCommit</c> uses), capturing the
    /// pre-commit item for optimistic-revert. Used by the stabilized handlers so they never capture
    /// the per-render element to reach <c>el.OnRowChanged</c>; the dispatcher is refreshed each
    /// render and is null exactly when no <c>OnRowChanged</c> is wired.
    /// </summary>
    private void CommitInFlightEditThroughDispatcher()
    {
        if (!IsEditing) return;

        var editingKey = EditingRowKey;
        T? originalItem = default;
        if (editingKey is not null)
        {
            var oi = GetRowIndex(editingKey.Value);
            if (oi >= 0) originalItem = GetItemAt(oi);
        }

        var result = CommitEdit();
        if (result is not null && CommitDispatcher is { } dispatch)
            dispatch(result.Value.Key, result.Value.NewItem, originalItem);
    }

    /// <summary>
    /// Drops all cached stabilized row/cell/expand handlers (#671). Safe to call any time — the
    /// handlers are pure functions of (rowKey[, column]) and are lazily recreated on the next
    /// render. Not called on routine reloads (that would needlessly defeat the skip); exposed for
    /// explicit invalidation and covered by the size-cap eviction in the factory methods.
    /// </summary>
    internal void ClearStabilizedHandlerCaches()
    {
        _rowPointerHandlerCache?.Clear();
        _expandHandlerCache?.Clear();
        _cellEditHandlerCache?.Clear();
    }

    // ── Focus navigation ──────────────────────────────────────────

    /// <summary>Set cell focus to a specific row and column index.</summary>
    public void SetFocus(int rowIndex, int colIndex)
    {
        var rowCount = ItemCount;
        var colCount = _columns.Count;
        if (rowCount == 0 || colCount == 0) return;

        _focusedRowIndex = Math.Clamp(rowIndex, 0, rowCount - 1);
        _focusedColIndex = Math.Clamp(colIndex, 0, colCount - 1);

        // Sync FocusedKey with the row index
        var key = GetRowKeyAt(_focusedRowIndex);
        if (key is not null)
            FocusedKey = new RowKey(key);

        StateChanged?.Invoke();
    }

    /// <summary>Move focus by a delta. Used for arrow key navigation.</summary>
    public void MoveFocus(int rowDelta, int colDelta)
    {
        if (ItemCount == 0 || _columns.Count == 0) return;

        // If no focus yet, start at (0, 0)
        if (_focusedRowIndex < 0) { SetFocus(0, 0); return; }

        SetFocus(_focusedRowIndex + rowDelta, _focusedColIndex + colDelta);
    }

    /// <summary>Move focus to the first column in the current row.</summary>
    public void FocusHome()
    {
        if (_focusedRowIndex < 0) { SetFocus(0, 0); return; }
        SetFocus(_focusedRowIndex, 0);
    }

    /// <summary>Move focus to the last column in the current row.</summary>
    public void FocusEnd()
    {
        if (_focusedRowIndex < 0) { SetFocus(0, _columns.Count - 1); return; }
        SetFocus(_focusedRowIndex, _columns.Count - 1);
    }

    /// <summary>Move focus to the next cell (left to right, top to bottom). Returns false at the end.</summary>
    public bool FocusNextCell()
    {
        var totalRows = ItemCount;
        if (totalRows == 0 || _columns.Count == 0) return false;
        if (_focusedRowIndex < 0) { SetFocus(0, 0); return true; }

        var nextCol = _focusedColIndex + 1;
        if (nextCol < _columns.Count)
        {
            SetFocus(_focusedRowIndex, nextCol);
            return true;
        }

        // Wrap to next row
        var nextRow = _focusedRowIndex + 1;
        if (nextRow < totalRows)
        {
            SetFocus(nextRow, 0);
            return true;
        }

        return false; // At the very end
    }

    /// <summary>Move focus to the previous cell (right to left, bottom to top). Returns false at the start.</summary>
    public bool FocusPrevCell()
    {
        if (ItemCount == 0 || _columns.Count == 0) return false;
        if (_focusedRowIndex < 0) { SetFocus(0, 0); return true; }

        var prevCol = _focusedColIndex - 1;
        if (prevCol >= 0)
        {
            SetFocus(_focusedRowIndex, prevCol);
            return true;
        }

        // Wrap to previous row
        var prevRow = _focusedRowIndex - 1;
        if (prevRow >= 0)
        {
            SetFocus(prevRow, _columns.Count - 1);
            return true;
        }

        return false; // At the very start
    }

    /// <summary>
    /// Advance the grid's logical cell cursor to the next visible column taking part in the active
    /// row edit, wrapping to the first one after the last. Row-mode Tab navigation stays inside the
    /// row and never commits — a row commits only on Enter, Save, or click-away (spec 017 §6.7).
    /// Returns false when no row edit is active, or when no visible column in the row has an editor.
    /// </summary>
    /// <remarks>
    /// Like every other focus API on this type (<see cref="FocusNextCell"/>, <see cref="MoveFocus"/>,
    /// …) this moves the grid's own <see cref="FocusedColIndex"/> bookkeeping only; it does not call
    /// XAML <c>Focus</c>. Real keyboard focus between the row's editors is WinUI's FocusManager tab
    /// order, which runs before the grid's <c>handledEventsToo</c> KeyDown handler.
    /// </remarks>
    public bool FocusNextRowEditColumn() => MoveRowEditFocus(+1);

    /// <summary>
    /// Move the grid's logical cell cursor back to the previous visible column taking part in the
    /// active row edit, wrapping to the last one before the first. The Shift+Tab counterpart of
    /// <see cref="FocusNextRowEditColumn"/>, with the same never-commit guarantee. Returns false
    /// when no row edit is active, or when no visible column in the row has an editor.
    /// </summary>
    /// <remarks>
    /// The grid's own KeyDown handler cannot call this today — it forwards only the raw key with no
    /// modifier state, so it never sees Shift+Tab. That gap is tracked in #987; until it closes, this
    /// is public for the same reason <see cref="FocusPrevCell"/> is: app authors driving custom
    /// keyboard handling need both directions. The logical-cursor caveat on
    /// <see cref="FocusNextRowEditColumn"/> applies here too.
    /// </remarks>
    public bool FocusPrevRowEditColumn() => MoveRowEditFocus(-1);

    /// <summary>
    /// Shared traversal for <see cref="FocusNextRowEditColumn"/> / <see cref="FocusPrevRowEditColumn"/>.
    /// </summary>
    /// <param name="direction">+1 to walk forward, -1 to walk backward.</param>
    private bool MoveRowEditFocus(int direction)
    {
        if (!_isRowEditing || _rowEditValues is null) return false;

        var colCount = _columns.Count;
        if (colCount == 0) return false;

        // _rowEditValues holds exactly the columns BeginRowEdit turned into editors (non-read-only
        // with a SetValue), so this traversal skips read-only columns the same way the rendered
        // editors do. BeginRowEdit snapshots from the full column list, so hidden columns can be
        // in there without a rendered editor — skip those too. Indices are into the full _columns
        // list, matching FocusNextCell and every other focus API.
        //
        // _focusedColIndex is -1 when the row edit began from the Edit button with no prior cell
        // focus. Walking forward from -1 naturally lands on column 0; walking backward has to
        // mirror that and land on the LAST column, so treat "no focus" as one position PAST the
        // end in that direction. Without this, backward from -1 would start at colCount - 2 and
        // never reach the last column at all.
        var origin = _focusedColIndex < 0 && direction < 0 ? colCount : _focusedColIndex;

        for (int step = 1; step <= colCount; step++)
        {
            var idx = ((origin + (step * direction)) % colCount + colCount) % colCount;
            var name = _columns[idx].Name;
            if (_rowEditValues.ContainsKey(name) && IsColumnVisible(name))
            {
                // A row with only one visible editable column wraps straight back to where we
                // started. SetFocus always raises StateChanged, so calling it here would re-render
                // the whole grid for a move that didn't move. Still report success — there IS a
                // valid target column, Tab just had nowhere else to go.
                if (idx != _focusedColIndex)
                    SetFocus(_focusedRowIndex, idx);
                return true;
            }
        }

        return false; // Every editable column in this row is hidden
    }

    /// <summary>
    /// Get the row index for a given row key, or -1 if not found.
    /// Searches the key cache (legacy mode) or scans loaded cache blocks (paged mode).
    /// </summary>
    public int GetRowIndex(RowKey key)
    {
        var keyStr = key.Value;

        if (_hookResource is not null)
        {
            // Mutation overlay first.
            foreach (var (idx, item) in _mutations)
            {
                if (_source.GetRowKey(item).Value == keyStr) return idx;
            }
            foreach (var (idx, k) in _keyOverrides)
            {
                if (k == keyStr) return idx;
            }
            var items = _hookResource.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (_mutations.ContainsKey(i)) continue;
                var it = items[i];
                if (it is null) continue;
                if (_source.GetRowKey(it).Value == keyStr) return i;
            }
            return -1;
        }

        if (_cache is not null)
        {
            // Check mutation overlay first
            foreach (var (idx, item) in _mutations)
            {
                if (_source.GetRowKey(item).Value == keyStr) return idx;
            }
            // Check key overrides
            foreach (var (idx, k) in _keyOverrides)
            {
                if (k == keyStr) return idx;
            }
            // Scan loaded cache blocks
            var total = _cache.TotalCount ?? 0;
            var blockSize = _cache.BlockSize;
            for (int b = 0; b * blockSize < total; b++)
            {
                if (!_cache.IsLoaded(b * blockSize)) continue;
                var block = _cache.GetBlock(b);
                if (block.Status != BlockStatus.Loaded) continue;
                for (int i = 0; i < block.Items.Count; i++)
                {
                    var rowIndex = b * blockSize + i;
                    if (_mutations.ContainsKey(rowIndex)) continue; // already checked
                    if (_source.GetRowKey(block.Items[i]).Value == keyStr) return rowIndex;
                }
            }
            return -1;
        }

        for (int i = 0; i < _rowKeyCache.Length; i++)
            if (_rowKeyCache[i] == keyStr) return i;
        return -1;
    }

    // ── Editing operations ──────────────────────────────────────

    /// <summary>Begin editing the currently focused cell. Returns false if the cell is not editable.</summary>
    public bool BeginEdit()
    {
        if (_focusedRowIndex < 0 || _focusedColIndex < 0) return false;
        return BeginEdit(_focusedRowIndex, _focusedColIndex);
    }

    /// <summary>Begin editing a specific cell.</summary>
    public bool BeginEdit(int rowIndex, int colIndex)
    {
        if (rowIndex < 0 || rowIndex >= ItemCount) return false;
        if (colIndex < 0 || colIndex >= _columns.Count) return false;

        var col = _columns[colIndex];
        if (col.IsReadOnly || col.SetValue is null) return false;

        var item = GetItemAt(rowIndex);
        if (item is null) return false;
        var keyStr = GetRowKeyAt(rowIndex);
        if (keyStr is null) return false;
        var rowKey = new RowKey(keyStr);
        var currentValue = col.GetValue(item!);

        _editingRowKey = rowKey;
        _editingColumnName = col.Name;
        _editingValue = currentValue;
        _focusedRowIndex = rowIndex;
        _focusedColIndex = colIndex;
        FocusedKey = rowKey;

        // Set up cell-level validation
        _editValidation = new ValidationContext();
        _editValidation.RegisterField(col.Name);
        _editValidation.SetInitialValue(col.Name, currentValue);

        _editingVersion++;
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>Update the pending value during editing.</summary>
    /// <summary>Update the pending value during editing. Does NOT trigger a re-render —
    /// the editor control manages its own visual state. The value is stored for
    /// later use by CommitEdit.</summary>
    public void UpdateEditingValue(object? value)
    {
        if (!IsEditing) return;
        _editingValue = value;
        _editingVersion++;

        // Run cell-level validation
        if (_editValidation is not null && _editingColumnName is not null)
        {
            ValidateField(_editingColumnName, value);
        }
        // No StateChanged here — the editor handles its own display.
        // Re-render only on BeginEdit/CommitEdit/CancelEdit.
    }

    /// <summary>
    /// Commit the current edit. Applies SetValue to produce the new item,
    /// updates the loaded items list, and returns the (row key, new item) for async commit.
    /// Returns null if no edit is active.
    /// In row-edit mode this delegates to <see cref="CommitRowEdit"/> — mirroring
    /// <see cref="CancelEdit"/>'s delegation to <see cref="CancelRowEdit"/>.
    /// </summary>
    public (RowKey Key, T NewItem)? CommitEdit()
    {
        // Row mode keeps its pending values in _rowEditValues and leaves _editingColumnName null
        // (the "row mode" signal) while IsEditing is still true, so the single-cell path below
        // would commit with a null column name. Delegate instead, so EVERY caller — keyboard,
        // row-pointer click, the blur safety-net, user code — lands on the right path. (#853)
        if (_isRowEditing) return CommitRowEdit();
        if (!IsEditing) return null;

        // Block commit if there are validation errors
        if (_editValidation is not null && !_editValidation.IsValid())
            return null;

        var rowKey = _editingRowKey!.Value;
        var colName = _editingColumnName!;
        var newValue = _editingValue;

        // Find the row and column
        var rowIndex = GetRowIndex(rowKey);
        if (rowIndex < 0) { CancelEdit(); return null; }

        var col = _columnIndexByName.TryGetValue(colName, out var colIdx) ? _columns[colIdx] : null;
        if (col?.SetValue is null) { CancelEdit(); return null; }

        var item = GetItemAt(rowIndex);
        if (item is null) { CancelEdit(); return null; }

        // Apply return-new-owner SetValue
        var newOwner = col.SetValue(item!, newValue);
        var newItem = (T)newOwner;

        // Update in-memory state
        if (_cache is not null || _hookResource is not null)
        {
            _mutations[rowIndex] = newItem;
            _keyOverrides[rowIndex] = _source.GetRowKey(newItem).Value;
        }
        else
        {
            _loadedItems[rowIndex] = newItem;
            _rowKeyCache[rowIndex] = _source.GetRowKey(newItem).Value;
        }

        // Clear editing state
        var savedKey = rowKey;
        _editingRowKey = null;
        _editingColumnName = null;
        _editingValue = null;
        _editValidation = null;
        _editingVersion++;
        StateChanged?.Invoke();

        return (savedKey, newItem);
    }

    /// <summary>Cancel the current edit, discarding pending changes.</summary>
    public void CancelEdit()
    {
        if (_isRowEditing) { CancelRowEdit(); return; }
        if (!IsEditing) return;

        _editingRowKey = null;
        _editingColumnName = null;
        _editingValue = null;
        _editValidation = null;
        _editingVersion++;
        StateChanged?.Invoke();
    }

    /// <summary>Commit the current edit and move focus to the next cell. Starts editing if the next cell is editable.</summary>
    public (RowKey Key, T NewItem)? CommitAndMoveNext()
    {
        var result = CommitEdit();
        FocusNextCell();
        return result;
    }

    // ── Row-mode editing ────────────────────────────────────────

    /// <summary>
    /// Begin editing an entire row. All editable columns switch to editors simultaneously.
    /// Returns false if the row index is invalid or there are no editable columns.
    /// </summary>
    public bool BeginRowEdit(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= ItemCount) return false;

        var item = GetItemAt(rowIndex);
        if (item is null) return false;
        var keyStr = GetRowKeyAt(rowIndex);
        if (keyStr is null) return false;
        var rowKey = new RowKey(keyStr);

        // Snapshot current values for all editable columns
        var values = new Dictionary<string, object?>();
        foreach (var col in _columns)
        {
            if (!col.IsReadOnly && col.SetValue is not null)
                values[col.Name] = col.GetValue(item!);
        }

        if (values.Count == 0) return false;

        _editingRowKey = rowKey;
        _editingColumnName = null; // null signals row mode
        _editingValue = null;
        _rowEditValues = values;
        _isRowEditing = true;
        _focusedRowIndex = rowIndex;
        FocusedKey = rowKey;

        // Set up row-level validation
        _editValidation = new ValidationContext();
        foreach (var (colName, colValue) in values)
        {
            _editValidation.RegisterField(colName);
            _editValidation.SetInitialValue(colName, colValue);
        }

        _editingVersion++;
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>Update a pending column value during row editing.</summary>
    public void UpdateRowEditValue(string columnName, object? value)
    {
        if (!_isRowEditing || _rowEditValues is null) return;
        _rowEditValues[columnName] = value;
        _editingVersion++;

        // Run column-level validation
        ValidateField(columnName, value);
    }

    /// <summary>
    /// Commit the entire row edit. Applies all pending SetValue calls to produce
    /// the new item. Returns the (row key, new item) for async commit.
    /// </summary>
    public (RowKey Key, T NewItem)? CommitRowEdit()
    {
        if (!_isRowEditing || _rowEditValues is null) return null;

        // Block commit if there are validation errors
        if (_editValidation is not null && !_editValidation.IsValid())
            return null;

        var rowKey = _editingRowKey!.Value;
        var rowIndex = GetRowIndex(rowKey);
        if (rowIndex < 0) { CancelRowEdit(); return null; }

        var item = GetItemAt(rowIndex);
        if (item is null) { CancelRowEdit(); return null; }
        var current = item!;

        // Apply all pending values via return-new-owner SetValue
        foreach (var (colName, newValue) in _rowEditValues)
        {
            var col = _columnIndexByName.TryGetValue(colName, out var colIdx) ? _columns[colIdx] : null;
            if (col?.SetValue is null) continue;
            current = (T)col.SetValue(current, newValue);
        }

        // Update in-memory state
        if (_cache is not null || _hookResource is not null)
        {
            _mutations[rowIndex] = current;
            _keyOverrides[rowIndex] = _source.GetRowKey(current).Value;
        }
        else
        {
            _loadedItems[rowIndex] = current;
            _rowKeyCache[rowIndex] = _source.GetRowKey(current).Value;
        }

        // Clear row editing state
        var savedKey = rowKey;
        _editingRowKey = null;
        _editingColumnName = null;
        _editingValue = null;
        _rowEditValues = null;
        _isRowEditing = false;
        _editingVersion++;
        StateChanged?.Invoke();

        return (savedKey, current);
    }

    /// <summary>Cancel the row edit, discarding all pending changes.</summary>
    public void CancelRowEdit()
    {
        if (!_isRowEditing) return;

        _editingRowKey = null;
        _editingColumnName = null;
        _editingValue = null;
        _rowEditValues = null;
        _isRowEditing = false;
        _editValidation = null;
        _editingVersion++;
        StateChanged?.Invoke();
    }

    /// <summary>Check if a specific column is being edited in row mode.</summary>
    public bool IsColumnInRowEdit(RowKey rowKey, string columnName)
        => _isRowEditing && _editingRowKey?.Equals(rowKey) == true
           && _rowEditValues?.ContainsKey(columnName) == true;

    // ── Async commit lifecycle ──────────────────────────────────

    /// <summary>
    /// Begin an async commit for a row. Stores the original item for potential revert.
    /// Call after CommitEdit/CommitRowEdit to mark the row as committing.
    /// </summary>
    public void BeginAsyncCommit(RowKey key, T originalItem)
    {
        _pendingCommitOriginals[key] = originalItem;
        _committingRows.Add(key);
        _commitErrors.Remove(key);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Mark an async commit as successfully completed. Clears the pending state.
    /// </summary>
    public void CompleteAsyncCommit(RowKey key)
    {
        _pendingCommitOriginals.Remove(key);
        _committingRows.Remove(key);
        _commitErrors.Remove(key);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Mark an async commit as failed. Reverts the item to its pre-edit state
    /// and stores the error message.
    /// </summary>
    public void FailAsyncCommit(RowKey key, string errorMessage)
    {
        _committingRows.Remove(key);
        _commitErrors[key] = errorMessage;

        // Revert the optimistic update
        if (_pendingCommitOriginals.TryGetValue(key, out var original))
        {
            var rowIndex = GetRowIndex(key);
            if (rowIndex >= 0)
            {
                if (_cache is not null || _hookResource is not null)
                {
                    _mutations[rowIndex] = original;
                    _keyOverrides[rowIndex] = _source.GetRowKey(original).Value;
                }
                else
                {
                    _loadedItems[rowIndex] = original;
                    _rowKeyCache[rowIndex] = _source.GetRowKey(original).Value;
                }
            }
            _pendingCommitOriginals.Remove(key);
        }

        StateChanged?.Invoke();
    }

    /// <summary>Dismiss the error for a specific row.</summary>
    public void DismissCommitError(RowKey key)
    {
        if (_commitErrors.Remove(key))
            StateChanged?.Invoke();
    }

    // ── Validation ──────────────────────────────────────────────

    /// <summary>
    /// Run synchronous validators for a field and update the validation context.
    /// Called automatically when editing values change.
    /// </summary>
    private void ValidateField(string fieldName, object? value)
    {
        if (_editValidation is null) return;

        var col = _columnIndexByName.TryGetValue(fieldName, out var fieldIdx) ? _columns[fieldIdx] : null;
        if (col is null) return;

        _editValidation.ClearInternal(fieldName);
        _editValidation.NotifyValueChanged(fieldName, value);

        if (col.Validators is { Count: > 0 })
        {
            foreach (var validator in col.Validators)
            {
                var msg = validator.Validate(value, fieldName);
                if (msg is not null)
                    _editValidation.Add(msg);
            }
        }
    }

    /// <summary>
    /// Run async validators for a field. Returns when all validators complete.
    /// </summary>
    public async Task ValidateFieldAsync(string fieldName, object? value, CancellationToken cancellationToken = default)
    {
        if (_editValidation is null) return;

        var col = _columnIndexByName.TryGetValue(fieldName, out var fieldIdx) ? _columns[fieldIdx] : null;
        if (col?.AsyncValidators is not { Count: > 0 }) return;

        foreach (var validator in col.AsyncValidators)
        {
            var msg = await validator.ValidateAsync(value, fieldName, cancellationToken);
            if (msg is not null)
                _editValidation.Add(msg);
        }
    }

    /// <summary>Get validation messages for a specific field in the current edit.</summary>
    public IReadOnlyList<ValidationMessage> GetValidationMessages(string fieldName)
        => _editValidation?.GetMessages(fieldName) ?? Array.Empty<ValidationMessage>();

    /// <summary>Get all validation messages for the current edit.</summary>
    public IReadOnlyList<ValidationMessage> GetAllValidationMessages()
        => _editValidation?.GetAllMessages() ?? Array.Empty<ValidationMessage>();

    // ── Data loading ─────────────────────────────────────────────

    /// <summary>Load data from the source using current sort/filter state.</summary>
    public async Task LoadDataAsync(CancellationToken cancellationToken = default)
    {
        // Hook-based paging owns loading through UseInfiniteResource — the grid just
        // passes sort/filter state into the hook's deps. This method becomes a no-op.
        if (_hookResource is not null) return;

        _isLoading = true;
        StateChanged?.Invoke();

        try
        {
            var caps = _source.Capabilities;
            var serverSort = caps.HasFlag(DataSourceCapabilities.ServerSort);
            var serverFilter = caps.HasFlag(DataSourceCapabilities.ServerFilter);
            var serverSearch = caps.HasFlag(DataSourceCapabilities.ServerSearch);

            var needsClientSort = !serverSort && _sorts.Count > 0;
            var needsClientFilter = !serverFilter && _filters.Count > 0;

            if (needsClientSort || needsClientFilter)
            {
                // Client-side fallback: source can't sort/filter server-side,
                // so we must load all rows and apply locally.
                _cache = null;
                _mutations.Clear();
                _keyOverrides.Clear();

                // SECURITY (TASK-097): cap the unbounded "load all rows"
                // request. Without this, mounting against a source that
                // doesn't support server sort/filter triggers a SELECT * with
                // no LIMIT — OOM territory. Apps that need more rows can
                // bump MaxClientFallbackPageSize.
                var request = new DataRequest
                {
                    PageSize = MaxClientFallbackPageSize,
                    Sort = serverSort && _sorts.Count > 0 ? _sorts : null,
                    Filters = serverFilter && _filters.Count > 0 ? _filters : null,
                    SearchQuery = serverSearch ? _searchQuery : null,
                };

                var page = await _source.GetPageAsync(request, cancellationToken);
                _loadedItems = new List<T>(page.Items);
                _totalCount = page.TotalCount;

                if (needsClientFilter)
                {
                    _loadedItems = ApplyClientFilters(_loadedItems, _filters);
                    _totalCount = _loadedItems.Count;
                }

                if (needsClientSort)
                {
                    _loadedItems = ApplyClientSort(_loadedItems, _sorts);
                }

                // Pre-cache row key strings so getItemKey during scroll is a simple array lookup.
                var keys = new string[_loadedItems.Count];
                for (int i = 0; i < _loadedItems.Count; i++)
                    keys[i] = _source.GetRowKey(_loadedItems[i]).Value;
                _rowKeyCache = keys;
            }
            else
            {
                // Incremental paging: use DataPageCache for block-based fetching.
                // Only the pages needed for the visible viewport are loaded.
                if (_cache is null)
                {
                    _cache = _blockSize > 0
                        ? new DataPageCache<T>(_source, blockSize: _blockSize)
                        : new DataPageCache<T>(_source);
                    _cache.BlockLoaded += OnBlockLoaded;
                }

                var state = new DataRequest
                {
                    Sort = _sorts.Count > 0 ? _sorts : null,
                    Filters = _filters.Count > 0 ? _filters : null,
                    SearchQuery = _searchQuery,
                };
                _cache.SetState(state);
                _mutations.Clear();
                _keyOverrides.Clear();

                // Clear legacy collections — paged mode uses cache accessors.
                _loadedItems = new List<T>();
                _rowKeyCache = Array.Empty<string>();

                // Pre-fetch block 0 to get initial data + total count.
                await _cache.GetBlockAsync(0, cancellationToken);
                _totalCount = _cache.TotalCount;
            }
        }
        finally
        {
            _isLoading = false;
            StateChanged?.Invoke();
        }
    }

    private void OnBlockLoaded(int blockIndex)
    {
        // Update total count from the latest response.
        if (_cache?.TotalCount is int tc)
            _totalCount = tc;
        StateChanged?.Invoke();
    }

    // ── Client-side sort/filter fallback ─────────────────────────

#pragma warning disable IL2090 // Generic type parameter flows through without DynamicallyAccessedMembers annotation
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2090",
        Justification = "DataGrid client-side sort reflects over T's public properties by name. Reflection over T (AutoColumns / client sort+filter) is AOT-broken and skip-listed (docs/aot-support.md, issue #70); explicit Column<T,V>() definitions are the AOT path. UnconditionalSuppressMessage (not just #pragma) so ILC honors it at consumer publish.")]
    private static List<T> ApplyClientSort(List<T> items, List<SortDescriptor> sorts)
    {
        if (sorts.Count == 0 || items.Count == 0) return items;

        IOrderedEnumerable<T>? ordered = null;
        foreach (var sort in sorts)
        {
            var prop = typeof(T).GetProperty(sort.Field, global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Instance);
            if (prop is null) continue;

            if (ordered is null)
            {
                ordered = sort.Direction == SortDirection.Ascending
                    ? items.OrderBy(x => prop.GetValue(x))
                    : items.OrderByDescending(x => prop.GetValue(x));
            }
            else
            {
                ordered = sort.Direction == SortDirection.Ascending
                    ? ordered.ThenBy(x => prop.GetValue(x))
                    : ordered.ThenByDescending(x => prop.GetValue(x));
            }
        }

        return ordered?.ToList() ?? items;
    }

#pragma warning disable IL2090 // Generic type parameter flows through without DynamicallyAccessedMembers annotation
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2090",
        Justification = "DataGrid client-side filter reflects over T's public properties by name. Reflection over T (AutoColumns / client sort+filter) is AOT-broken and skip-listed (docs/aot-support.md, issue #70); explicit Column<T,V>() definitions are the AOT path. UnconditionalSuppressMessage (not just #pragma) so ILC honors it at consumer publish.")]
    private static List<T> ApplyClientFilters(List<T> items, List<FilterDescriptor> filters)
    {
        if (filters.Count == 0 || items.Count == 0) return items;

        IEnumerable<T> query = items;
        foreach (var filter in filters)
        {
            var prop = typeof(T).GetProperty(filter.Field, global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Instance);
            if (prop is null) continue;

            query = filter.Operator switch
            {
                FilterOperator.Equals => query.Where(x => Equals(prop.GetValue(x), filter.Value)),
                FilterOperator.NotEquals => query.Where(x => !Equals(prop.GetValue(x), filter.Value)),
                FilterOperator.Contains => query.Where(x => prop.GetValue(x)?.ToString()?.Contains(filter.Value?.ToString() ?? "", StringComparison.OrdinalIgnoreCase) == true),
                FilterOperator.GreaterThan => query.Where(x => x is not null && SafeCompare(prop.GetValue(x), filter.Value) > 0),
                FilterOperator.LessThan => query.Where(x => x is not null && SafeCompare(prop.GetValue(x), filter.Value) < 0),
                FilterOperator.IsNull => query.Where(x => prop.GetValue(x) is null),
                FilterOperator.IsNotNull => query.Where(x => prop.GetValue(x) is not null),
                _ => query,
            };
        }

        return query.ToList();
    }
#pragma warning restore IL2090

    private static int SafeCompare(object? a, object? b)
    {
        if (a is IComparable ca && b is not null)
        {
            try { return ca.CompareTo(Convert.ChangeType(b, a.GetType())); }
            catch { return 0; }
        }
        return 0;
    }
}
