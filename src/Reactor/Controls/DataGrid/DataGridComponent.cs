using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Xaml;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// The core DataGrid component. Composes VirtualList for the row area,
/// renders a fixed header row with sort indicators, and resolves cell
/// renderers from TypeRegistry. Supports keyboard navigation and inline editing.
///
/// Architecture: the VirtualList's renderItem callback is stored in a ref so it's
/// stable across re-renders. This prevents the LazyVStack from replacing its
/// ElementFactory, which would cause ItemsRepeater to re-realize items and
/// crash with "Cannot run layout in the middle of a collection change." Instead,
/// cells read current state from the DataGridState ref at render time. When state
/// changes, the DataGrid forceRenders, the VirtualList sees the same props (stable
/// callback, same item count), and the Reactor reconciler only updates the cells whose
/// output changed — as property updates on existing controls, not collection changes.
/// </summary>
public class DataGridComponent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] T> : Component<DataGridElement<T>>
{
    /// <summary>
    /// Input to the row-commit mutation. Bundles the row key, the post-edit item
    /// (already applied in the state's mutation overlay), and the pre-edit snapshot
    /// for revert on failure.
    /// </summary>
    private readonly record struct CommitMutationInput(RowKey Key, T NewItem, T? OriginalItem);

    // Reference-stable no-op handlers for PLACEHOLDER rows (#671). A loading placeholder has no
    // row key, so it gets these shared stable delegates instead of a per-row cached handler — the
    // original inline closures early-returned for placeholders anyway. Sharing one ref keeps
    // placeholder cells/rows on the reconciler skip path too (and avoids a per-placeholder closure).
    private static readonly Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> StableNoopTap = (_, _) => { };
    private static readonly Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> StableNoopPointer = (_, _) => { };

