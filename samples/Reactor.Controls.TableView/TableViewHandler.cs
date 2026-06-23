using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>Creates/rents the native control, builds columns, binds items + selection.</summary>
    public WinUITableView Mount(MountContext ctx, TableViewElement el)
    {
        var tv = ctx.RentControl<WinUITableView>();
        Reconciler.SetElementTag(tv, el);

        tv.Height = el.Height;
        tv.MinWidth = el.MinWidth;
        ApplyTableProps(tv, el);

        ApplyColumns(tv, el);
        tv.ItemsSource = el.Items;
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
                var items = el.Items;
                tv.ItemsSource = null;
                tv.ItemsSource = items;
                if (el.SelectedIndex is { } si2)
                    tv.SelectedIndex = si2;
                try { tv.UpdateLayout(); } catch { }
            }
            catch { /* best-effort realization nudge */ }
        }
        tv.Loaded += OnLoaded;

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

        if (oldEl.Height != newEl.Height)
            tv.Height = newEl.Height;
        if (oldEl.MinWidth != newEl.MinWidth)
            tv.MinWidth = newEl.MinWidth;
        ApplyTableProps(tv, newEl);
        if (!ColumnsEqual(oldEl.Columns, newEl.Columns) || oldEl.FrozenColumnCount != newEl.FrozenColumnCount)
            ApplyColumns(tv, newEl);
        if (!ReferenceEquals(oldEl.Items, newEl.Items))
            tv.ItemsSource = newEl.Items;
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

            if (!double.IsNaN(c.Width))
                col.Width = new GridLength(c.Width);
            if (i < frozen)
                col.FrozenEdge = TableViewFrozenEdge.Leading;

            tv.Columns.Add(col);
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
