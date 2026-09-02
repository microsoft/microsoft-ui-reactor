using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using WinUITableView = Microsoft.UI.Xaml.Controls.TableView;
using TableViewTextColumn = Microsoft.UI.Xaml.Controls.TableViewTextColumn;
using TableViewTemplateColumn = Microsoft.UI.Xaml.Controls.TableViewTemplateColumn;
using TableViewColumn = Microsoft.UI.Xaml.Controls.TableViewColumn;
using TableViewFrozenEdge = Microsoft.UI.Xaml.Controls.TableViewFrozenEdge;
using GridLength = Microsoft.UI.Xaml.GridLength;
using TableViewSelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.TableViewSelectionChangedEventArgs;
using SortDirection = Microsoft.UI.Xaml.Controls.Primitives.SortDirection;

namespace Reactor.Controls;

/// <summary>
/// V1 handler that mounts, updates, and unmounts <see cref="TableViewElement"/>
/// instances onto the native split-binary <see cref="WinUITableView"/>.
/// Mirrors the opt-in control pattern used by Reactor.Advanced's Win2D handlers.
/// </summary>
public sealed class TableViewHandler : IElementHandler<TableViewElement, WinUITableView>
{
    /// <summary>The most recently mounted native control (used by the demo's headless capture).</summary>
    public static WinUITableView? LastInstance { get; private set; }

    // Per-control sort/filter reshape state. The native TableView owns sort/filter STATE (the column
    // SortMemberPath/SortDirection/SortIndex + Filter + header chevrons/funnels) and raises Sorted/Filtered;
    // the CONSUMER owns the DATA -- it must re-order/-filter the items source itself. We honour that
    // contract by binding an ObservableCollection "view" and rebuilding it from a master snapshot on each
    // event (matching the reference TableViewSamples SortPage's Sorted/Filtered re-shape model).
    private sealed class ShapeState
    {
        public List<object> Master = new();
        public ObservableCollection<object> View = new();
        public bool Hooked;
        // A live source we track so in-place add/remove/move/reset reflect in the table (re-applying the
        // active sort/filter), honouring the standard ItemsSource = ObservableCollection contract.
        public System.Collections.Specialized.INotifyCollectionChanged? Source;
        public System.Collections.Specialized.NotifyCollectionChangedEventHandler? SourceHandler;
    }

    private static readonly ConditionalWeakTable<WinUITableView, ShapeState> s_shape = new();

