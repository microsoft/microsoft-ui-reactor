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
        if (el.SelectionMode is { } mode)
            tv.SelectionMode = mode;

        ApplyColumns(tv, el);
        tv.ItemsSource = el.Items;
        if (el.SelectedIndex is { } si)
            tv.SelectedIndex = si;

        // The satellite control's default Style isn't found by implicit lookup for a loose consumer,
        // and a code-only Reactor host has no XAML metadata for it -- register + apply so it renders.
        TableViewStyles.EnsureLoadedAndApply(tv);

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
        if (newEl.SelectionMode is { } mode && oldEl.SelectionMode != newEl.SelectionMode)
            tv.SelectionMode = mode;
        if (!ColumnsEqual(oldEl.Columns, newEl.Columns))
            ApplyColumns(tv, newEl);
        if (!ReferenceEquals(oldEl.Items, newEl.Items))
            tv.ItemsSource = newEl.Items;
        if (newEl.SelectedIndex is { } si && oldEl.SelectedIndex != newEl.SelectedIndex)
            tv.SelectedIndex = si;

        ctx.ApplySetters(newEl.Setters, tv);
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
        foreach (var c in cols)
        {
            tv.Columns.Add(new TableViewTextColumn
            {
                Header = c.Header,
                Binding = new Binding { Path = new PropertyPath(c.PropertyPath) },
            });
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