    public override Element Render()
    {
        var el = Props;
        var source = el.Source;
        var registry = el.Registry ?? UseMemo(() => new TypeRegistry());
        var useHookPaging = ReactorFeatureFlags.UseHookBasedPaging;

        // Resolve columns: use explicit columns from props, or auto-generate from
        // reflection. Re-resolve when explicit columns change (e.g., external state
        // affecting CellRenderers). Auto-columns are cached by UseMemo.
        var columns = el.Columns is not null
            ? el.Columns
            : UseMemo(() => Factories.AutoColumns<T>(registry, el.ColumnOverrides));

        // Create the headless state machine once and hold it in a ref.
        var stateRef = UseRef<DataGridState<T>>(null!);
        var (renderCount, forceRender) = UseReducer(0);

        if (stateRef.Current is null)
        {
            // Size blocks to comfortably fill any viewport. Use 2160px (4K height)
            // as the upper bound so block 0 covers the full screen even on large
            // displays, avoiding placeholder flicker on initial load.
            var rowH = el.RowHeight ?? el.EstimatedRowHeight;
            var blockSize = Math.Max(50, (int)Math.Ceiling(2160.0 / rowH));
            var s = new DataGridState<T>(source, columns, el.SelectionMode, blockSize);

            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            // Settle timer: fires once after scrolling pauses for 2 frames (~32ms).
            // When it fires, check if scrolling truly stopped before rendering.
            Microsoft.UI.Dispatching.DispatcherQueueTimer? settleTimer = null;
            var hasDeferredRender = false;
            if (dq is not null)
            {
                settleTimer = dq.CreateTimer();
                settleTimer.Interval = TimeSpan.FromMilliseconds(32);
                settleTimer.IsRepeating = false;
                settleTimer.Tick += (_, _) =>
                {
                    // Re-check: is scrolling still active?
                    var scrollTick = s.LastScrollTick;
                    var elapsed = scrollTick > 0
                        ? (global::System.Diagnostics.Stopwatch.GetTimestamp() - scrollTick) * 1000.0 / global::System.Diagnostics.Stopwatch.Frequency
                        : double.MaxValue;

                    if (elapsed < 48)
                    {
                        // Still scrolling — reschedule, don't render yet.
                        settleTimer.Stop();
                        settleTimer.Start();
                    }
                    else
                    {
                        hasDeferredRender = false;
                        s.RenderDispatchTick = global::System.Diagnostics.Stopwatch.GetTimestamp();
                        forceRender(n => n + 1);

                        // Re-request blocks for the final visible range. During rapid
                        // scrolling, EnsureRangeLoaded may have been called for an
                        // intermediate position, not where the user actually stopped.
                        s.EnsureRangeLoaded(s.LastVisibleFirst, s.LastVisibleLast);
                    }
                };
            }

            s.StateChanged += () =>
            {
                if (dq is not null)
                {
                    // Check if scroll is active: was there a ViewChanged within the last 100ms?
                    var scrollTick = s.LastScrollTick;
                    var elapsed = scrollTick > 0
                        ? (global::System.Diagnostics.Stopwatch.GetTimestamp() - scrollTick) * 1000.0 / global::System.Diagnostics.Stopwatch.Frequency
                        : double.MaxValue;

                    if (elapsed < 100)
                    {
                        // Scrolling is active — defer render.
                        if (!hasDeferredRender)
                        {
                            hasDeferredRender = true;
                            settleTimer!.Stop();
                            settleTimer.Start();
                        }
                        // If timer already running, let it handle it — don't restart
                        // on every StateChanged to avoid pushing the deadline out forever.
                    }
                    else
                    {
                        // Not scrolling — render on next dispatcher tick.
                        hasDeferredRender = false;
                        dq.TryEnqueue(() =>
                        {
                            s.RenderDispatchTick = global::System.Diagnostics.Stopwatch.GetTimestamp();
                            forceRender(n => n + 1);
                        });
                    }
                }
                else
                {
                    forceRender(n => n + 1);
                }
            };

            stateRef.Current = s;
        }
        var state = stateRef.Current!;

        // ── Row-commit mutation (Phase 3) ────────────────────────
        // UseMutation drives the async commit lifecycle: OnOptimistic snapshots the
        // pre-edit item (so FailAsyncCommit can revert the overlay), OnSuccess clears
        // the committing flag, OnError records the error message for the row banner.
        // This replaces the ad-hoc Task.Run + TryEnqueue path previously in HandleAsyncCommit.
        var rowChanged = el.OnRowChanged;
        var commitMutation = Context.UseMutation<CommitMutationInput, bool>(
            mutator: async (input, ct) =>
            {
                if (rowChanged is null) return true;
                await rowChanged(input.Key, input.NewItem).ConfigureAwait(false);
                return true;
            },
            options: new MutationOptions<CommitMutationInput, bool>(
                OnOptimistic: input => state.BeginAsyncCommit(input.Key, input.OriginalItem!),
                OnSuccess: (_, input) => state.CompleteAsyncCommit(input.Key),
                OnError: (ex, input) => state.FailAsyncCommit(input.Key, ex.Message)));

        // Route HandleAsyncCommit through the UseMutation handle. The mutation state
        // persists across renders so overlapping RunAsync calls from rapid commits
        // all land on the same pending-count / LastResult machinery.
        state.CommitDispatcher = rowChanged is null
            ? null
            : (key, newItem, origItem) => _ = commitMutation.RunAsync(new CommitMutationInput(key, newItem, origItem));

        // ── Hook-based paging (Phase 3) ──────────────────────────
        // Under ReactorFeatureFlags.UseHookBasedPaging, data loading flows through
        // UseInfiniteResource / UseDataSource instead of DataGridState.LoadDataAsync.
        // The hook owns fetch lifecycle, cache subscriptions, and deps-change restart.
        if (useHookPaging)
        {
            // DataRequest is rebuilt each render so sort/filter/search changes flow into
            // the hook's deps and restart pagination cleanly.
            var rowH = el.RowHeight ?? el.EstimatedRowHeight;
            var pageSize = Math.Max(50, (int)Math.Ceiling(2160.0 / rowH));

            // Memoize the DataRequest on the sort/filter/search/pageSize identity. Rebuilding it
            // every render allocated two fresh List copies (Sort/Filters) whose references then
            // changed UseDataSource's deps every render, restarting pagination. Keyed on the
            // version counters, the request and its list references stay stable until the
            // underlying sort/filter/search actually change.
            var request = UseMemo(() => new DataRequest
            {
                PageSize = pageSize,
                Sort = state.Sorts.Count > 0 ? state.Sorts.ToList() : null,
                Filters = state.Filters.Count > 0 ? state.Filters.ToList() : null,
                SearchQuery = state.SearchQuery,
            }, state.SortVersion, state.FilterVersion, state.SearchQuery ?? string.Empty, pageSize);

            var resource = UseDataSource(
                source,
                request,
                options: new InfiniteResourceOptions(PageSize: pageSize));

            // Attach (or re-attach on deps-change) the latest resource reference. The
            // state reads data from this resource in its accessors when set.
            state.SetHookResource(resource);
        }

        // Subscribe to observable data sources (e.g. ObservableListDataSource)
        // so the grid refreshes when items are added, removed, or modified via INPC.
        // Cancel any active edit first — the underlying data changed externally.
        UseEffect(() =>
        {
            if (source is IObservableDataSource<T> observable)
            {
                void OnDataChanged(object? sender, EventArgs e)
                {
                    if (state.IsEditing || state.IsRowEditing)
                        state.CancelEdit();
                    if (useHookPaging)
                        state.HookResource?.Refresh();
                    else
                        _ = state.LoadDataAsync();
                }
                observable.DataChanged += OnDataChanged;
                return () => observable.DataChanged -= OnDataChanged;
            }
            return () => { };
        }, source);

        // Load data on mount and when sort changes (legacy path only — hook path
        // reacts to sort/filter changes through its own deps).
        // Memoize the sort key so the Select + interpolation + Join only runs when sorts change,
        // not on every render. It feeds the LoadDataAsync effect's deps below.
        var sortKey = UseMemo(
            () => string.Join(",", state.Sorts.Select(s => $"{s.Field}:{s.Direction}")),
            state.SortVersion);

        UseEffect(() =>
        {
            if (!useHookPaging)
                _ = state.LoadDataAsync();
        }, sortKey);

        // Notify selection changes via effect (not during render)
        var selVersion = UseRef(0);
        var currentSelVersion = state.SelectionVersion;
        UseEffect(() =>
        {
            if (el.OnSelectionChanged is not null && selVersion.Current != currentSelVersion)
            {
                selVersion.Current = currentSelVersion;
                el.OnSelectionChanged(new HashSet<RowKey>(state.SelectedKeys));
            }
        }, currentSelVersion);

        var itemCount = state.ItemCount;

        // Stable identity for the search box's onChanged delegate. Declared unconditionally here so
        // the hook order is fixed even though the search box is conditional (ShowSearch can toggle);
        // built lazily where the search box is rendered below.
        var onSearchRef = UseRef<Action<string>?>(null);

        // ── Build the UI ────────────────────────────────────────────
        // Use a WinUI Grid instead of FlexColumn for the DataGrid root container.
        // This breaks the FlexPanel ancestor chain so header column width changes
        // don't cascade Yoga re-layout up through every parent FlexPanel.
        var gridChildren = new List<Element?>();
        int gridRow = 0;

        // Search bar
        if (el.ShowSearch)
        {
            var searchQuery = state.SearchQuery ?? "";
            // Cache the onChanged delegate so it keeps a stable identity across renders. It only
            // captures the stable `state`, so a once-built handler is equivalent to rebuilding it
            // every render — this drops a per-render closure allocation and lets the search box reuse
            // its control on the reconciler's skip path instead of re-running Update.
            onSearchRef.Current ??= q =>
            {
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
                {
                    state.SetSearchQuery(q);
                    _ = state.LoadDataAsync();
                });
            };
            gridChildren.Add(
                TextBox(searchQuery, onSearchRef.Current)
                    .Padding(horizontal: 8, vertical: 4).Grid(row: gridRow, column: 0)
            );
            gridRow++;
        }

        if (el.ShowHeaders)
        {
            gridChildren.Add(RenderHeaderRow(state, columns, el).Grid(row: gridRow, column: 0));
            gridRow++;
        }

        if (state.TotalCount is not null)
        {
            gridChildren.Add(
                TextBlock($"{state.TotalCount:N0} items").Opacity(0.5).FontSize(12)
                    .Padding(8, 2, 8, 2).Grid(row: gridRow, column: 0)
            );
            gridRow++;
        }

        // Surface hook-path fetch errors directly — otherwise a failed page 0 just
        // collapses into the empty template and users have no idea why their grid is
        // blank. Legacy path never had this affordance because LoadDataAsync swallowed
        // exceptions too; this fills both gaps.
        Exception? loadError = null;
        if (useHookPaging && state.HookResource?.LoadState is LoadState.Error err)
            loadError = err.Exception;

        Element dataContent;
        if (loadError is not null && itemCount == 0)
        {
            dataContent = RenderDefaultError(loadError);
        }
        else if (state.IsLoading && itemCount == 0)
        {
            dataContent = el.LoadingTemplate ?? RenderDefaultLoading();
        }
        else if (itemCount == 0)
        {
            dataContent = el.EmptyTemplate ?? RenderDefaultEmpty();
        }
        else
        {
            dataContent = RenderDataRows(state, columns, el, registry);
        }
        gridChildren.Add(dataContent.Grid(row: gridRow, column: 0));