    /// <summary>Creates/rents the native control, builds columns, binds items + selection.</summary>
    public WinUITableView Mount(MountContext ctx, TableViewElement el)
    {
        var tv = ctx.RentControl<WinUITableView>();
        Reconciler.SetElementTag(tv, el);

        ApplyLayout(tv, el);
        ApplyTableProps(tv, el);

        ApplyColumns(tv, el);
        BindItems(tv, el);
        if (el.SelectedIndex is { } si)
            tv.SelectedIndex = si;

        // The satellite control's default Style isn't found by implicit lookup for a loose consumer,
        // and a code-only Reactor host has no XAML metadata for it -- register + apply so it renders.
        TableViewStyles.EnsureLoadedAndApply(tv);

        // The native control populates its header host (PinnedRegionPresenter) and body rows
        // (ItemsRepeater) from Columns/ItemsSource only after its ControlTemplate is applied. In a
        // code-only host the control is data-bound during Mount -- BEFORE it enters the live tree, so
        // its template isn't applied yet and the initial population is a no-op (declarative XAML hosts
        // avoid this because the template is parsed first, then data is bound). Re-assert columns + items
        // once Loaded fires (template now present) so headers + rows actually realize.
        void OnLoaded(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
        {
            tv.Loaded -= OnLoaded;
            try
            {
                ApplyColumns(tv, el);
                BindItems(tv, el);
                if (el.SelectedIndex is { } si2)
                    tv.SelectedIndex = si2;
                try { tv.UpdateLayout(); } catch { }
                try { el.OnControlReady?.Invoke(tv); } catch { /* page-supplied callback */ }
            }
            catch { /* best-effort realization nudge */ }
        }
        tv.Loaded += OnLoaded;

        HookSortFilter(tv);

        // Re-attaches on every render so the latest element's callback fires (echo-safe).
        var bind = ctx.BindFor(tv, el);
        bind.OnCustomEvent<TableViewSelectionChangedEventArgs>(
            subscribe: static (c, t) => ((WinUITableView)c).SelectionChanged += (s, a) => t(s, a),
            unsubscribe: static (_, _) => { },
            handler: static (cur, args) => cur.OnSelectionChanged?.Invoke(args));

        ctx.ApplySetters(el.Setters, tv);
        LastInstance = tv;
        SelfTest.NoteMounted(tv);
        return tv;
    }

    /// <summary>Diffs old vs new element and applies minimal writes to the live control.</summary>
    public void Update(UpdateContext ctx, TableViewElement oldEl, TableViewElement newEl, WinUITableView tv)
    {
        Reconciler.SetElementTag(tv, newEl);

        if (oldEl.Height != newEl.Height || oldEl.Stretch != newEl.Stretch)
            ApplyLayout(tv, newEl);
        ApplyTableProps(tv, newEl);
        if (!ColumnsEqual(oldEl.Columns, newEl.Columns) || oldEl.FrozenColumnCount != newEl.FrozenColumnCount)
            ApplyColumns(tv, newEl);
        if (!ReferenceEquals(oldEl.Items, newEl.Items)
            || !ReferenceEquals(oldEl.HierarchicalItems, newEl.HierarchicalItems)
            || oldEl.HierarchicalChildrenPath != newEl.HierarchicalChildrenPath)
            BindItems(tv, newEl);
        if (newEl.SelectedIndex is { } si && oldEl.SelectedIndex != newEl.SelectedIndex)
            tv.SelectedIndex = si;

        ctx.ApplySetters(newEl.Setters, tv);
    }

    /// <summary>Applies the table-level feature properties (idempotent; only writes the ones set).</summary>
    private static void ApplyTableProps(WinUITableView tv, TableViewElement el)
    {
        if (el.SelectionMode is { } sm) tv.SelectionMode = sm;
        if (el.SelectionUnit is { } su) tv.SelectionUnit = su;
        if (el.GridLinesVisibility is { } gl) tv.GridLinesVisibility = gl;
        if (el.HeadersVisibility is { } hv) tv.HeadersVisibility = hv;
        if (el.CanSortColumns is { } cs) tv.CanUserSortColumns = cs;
        if (el.CanFilterColumns is { } cf) tv.CanUserFilterColumns = cf;
        if (el.CanReorderColumns is { } cr) tv.CanUserReorderColumns = cr;
        if (el.CanResizeColumns is { } cz) tv.CanUserResizeColumns = cz;
        if (el.IsSelectionGutterVisible is { } sg) tv.IsSelectionGutterVisible = sg;
    }

    /// <summary>Clears item/column state and returns the control to the Reactor pool.</summary>
    public void Unmount(UnmountContext ctx, WinUITableView tv)
    {
        tv.ItemsSource = null;
        tv.Columns.Clear();
        if (s_shape.TryGetValue(tv, out var st))
        {
            DetachSource(st);
            st.Master.Clear();
            st.View.Clear();
        }
        ctx.ReturnControl(tv);
    }

    private static void ApplyColumns(WinUITableView tv, TableViewElement el)
    {
        tv.Columns.Clear();
        IReadOnlyList<TableColumn> cols = el.Columns ?? AutoColumns(el.Items);
        int frozen = el.FrozenColumnCount ?? 0;
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            TableViewColumn col;
            if (c.Style == CellStyle.Text)
            {
                col = new TableViewTextColumn
                {
                    Header = c.Header,
                    Binding = new Binding { Path = new PropertyPath(c.PropertyPath) },
                };
            }
            else
            {
                var tmpl = TableViewCellTemplates.Create(c.PropertyPath, c.Style);
                col = new TableViewTemplateColumn { Header = c.Header };
                if (tmpl != null)
                    ((TableViewTemplateColumn)col).CellTemplate = tmpl;
            }

            // Make the column sortable/filterable by the bound property. The native control needs an
            // explicit SortMemberPath to know which member to sort/filter on (template columns have no
            // Binding to infer it from, and even text columns require it to participate). Header clicks
            // then raise Sorted/Filtered, which Reshape() honours.
            if (!string.IsNullOrEmpty(c.PropertyPath))
                col.SortMemberPath = c.PropertyPath;

            if (!double.IsNaN(c.Width))
                col.Width = new GridLength(c.Width);
            if (i < frozen)
                col.FrozenEdge = TableViewFrozenEdge.Leading;

            tv.Columns.Add(col);
        }
    }

    /// <summary>
    /// Binds the items through an ObservableCollection "view" so the consumer-owned sort/filter
    /// reshape can rebuild it in place when the native control raises Sorted/Filtered.
    /// </summary>
    private static void BindItems(WinUITableView tv, TableViewElement el)
    {
        if (el.HierarchicalItems != null)
        {
            // Tree-grid mode: the native control shapes the hierarchy itself (the flat sort/filter
            // reshape doesn't apply). Bind HierarchicalItemsSource + the children-property name.
            if (!string.IsNullOrEmpty(el.HierarchicalChildrenPath))
                tv.HierarchicalChildrenPropertyName = el.HierarchicalChildrenPath;
            tv.ItemsSource = null;
            tv.HierarchicalItemsSource = el.HierarchicalItems;
            if (el.ExpandFirstLevel)
            {
                // Expand the roots once the tree is realized (ExpandItem before the rows exist is a no-op).
                var roots = el.HierarchicalItems;
                tv.DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    try { foreach (var item in roots) tv.ExpandItem(item); } catch { /* best-effort */ }
                });
            }
            return;
        }
        tv.HierarchicalItemsSource = null;
        var st = s_shape.GetOrCreateValue(tv);
        DetachSource(st);
        st.Master = el.Items?.Cast<object>().ToList() ?? new List<object>();
        st.View = new ObservableCollection<object>(st.Master);
        tv.ItemsSource = st.View;

        // Track a live source so in-place add/remove/move/reset on the consumer's collection reflect in
        // the table, re-applying the active sort/filter. (Sort/filter still go through the Sorted/Filtered
        // re-shape; here we keep Master in sync with the source.)
        if (el.Items is System.Collections.Specialized.INotifyCollectionChanged incc)
        {
            st.Source = incc;
            st.SourceHandler = (_, _) =>
            {
                st.Master = (el.Items?.Cast<object>() ?? Enumerable.Empty<object>()).ToList();
                Reshape(tv);
            };
            incc.CollectionChanged += st.SourceHandler;
        }
    }

    private static void DetachSource(ShapeState st)
    {
        if (st.Source != null && st.SourceHandler != null)
            st.Source.CollectionChanged -= st.SourceHandler;
        st.Source = null;
        st.SourceHandler = null;
    }

    /// <summary>Applies stretch-to-fill, explicit fixed height, or leaves height to layout/modifiers.</summary>
    private static void ApplyLayout(WinUITableView tv, TableViewElement el)
    {
        if (el.Stretch)
        {
            tv.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
            tv.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
            tv.Height = double.NaN;
            tv.MinHeight = 320;
        }
        else if (el.Height is { } h)
        {
            tv.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top;
            tv.Height = h;
            tv.MinHeight = 0;
        }
        else
        {
            // No explicit height + not stretching: leave sizing to layout modifiers (.Height()/.MinHeight()).
            tv.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top;
            tv.Height = double.NaN;
        }
    }

    private static void HookSortFilter(WinUITableView tv)
    {
        var st = s_shape.GetOrCreateValue(tv);
        if (st.Hooked)
            return;
        st.Hooked = true;
        tv.Sorted += static (s, _) => Reshape(s);
        tv.Filtered += static (s, _) => Reshape(s);
    }

    /// <summary>
    /// Rebuilds the bound view from the master snapshot: intersect every active column filter, then
    /// apply the active sort chain in priority order. This is the data half of the control's
    /// consumer-owned re-shape contract.
    /// </summary>
    private static void Reshape(WinUITableView tv)
    {
        if (!s_shape.TryGetValue(tv, out var st))
            return;

        IEnumerable<object> visible = st.Master;

        var filters = tv.FilteredColumns
            .Select(c => c.Filter)
            .Where(f => f is not null)
            .ToList();
        if (filters.Count > 0)
            visible = visible.Where(item => filters.All(f => f!.Matches(item)));

        var sortedColumns = tv.SortedColumns.OrderBy(c => c.SortIndex).ToList();
        if (sortedColumns.Count > 0)
        {
            IOrderedEnumerable<object>? ordered = null;
            foreach (var column in sortedColumns)
            {
                var path = column.SortMemberPath;
                if (string.IsNullOrEmpty(path))
                    continue;
                Func<object, object?> key = item => GetMember(item, path);
                bool desc = column.SortDirection == SortDirection.Descending;
                ordered = ordered is null
                    ? (desc ? visible.OrderByDescending(key, MemberComparer.Instance) : visible.OrderBy(key, MemberComparer.Instance))
                    : (desc ? ordered.ThenByDescending(key, MemberComparer.Instance) : ordered.ThenBy(key, MemberComparer.Instance));
            }
            if (ordered is not null)
                visible = ordered;
        }

        var snapshot = visible.ToList();
        st.View.Clear();
        foreach (var item in snapshot)
            st.View.Add(item);
    }

    private static readonly Dictionary<(Type, string), PropertyInfo?> s_propCache = new();

    private static object? GetMember(object item, string path)
    {
        if (item is null)
            return null;
        var keyT = (item.GetType(), path);
        if (!s_propCache.TryGetValue(keyT, out var pi))
        {
            pi = item.GetType().GetProperty(path, BindingFlags.Public | BindingFlags.Instance);
            s_propCache[keyT] = pi;
        }
        return pi?.GetValue(item);
    }

    /// <summary>Null-safe comparer that uses IComparable when available, else string compare.</summary>
    private sealed class MemberComparer : IComparer<object?>
    {
        public static readonly MemberComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            if (x is IComparable cx && x.GetType() == y.GetType())
                return cx.CompareTo(y);
            return string.Compare(x.ToString(), y.ToString(), StringComparison.CurrentCulture);
        }
    }

    // Reflection-based convenience for demos. The library is a non-Reactor (consumer)
    // project, so trimming/AOT warnings are suppressed there; pass explicit Columns for
    // trim-safe usage.
    private static IReadOnlyList<TableColumn> AutoColumns(IEnumerable? items)
    {
        var first = items?.Cast<object>().FirstOrDefault();
        if (first is null)
            return Array.Empty<TableColumn>();
        return first.GetType().GetProperties()
            .Select(p => new TableColumn(p.Name, p.Name))
            .ToList();
    }

    private static bool ColumnsEqual(IReadOnlyList<TableColumn>? a, IReadOnlyList<TableColumn>? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null || a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i]))
                return false;
        }
        return true;
    }
}

internal static class SelfTest
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("TVDEMO_SELFTEST") == "1";

    internal static void NoteMounted(WinUITableView tv)
    {
        if (!Enabled)
            return;
        try
        {
            var log = Path.Combine(AppContext.BaseDirectory, "tvdemo-selftest.log");
            File.AppendAllText(
                log,
                "PASS: native " + tv.GetType().FullName + " activated + " + tv.Columns.Count +
                " columns + ItemsSource set inside Reactor mount via first-class TableViewHandler (WinAppSDK 2.0.1)" +
                " | render[" + TableViewStyles.Status + "]\n");
        }
        catch
        {
            // best-effort diagnostics only
        }
    }
}