        // Root grid: one "*" column, with `gridRow` leading "Auto" rows (search / headers / count)
        // and a final "*" row for the data area. gridRow is 0..3, so the row-def arrays and their
        // GridDefinitions are precomputed once (RootGridDefCache) instead of allocating a string[]
        // + GridDefinition every render. The stable definition reference also lets the reconciler
        // skip re-applying the root grid's row/column definitions when the row count is unchanged.
        var rootDef = gridRow < RootGridDefCache.Length
            ? RootGridDefCache[gridRow]
            : new GridDefinition(RootColsStar, BuildRootRowDefs(gridRow));
        var gridEl = new GridElement(rootDef, FilterRowChildren(gridChildren.ToArray()));

        // Commit active edit when focus leaves the DataGrid entirely.
        // Attached once at mount via Setters; the handler reads current state from the ref.
        // Hooks must run unconditionally and in the same order every render, so both refs are
        // declared outside the el.Editable branch (Editable can toggle between renders, which
        // would otherwise change the hook call sequence and throw HookOrderException).
        var lostFocusWired = UseRef(false);
        var lostFocusSetters = UseRef<Action<global::Microsoft.UI.Xaml.Controls.Grid>[]?>(null);
        if (el.Editable)
        {
            // Cache the LostFocus setter array (and its closure) in a ref so the spread + lambda
            // aren't re-allocated every render. The handler wires g.LostFocus exactly once (guarded
            // by lostFocusWired) and reads live state through the captured refs, so a once-built
            // setter is equivalent to rebuilding it each render. gridEl is freshly constructed
            // above with no setters, so assigning Setters here matches the old spread.
            lostFocusSetters.Current ??= new Action<global::Microsoft.UI.Xaml.Controls.Grid>[]
            {
                g =>
                {
                    if (lostFocusWired.Current) return;
                    lostFocusWired.Current = true;
                    g.LostFocus += (sender, e) =>
                    {
                        // Consume the one-shot editing-Tab guard synchronously here, before the IsEditing
                        // guard. A keyboard editing-Tab owns this focus-out: it already committed the
                        // current cell and, when the next cell is editable, reopened the editor there — so
                        // skip the safety-net commit. Consuming here (not in the deferred tick) is essential:
                        // when the Tab lands on a NON-editable cell the reopen fails and IsEditing is already
                        // false by the time this fires, so the guard below would short-circuit and never
                        // schedule the tick — leaving the flag set to wrongly suppress a later legitimate
                        // blur-commit (lost edit). The flag is set synchronously in the KeyDown handler,
                        // before this LostFocus fires.
                        if (state.SuppressNextLostFocusCommit)
                        {
                            state.SuppressNextLostFocusCommit = false;
                            return;
                        }
                        if (!state.IsEditing && !state.IsRowEditing) return;
                        // Defer the entire check to the next tick. During DOM transitions
                        // (e.g., cell switching from TextBlock to TextBox), the old element
                        // fires LostFocus before the new element receives GotFocus. Checking
                        // synchronously would falsely conclude that focus left the grid.
                        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
                        {
                            if (!state.IsEditing && !state.IsRowEditing) return;
                            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(g.XamlRoot);
                            if (focused is DependencyObject dep)
                            {
                                var parent = dep;
                                while (parent is not null)
                                {
                                    if (ReferenceEquals(parent, g)) return;
                                    parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
                                }
                            }
                            if (state.IsRowEditing)
                            {
                                var origItem = state.EditingRowKey is not null
                                    ? GetOriginalItem(state, state.EditingRowKey.Value) : default;
                                var result = state.CommitRowEdit();
                                if (result is not null && el.OnRowChanged is not null)
                                    HandleAsyncCommit(state, el, result.Value.Key, result.Value.NewItem, origItem!);
                            }
                            else if (state.IsEditing)
                            {
                                var editKey = state.EditingRowKey;
                                var origItem = editKey is not null ? GetOriginalItem(state, editKey.Value) : default;
                                var result = state.CommitEdit();
                                if (result is not null && el.OnRowChanged is not null)
                                    HandleAsyncCommit(state, el, result.Value.Key, result.Value.NewItem, origItem!);
                            }
                        });
                    };
                }
            };
            gridEl = gridEl with { Setters = lostFocusSetters.Current };
        }

        Element grid = gridEl;

        // Keyboard navigation handler.
        // Use a ref to hold the current props so the OnMount handler (registered once)
        // always reads the latest values.
        var elRef = UseRef(el);
        elRef.Current = el;

        // Register the KeyDown handler with handledEventsToo: true via OnMount.
        // This is critical because WinUI's FocusManager processes Tab for focus
        // navigation and marks the event as handled BEFORE normal KeyDown handlers
        // fire. Without handledEventsToo, Tab never reaches the DataGrid when the
        // user presses Tab inside an editing TextBox.
        grid = grid
            .IsTabStop(true)
            .OnMount(fe =>
            {
                fe.AddHandler(
                    UIElement.KeyDownEvent,
                    new Microsoft.UI.Xaml.Input.KeyEventHandler((sender, e) =>
                    {
                        var currentEl = elRef.Current;
                        if (ShouldHandleKey(state, currentEl, e.Key))
                        {
                            e.Handled = true;
                            var capturedKey = e.Key;
                            // An editing-Tab moves real focus out of the single-tab-stop grid, which fires
                            // the grid's deferred LostFocus commit. But the editing-Tab path (HandleKeyDown)
                            // itself commits the current cell and reopens the editor on the next cell, so the
                            // LostFocus safety-net must NOT also commit — that would tear down the just-reopened
                            // editor. Claim that one focus-out here, synchronously (before either handler
                            // defers), so the guard is robust to dispatcher ordering.
                            if (capturedKey == VirtualKey.Tab && state.IsEditing)
                                state.SuppressNextLostFocusCommit = true;
                            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
                            {
                                HandleKeyDown(state, currentEl, capturedKey);
                            });
                        }
                    }),
                    true); // handledEventsToo — receive Tab even after FocusManager handles it
            });

        return grid;
    }

    // ── Data rows ────────────────────────────────────────────────────

    private static Element RenderDataRows(
        DataGridState<T> state,
        IReadOnlyList<FieldDescriptor> columns,
        DataGridElement<T> el,
        TypeRegistry registry)
    {
        var totalItems = state.ItemCount;
        var selectable = el.SelectionMode != SelectionMode.None;
        var editable = el.Editable;

        // Cache the per-column pixel widths + the shared GridDefinition in state. Rebuilt only when
        // a column changes (see DataGridState.GetColumnLayout) — this eliminates the per-render
        // double[] + string[] + per-column double.ToString. The header row and every data row reuse
        // the same GridDefinition reference, so the reconciler skips re-applying its
        // ColumnDefinitions each render.
        var hasRowDetailTemplate = el.RowDetailTemplate is not null;
        var hasRowEditActions = editable && el.EditMode == EditMode.Row;
        var (colWidths, gridDef) = state.GetColumnLayout(
            columns, hasRowDetailTemplate, selectable, hasRowEditActions);

        return VirtualList(
            itemCount: totalItems,
            renderItem: index =>
            {
                return RenderRow(index, state, columns, el, registry, colWidths, gridDef);
            },
            itemHeight: el.RowHeight,
            estimatedItemHeight: el.EstimatedRowHeight,
            spacing: 0,
            getItemKey: index =>
            {
                return state.GetRowKeyAt(index) ?? index.ToString();
            },
            @ref: vlRef =>
            {
                // Wire up the scroll guard on the factory so RefreshRealizedItems
                // can bail out if scrolling restarted after forceRender was dispatched.
                if (vlRef.Repeater?.ItemTemplate is Core.ElementFactory<int> factory)
                {
                    factory.ShouldSkipRefresh ??= () =>
                    {
                        return state.LastScrollTick > state.RenderDispatchTick
                               && state.RenderDispatchTick > 0;
                    };
                }
            },
            onVisibleRangeChanged: (first, last) =>
            {
                // Stamp scroll activity so StateChanged can defer re-renders.
                state.LastScrollTick = global::System.Diagnostics.Stopwatch.GetTimestamp();
                state.LastVisibleFirst = first;
                state.LastVisibleLast = last;

                // Prefetch blocks that are about to enter the viewport.
                // This triggers async loads; when they complete, ItemCount
                // grows and new items are realized with real data.
                state.EnsureRangeLoaded(first, last);
            }
        ).Flex(grow: 1);
    }

    private static Element RenderRow(
        int index,
        DataGridState<T> state,
        IReadOnlyList<FieldDescriptor> columns,
        DataGridElement<T> el,
        TypeRegistry registry,
        double[] colWidths,
        GridDefinition gridDef)
    {
        var item = state.GetItemAt(index);
        var keyStr = state.GetRowKeyAt(index);
        var isPlaceholder = item is null || keyStr is null;

        var rowKey = isPlaceholder ? default : new RowKey(keyStr!);
        var selectable = el.SelectionMode != SelectionMode.None;
        var editable = el.Editable && !isPlaceholder;
        var colCount = columns.Count;
        var isSelected = !isPlaceholder && selectable && state.IsSelected(rowKey);
        var isRowFocused = !isPlaceholder && index == state.FocusedRowIndex;

        var hasRowDetailTemplate = el.RowDetailTemplate is not null;
        var hasRowEditActions = editable && el.EditMode == EditMode.Row;
        var expandOffset = hasRowDetailTemplate ? 1 : 0;
        var cellOffset = expandOffset + (selectable ? 1 : 0);
        var cells = new Element?[colCount + cellOffset + (hasRowEditActions ? 1 : 0)];

        // Expand/collapse toggle — embedded in the Grid as column 0.
        // Avoids wrapping every row in a FlexRow (which adds Yoga layout overhead).
        if (hasRowDetailTemplate)
        {
            var isExpanded = !isPlaceholder && state.IsExpanded(rowKey);
            var expandIcon = isExpanded ? "\u25BC" : "\u25B6";
            cells[0] = TextBlock(expandIcon)
                .FontSize(10).Opacity(0.6)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
                // #671: reference-stable expand handler (cached per rowKey) so the toggle cell can
                // skip across renders. Placeholders (no key) share the stable no-op.
                .OnTapped(isPlaceholder ? StableNoopTap : state.GetExpandHandler(rowKey))
                .Grid(row: 0, column: 0);
        }

        if (selectable)
        {
            cells[expandOffset] = TextBlock(isSelected ? "\u2713" : "")
                .FontSize(12)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
                .Grid(row: 0, column: expandOffset);
        }

        var isRowInRowEdit = !isPlaceholder && state.IsRowEditing && state.EditingRowKey?.Equals(rowKey) == true;

        for (int c = 0; c < colCount; c++)
        {
            var col = columns[c];
            var value = isPlaceholder ? null : col.GetValue(item!);
            var isCellFocused = isRowFocused && c == state.FocusedColIndex;
            var isCellEditing = !isPlaceholder && !isRowInRowEdit
                                && state.EditingRowKey?.Equals(rowKey) == true
                                && state.EditingColumnName == col.Name;
            var isColInRowEdit = isRowInRowEdit && state.IsColumnInRowEdit(rowKey, col.Name);

            Element cellContent;
            if (isCellEditing)
            {
                cellContent = RenderEditingCell(col, state, registry);
            }
            else if (isColInRowEdit)
            {
                cellContent = RenderRowEditingCell(col, state, registry);
            }
            else if (!isPlaceholder && el.CellTemplate is not null)
            {
                cellContent = el.CellTemplate(new CellContext<T>(item!, rowKey, col, value, false, _ => { }));
            }
            else if (isPlaceholder)
            {
                // Placeholder cell: use custom template or default shimmer bar.
                // Must produce the same element TYPE as RenderCell (a Text with Padding)
                // so RefreshRealizedItems can patch properties without structural changes.
                cellContent = el.PlaceholderCellTemplate is not null
                    ? el.PlaceholderCellTemplate(col, colWidths[c])
                    : RenderDefaultPlaceholderCell(col, colWidths[c]);
            }
            else
            {
                cellContent = RenderCell(col, value, registry);

                // Highlight cell if it matches the search query
                if (state.SearchQuery is not null && value is not null)
                {
                    var valueStr = value.ToString() ?? "";
                    if (valueStr.Contains(state.SearchQuery, StringComparison.OrdinalIgnoreCase))
                        cellContent = cellContent.Background(SystemAttentionBackground);
                }
            }

            var cell = cellContent.VAlign(VerticalAlignment.Center);

            // Validation error indicator — red border when field has errors
            var hasValidationError = (isCellEditing || isColInRowEdit)
                                     && state.EditValidation is not null
                                     && state.EditValidation.HasError(col.Name);
            if (hasValidationError)
            {
                cell = cell.WithBorder(SystemCritical, 2);
            }
            // Focus indicator — property change only, no structural change
            else if (isCellFocused && !isCellEditing && !isColInRowEdit)
            {
                cell = cell.WithBorder(Accent, 2);
            }

            // Click to edit (deferred) — only for Cell edit mode
            if (editable && el.EditMode == EditMode.Cell
                && !isCellEditing && !isRowInRowEdit
                && !col.IsReadOnly && col.SetValue is not null)
            {
                // #671: reference-stable click-to-edit handler (cached per (rowKey, column)). It
                // resolves the live row + column index at click time, so it always edits the right
                // cell after a mutation/sort, while its stable identity lets unchanged cells skip.
                cell = cell.OnTapped(state.GetCellEditHandler(rowKey, col.Name));
            }

            cells[c + cellOffset] = cell.Grid(row: 0, column: c + cellOffset);
        }

        // Row-mode edit actions column: Edit button or Save/Cancel
        if (hasRowEditActions)
        {
            var actionsCol = colCount + cellOffset;
            if (isRowInRowEdit)
            {
                var capturedIdx = index;
                cells[actionsCol] = FlexRow(
                    Button("Save", () =>
                    {
                        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
                        {
                            var editKey = state.EditingRowKey;
                            var origItem = editKey is not null ? GetOriginalItem(state, editKey.Value) : default;
                            var result = state.CommitRowEdit();
                            if (result is not null && el.OnRowChanged is not null)
                                HandleAsyncCommit(state, el, result.Value.Key, result.Value.NewItem, origItem!);
                        });
                    }).Padding(2),
                    Button("Cancel", () =>
                    {
                        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
                        {
                            state.CancelRowEdit();
                        });
                    }).Padding(2)
                ).VAlign(VerticalAlignment.Center).Grid(row: 0, column: actionsCol);
            }
            else
            {
                var capturedIdx = index;
                cells[actionsCol] = Button("Edit", () =>
                {
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
                    {
                        state.BeginRowEdit(capturedIdx);
                    });
                }).VAlign(VerticalAlignment.Center).Padding(horizontal: 2, vertical: 4).Grid(row: 0, column: actionsCol);
            }
        }

        var rowBg = isSelected ? SubtleFill
            : isRowFocused ? ControlFillSecondary
            : (index % 2 == 0 ? LayerFill : CardBackground);
        // Use a WinUI Grid with pixel column definitions instead of FlexRow.
        // Grid with pixel columns avoids Yoga layout entirely — the dominant
        // cost identified by profiling. Construct it directly with the cached, shared
        // GridDefinition (FilterRowChildren mirrors the Grid(...) factory's child handling) so the
        // reconciler reuses the definition reference and skips rebuilding ColumnDefinitions.
        Element row = new GridElement(gridDef, FilterRowChildren(cells));
        row = row.Background(rowBg);

        // Click handler — always present (maintains element tree structure).
        // #671: reference-stable per-row pointer handler (cached per rowKey) so unchanged rows can
        // skip across renders. It resolves the live row index at click time (correct after a
        // mutation/sort), commits an in-flight edit on a different row, focuses, and runs selection.
        // Placeholders (no key) share the stable no-op — the original handler early-returned for them.
        row = row.OnPointerPressed(isPlaceholder ? StableNoopPointer : state.GetRowPointerHandler(rowKey));

        // Per-row validation visualizer — always evaluate (never wraps for placeholders
        // since isEditingThisRow is false, so the element tree stays flat).
        var isEditingThisRow = !isPlaceholder && state.EditingRowKey?.Equals(rowKey) == true;
        if (isEditingThisRow && state.HasValidationErrors)
        {
            var messages = state.GetAllValidationMessages();
            var errorTexts = messages
                .Where(m => m.Severity == Validation.Severity.Error)
                .Select(m => m.Text);
            var errorSummary = string.Join("; ", errorTexts);

            row = FlexColumn(
                row,
                TextBlock(errorSummary).Foreground(SystemCritical).FontSize(11).Padding(horizontal: 8, vertical: 2)
            );
        }

        // Async commit: loading indicator during commit
        if (!isPlaceholder && state.IsCommitting(rowKey))
        {
            row = row.Opacity(0.6);
        }

        // Async commit: error display after failed commit
        var commitError = isPlaceholder ? null : state.GetCommitError(rowKey);
        if (commitError is not null)
        {
            var capturedKey = rowKey;
            row = FlexColumn(
                row,
                FlexRow(
                    TextBlock(commitError).Foreground(SystemCritical).FontSize(11).Flex(grow: 1),
                    Button("Dismiss", () =>
                    {
                        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
                        {
                            state.DismissCommitError(capturedKey);
                        });
                    })
                ).Padding(horizontal: 8, vertical: 2)
            );
        }

        // Row detail expansion — expand icon is already in the Grid (column 0).
        // Only wrap in FlexColumn when the row IS expanded (to show the detail pane).
        if (el.RowDetailTemplate is not null)
        {
            var isExpanded = !isPlaceholder && state.IsExpanded(rowKey);
            if (isExpanded)
            {
                var detail = el.RowDetailTemplate(item!, rowKey).Padding(horizontal: 16, vertical: 8);
                row = FlexColumn(
                    row,
                    detail.Background(CardBackground)
                );
            }
        }

        return row;
    }

    // ── Cell rendering ──────────────────────────────────────────────

    // Shared cell padding: left, top, right, bottom. The extra right padding
    // creates a forced gutter between adjacent columns so content — including
    // right-aligned numbers and colored pills — can't visually merge into the
    // neighbor cell.
    private const double CellPadLeft = 8, CellPadTop = 4, CellPadRight = 12, CellPadBottom = 4;

    private static Element RenderCell(
        FieldDescriptor col, object? value, TypeRegistry registry)
    {
        if (col.CellRenderer is not null && value is not null)
            return col.CellRenderer(value).Padding(CellPadLeft, CellPadTop, CellPadRight, CellPadBottom);

        if (col.FormatValue is not null)
            return TextBlock(col.FormatValue(value)).Padding(CellPadLeft, CellPadTop, CellPadRight, CellPadBottom);

        var registryRenderer = registry.GetCellRenderer(col.FieldType);
        if (registryRenderer is not null && value is not null)
            return registryRenderer(value).Padding(CellPadLeft, CellPadTop, CellPadRight, CellPadBottom);

        var registryFormatter = registry.GetFormatter(col.FieldType);
        if (registryFormatter is not null)
            return TextBlock(registryFormatter(value)).Padding(CellPadLeft, CellPadTop, CellPadRight, CellPadBottom);

        if (value is bool boolVal)
            return TextBlock(boolVal ? "\u2713" : "").Padding(CellPadLeft, CellPadTop, CellPadRight, CellPadBottom);

        if (value is double d)
            return TextBlock(d.ToString("G")).Padding(CellPadLeft, CellPadTop, CellPadRight, CellPadBottom);

        if (value is float f)
            return TextBlock(f.ToString("G")).Padding(CellPadLeft, CellPadTop, CellPadRight, CellPadBottom);

        return TextBlock(value?.ToString() ?? "").Padding(CellPadLeft, CellPadTop, CellPadRight, CellPadBottom);
    }

    private static Element RenderEditingCell(
        FieldDescriptor col, DataGridState<T> state, TypeRegistry registry)
    {
        var currentValue = state.EditingValue;

        Func<object, Action<object>, Element>? editor = col.Editor;
        if (editor is null)
            editor = registry.ResolveEditor(col.FieldType, EditorTier.Compact);
        if (editor is null)
            editor = registry.ResolveEditor(col.FieldType, EditorTier.Standard);

        if (editor is not null)
            return editor(currentValue!, v => state.UpdateEditingValue(v)).Padding(2);

        return TextBox(currentValue?.ToString() ?? "", s => state.UpdateEditingValue(s)).Padding(2);
    }

    private static Element RenderRowEditingCell(
        FieldDescriptor col, DataGridState<T> state, TypeRegistry registry)
    {
        var currentValue = state.GetRowEditValue(col.Name);

        Func<object, Action<object>, Element>? editor = col.Editor;
        if (editor is null)
            editor = registry.ResolveEditor(col.FieldType, EditorTier.Compact);
        if (editor is null)
            editor = registry.ResolveEditor(col.FieldType, EditorTier.Standard);

        var colName = col.Name;
        if (editor is not null)
            return editor(currentValue!, v => state.UpdateRowEditValue(colName, v)).Padding(2);

        return TextBox(currentValue?.ToString() ?? "", s => state.UpdateRowEditValue(colName, s)).Padding(2);
    }

    // ── Header rendering ────────────────────────────────────────────

    private static Element RenderHeaderRow(
        DataGridState<T> state,
        IReadOnlyList<FieldDescriptor> columns,
        DataGridElement<T> el)
    {
        var hasRowDetailTemplate = el.RowDetailTemplate is not null;
        var selectable = el.SelectionMode != SelectionMode.None;
        var expandOffset = hasRowDetailTemplate ? 1 : 0;
        var cellOffset = expandOffset + (selectable ? 1 : 0);
        var colCount = columns.Count;

        var editable = el.Editable;
        var hasRowEditActions = editable && el.EditMode == EditMode.Row;

        // Reuse the cached column layout (shared with the data rows) so the header doesn't
        // rebuild the column-definition strings or re-run double.ToString each render.
        var (_, gridDef) = state.GetColumnLayout(
            columns, hasRowDetailTemplate, selectable, hasRowEditActions);

        var headerCells = new List<Element?>();

        if (hasRowDetailTemplate)
        {
            headerCells.Add(Border(Empty()).Grid(row: 0, column: 0));
        }

        if (selectable)
        {
            headerCells.Add(Border(Empty()).Grid(row: 0, column: expandOffset));
        }

        for (int i = 0; i < colCount; i++)
        {
            var col = columns[i];
            var sortDir = state.GetSortDirection(col.Name);
            var colName = col.Name;

            Element headerContent;
            if (el.HeaderTemplate is not null)
            {
                headerContent = el.HeaderTemplate(new HeaderContext(
                    col, sortDir,
                    () => state.ToggleSort(colName),
                    w => state.ResizeColumn(colName, w)));
            }
            else
            {
                headerContent = RenderDefaultHeader(col, sortDir, () => state.ToggleSort(colName), state, true);
            }

            if (el.AllowColumnResize || el.AllowColumnReorder)
            {
                // Overlay the header content and optional resize grip / reorder handler.
                var overlayChildren = new List<Element>
                {
                    headerContent.Grid(row: 0, column: 0)
                };

                if (el.AllowColumnResize)
                {
                    var grip = new ResizeGripElement()
                        .Width(6)
                        .HAlign(HorizontalAlignment.Right)
                        .VAlign(VerticalAlignment.Stretch)
                        .WithKey($"grip-{colName}")
                        .OnMount(fe => AttachResizeHandlers(fe, state, colName));
                    overlayChildren.Add(grip.Grid(row: 0, column: 0));
                }

                headerContent = new GridElement(ResizeOverlayDef, overlayChildren.ToArray());

                if (el.AllowColumnReorder)
                {
                    var capturedIdx = i;
                    headerContent = headerContent
                        .OnMount(fe => AttachReorderHandlers(fe, state, capturedIdx, columns, cellOffset));
                }
            }

            headerCells.Add(headerContent.Grid(row: 0, column: i + cellOffset));
        }

        if (hasRowEditActions)
        {
            headerCells.Add(
                TextBlock("").Padding(horizontal: 4, vertical: 6)
                    .Grid(row: 0, column: colCount + cellOffset));
        }

        return new GridElement(gridDef, FilterRowChildren(headerCells.ToArray()));
    }

    // Cached GridDefinition for the resize grip overlay. Using a static instance
    // ensures the reconciler sees the same reference across renders and takes the
    // fast update path (property changes only) instead of remounting the Grid.
    private static readonly GridDefinition ResizeOverlayDef = new(["*"], ["*"]);

    // Root grid layout caches (#129). The root grid always has a single "*" column and `gridRow`
    // leading "Auto" rows (optional search box / header row / total-count row) followed by a final
    // "*" data row. gridRow is 0..3, so all four row-definition arrays and their GridDefinitions
    // are built once and reused — avoiding a per-render string[] + GridDefinition allocation. The
    // stable GridDefinition reference also lets the reconciler skip re-applying the root grid's
    // row/column definitions when the optional-row count is unchanged.
    private static readonly string[] RootColsStar = ["*"];

    private static readonly string[][] RootRowDefsCache =
    [
        BuildRootRowDefs(0),
        BuildRootRowDefs(1),
        BuildRootRowDefs(2),
        BuildRootRowDefs(3),
    ];

    private static readonly GridDefinition[] RootGridDefCache =
    [
        new GridDefinition(RootColsStar, RootRowDefsCache[0]),
        new GridDefinition(RootColsStar, RootRowDefsCache[1]),
        new GridDefinition(RootColsStar, RootRowDefsCache[2]),
        new GridDefinition(RootColsStar, RootRowDefsCache[3]),
    ];

    // Builds the root grid's row definitions: `gridRow` leading "Auto" rows + a final "*" data row.
    private static string[] BuildRootRowDefs(int gridRow)
    {
        var rows = new string[gridRow + 1];
        for (int i = 0; i < gridRow; i++) rows[i] = "Auto";
        rows[gridRow] = "*";
        return rows;
    }

    // Mirrors Dsl.FilterChildren (which is private): flattens GroupElements and removes null /
    // EmptyElement children, with a fast path that aliases the input array when no expansion is
    // needed. Replicated here so the DataGrid can build GridElements directly with a cached
    // GridDefinition while preserving the exact child semantics of the Grid(...) factory.
    private static Element[] FilterRowChildren(Element?[] children)
    {
        bool needsExpansion = false;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is null or GroupElement or EmptyElement)
            {
                needsExpansion = true;
                break;
            }
        }
        if (!needsExpansion) return (Element[])(object)children;

        var result = new List<Element>();
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is GroupElement group)
            {
                foreach (var gc in group.Children)
                {
                    if (gc is not null and not EmptyElement)
                        result.Add(gc);
                }
            }
            else if (children[i] is not null and not EmptyElement)
            {
                result.Add(children[i]!);
            }
        }
        return result.ToArray();
    }

    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush TransparentBrush =
        new(Microsoft.UI.Colors.Transparent);
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush ResizeHoverBrush =
        new(Microsoft.UI.ColorHelper.FromArgb(0x40, 0x00, 0x78, 0xD4));

    /// <summary>
    /// Attaches pointer event handlers to a resize grip control for column drag resizing.
    /// Uses pointer capture so the drag remains responsive even when the pointer moves
    /// outside the grip area. Calls state.ResizeColumn() on each move, which triggers a
    /// normal Reactor re-render — the reconciler updates Width on existing controls without
    /// remounting (fast path via cached GridDefinition).
    /// </summary>
    private static void AttachResizeHandlers(
        FrameworkElement fe, DataGridState<T> state, string colName)
    {
        var grip = (ResizeGripControl)fe;
        var dragging = false;
        var startX = 0.0;
        var startWidth = 0.0;

        grip.PointerEntered += (s, _) =>
        {
            if (!dragging)
                ((ResizeGripControl)s!).Background = ResizeHoverBrush;
        };

        grip.PointerExited += (s, _) =>
        {
            if (!dragging)
                ((ResizeGripControl)s!).Background = TransparentBrush;
        };

        grip.PointerPressed += (s, e) =>
        {
            var props = e.GetCurrentPoint(null).Properties;
            if (!props.IsLeftButtonPressed) return;

            var el = (UIElement)s!;
            el.CapturePointer(e.Pointer);
            dragging = true;
            startX = e.GetCurrentPoint(null).Position.X;
            startWidth = state.GetColumnWidth(colName);
            e.Handled = true;
        };

        grip.PointerMoved += (s, e) =>
        {
            if (!dragging) return;
            var x = e.GetCurrentPoint(null).Position.X;
            state.ResizeColumn(colName, startWidth + (x - startX));
        };

        grip.PointerReleased += (s, e) =>
        {
            if (!dragging) return;
            var el = (UIElement)s!;
            el.ReleasePointerCapture(e.Pointer);
            dragging = false;
            e.Handled = true;
        };
    }

    /// <summary>
    /// Attaches pointer event handlers for column drag-and-drop reorder.
    /// Drag begins after a 5px horizontal threshold to avoid interfering with clicks.
    /// On release, computes the target column index from the drop X position and
    /// calls state.ReorderColumn().
    /// </summary>
    private static void AttachReorderHandlers(
        FrameworkElement fe, DataGridState<T> state, int sourceIndex,
        IReadOnlyList<FieldDescriptor> columns, int cellOffset)
    {
        var dragging = false;
        var dragStarted = false;
        var startX = 0.0;

        fe.PointerPressed += (s, e) =>
        {
            var props = e.GetCurrentPoint(null).Properties;
            if (!props.IsLeftButtonPressed) return;

            dragging = true;
            dragStarted = false;
            startX = e.GetCurrentPoint(null).Position.X;
        };

        fe.PointerMoved += (s, e) =>
        {
            if (!dragging) return;
            var x = e.GetCurrentPoint(null).Position.X;
            var delta = Math.Abs(x - startX);

            if (!dragStarted && delta > 5)
            {
                dragStarted = true;
                var el = (UIElement)s!;
                el.CapturePointer(e.Pointer);
                fe.Opacity = 0.5;
            }
        };

        fe.PointerReleased += (s, e) =>
        {
            if (!dragging) return;
            var el = (UIElement)s!;

            if (dragStarted)
            {
                el.ReleasePointerCapture(e.Pointer);
                fe.Opacity = 1.0;

                // Compute target index from the drop X position relative to column widths.
                var dropX = e.GetCurrentPoint(null).Position.X;
                var totalDelta = dropX - startX;

                // Estimate target column: walk columns accumulating widths.
                var targetIndex = sourceIndex;
                if (totalDelta > 0)
                {
                    // Dragging right
                    double accumulated = 0;
                    for (int c = sourceIndex + 1; c < columns.Count; c++)
                    {
                        var w = state.GetColumnWidth(columns[c].Name);
                        accumulated += w;
                        if (totalDelta > accumulated - w / 2)
                            targetIndex = c;
                        else
                            break;
                    }
                }
                else if (totalDelta < 0)
                {
                    // Dragging left
                    double accumulated = 0;
                    for (int c = sourceIndex - 1; c >= 0; c--)
                    {
                        var w = state.GetColumnWidth(columns[c].Name);
                        accumulated -= w;
                        if (totalDelta < accumulated + w / 2)
                            targetIndex = c;
                        else
                            break;
                    }
                }

                if (targetIndex != sourceIndex)
                    state.ReorderColumn(sourceIndex, targetIndex);
            }

            dragging = false;
            dragStarted = false;
            e.Handled = true;
        };

        fe.PointerCanceled += (s, e) =>
        {
            if (dragStarted)
            {
                fe.Opacity = 1.0;
            }
            dragging = false;
            dragStarted = false;
        };
    }

    private static Element RenderDefaultHeader(
        FieldDescriptor col, SortDirection? sortDir, Action toggleSort)
    {
        return RenderDefaultHeader(col, sortDir, toggleSort, null, false);
    }

    private static Element RenderDefaultHeader(
        FieldDescriptor col, SortDirection? sortDir, Action toggleSort,
        DataGridState<T>? state, bool showFilter)
    {
        var label = col.DisplayName ?? col.Name;
        var sortIndicator = sortDir switch
        {
            SortDirection.Ascending => " \u25B2",
            SortDirection.Descending => " \u25BC",
            _ => "",
        };

        var hasActiveFilter = state?.GetFilter(col.Name) is not null;
        var filterIcon = showFilter && col.Filterable
            ? TextBlock(hasActiveFilter ? "\u2BC7" : "\u2BC6")
                .FontSize(10).Opacity(hasActiveFilter ? 1.0 : 0.4).Padding(horizontal: 2, vertical: 0)
            : null;

        if (col.Sortable)
        {
            return Button(
                FlexRow(
                    TextBlock(label).SemiBold().Flex(grow: 1),
                    sortIndicator.Length > 0
                        ? TextBlock(sortIndicator).FontSize(10).Opacity(0.7)
                        : null,
                    filterIcon
                ) with { AlignItems = FlexAlign.Center },
                toggleSort)
                .Background("#00000000")
                .BorderThickness(0)
                .Padding(8, 6, 8, 6)
                .HorizontalContentAlignment(HorizontalAlignment.Stretch)
                .HAlign(HorizontalAlignment.Stretch);
        }

        if (filterIcon is not null)
            return FlexRow(TextBlock(label).SemiBold().Flex(grow: 1), filterIcon).Padding(horizontal: 8, vertical: 6);

        return TextBlock(label).SemiBold().Padding(horizontal: 8, vertical: 6);
    }

    // ── Keyboard handling ───────────────────────────────────────────

    private static bool ShouldHandleKey(DataGridState<T> state, DataGridElement<T> el, VirtualKey key)
    {
        if (state.IsEditing)
        {
            return key is VirtualKey.Enter or VirtualKey.Escape or VirtualKey.Tab;
        }

        return key switch
        {
            VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right => true,
            VirtualKey.Tab or VirtualKey.Home or VirtualKey.End => true,
            VirtualKey.Enter or VirtualKey.F2 => el.Editable,
            VirtualKey.Space => state.FocusedKey is not null && el.SelectionMode != SelectionMode.None,
            _ => false,
        };
    }

    private static void HandleKeyDown(DataGridState<T> state, DataGridElement<T> el, VirtualKey key)
    {
        if (state.IsEditing)
        {
            switch (key)
            {
                case VirtualKey.Enter:
                    {
                        var editRowKey = state.EditingRowKey;
                        var originalItem = editRowKey is not null ? GetOriginalItem(state, editRowKey.Value) : default;
                        var commitResult = state.CommitEdit();
                        if (commitResult is not null && el.OnRowChanged is not null)
                            HandleAsyncCommit(state, el, commitResult.Value.Key, commitResult.Value.NewItem, originalItem!);
                    }
                    return;

                case VirtualKey.Escape:
                    state.CancelEdit();
                    return;

                case VirtualKey.Tab:
                    {
                        var editRowKey = state.EditingRowKey;
                        var originalItem = editRowKey is not null ? GetOriginalItem(state, editRowKey.Value) : default;
                        var tabResult = state.CommitAndMoveNext();
                        if (tabResult is not null && el.OnRowChanged is not null)
                            HandleAsyncCommit(state, el, tabResult.Value.Key, tabResult.Value.NewItem, originalItem!);
                        if (el.Editable)
                            state.BeginEdit();
                    }
                    return;
            }
            return;
        }

        switch (key)
        {
            case VirtualKey.Up:    state.MoveFocus(-1, 0);  break;
            case VirtualKey.Down:  state.MoveFocus(1, 0);   break;
            case VirtualKey.Left:  state.MoveFocus(0, -1);  break;
            case VirtualKey.Right: state.MoveFocus(0, 1);   break;
            case VirtualKey.Tab:   state.FocusNextCell();    break;
            case VirtualKey.Home:  state.FocusHome();        break;
            case VirtualKey.End:   state.FocusEnd();         break;

            case VirtualKey.Enter:
            case VirtualKey.F2:
                if (el.Editable) state.BeginEdit();
                break;

            case VirtualKey.Space:
                if (state.FocusedKey is not null && el.SelectionMode != SelectionMode.None)
                    state.HandleRowClick(state.FocusedKey.Value);
                break;
        }
    }

    /// <summary>Gets the item at the given row key position, for capturing pre-edit state.</summary>
    private static T? GetOriginalItem(DataGridState<T> state, RowKey key)
    {
        var idx = state.GetRowIndex(key);
        return idx >= 0 ? state.GetItemAt(idx) : default;
    }

    /// <summary>
    /// Default placeholder cell: a rounded gray bar that mimics a text shimmer.
    /// Produces a Text element with Padding — same structure as RenderCell —
    /// so RefreshRealizedItems can patch it to real content with property-only changes.
    /// </summary>
    private static Element RenderDefaultPlaceholderCell(FieldDescriptor col, double colWidth)
    {
        // Vary the bar width per column so it looks organic, not uniform
        var barText = new string('\u2003', Math.Max(1, (int)(colWidth / 24)));
        return TextBlock(barText).Padding(CellPadLeft, CellPadTop, CellPadRight, CellPadBottom)
            .Background(ControlFillSecondary).Opacity(0.5);
    }

    private static Element RenderDefaultLoading()
        => TextBlock("Loading...").Opacity(0.5).Padding(16)
            .HAlign(HorizontalAlignment.Center);

    private static Element RenderDefaultEmpty()
        => TextBlock("No data to display").Opacity(0.5).Padding(16)
            .HAlign(HorizontalAlignment.Center);

    private static Element RenderDefaultError(Exception ex)
    {
        return FlexColumn(
            TextBlock("Failed to load data").FontSize(14).Bold().Foreground(SystemCritical),
            TextBlock(ex.GetType().Name).FontSize(11).Opacity(0.6),
            TextBlock(ex.Message).FontSize(12).Opacity(0.8)
        ).Padding(16);
    }

    /// <summary>
    /// Routes the post-edit commit through the DataGrid's <c>UseMutation</c> handle.
    /// The handle's OnOptimistic snapshots the pre-edit item into <see cref="DataGridState{T}"/>,
    /// OnSuccess clears the committing flag, OnError writes the error into the row's
    /// banner. When no dispatcher is installed (e.g. headless tests), this method
    /// falls back to invoking <c>OnRowChanged</c> inline so the legacy tests that
    /// never go through <c>UseMutation</c> continue to pass.
    /// </summary>
    private static void HandleAsyncCommit(
        DataGridState<T> state,
        DataGridElement<T> el,
        RowKey key,
        T newItem,
        T originalItem)
    {
        if (el.OnRowChanged is null) return;

        if (state.CommitDispatcher is { } dispatch)
        {
            dispatch(key, newItem, originalItem);
            return;
        }

        // Fallback — no UseMutation dispatcher installed. Mirror the pre-Phase-3
        // Task.Run pattern so headless / non-hook consumers keep working.
        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        state.BeginAsyncCommit(key, originalItem);
        _ = Task.Run(async () =>
        {
            try
            {
                await el.OnRowChanged(key, newItem);

                if (dq is not null)
                    dq.TryEnqueue(() => state.CompleteAsyncCommit(key));
                else
                    state.CompleteAsyncCommit(key);
            }
            catch (Exception ex)
            {
                if (dq is not null)
                    dq.TryEnqueue(() => state.FailAsyncCommit(key, ex.Message));
                else
                    state.FailAsyncCommit(key, ex.Message);
            }
        });
    }
}
