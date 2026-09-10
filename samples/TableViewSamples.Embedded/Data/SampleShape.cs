// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace TableViewSamples.Data;

/// <summary>
/// Reusable wiring that gives any sample page "default" sort + filter behavior
/// against a flat ObservableCollection&lt;T&gt; bound to a TableView.
///
/// The TableView control intentionally does NOT reshape a flat ItemsSource — it
/// only raises Sorted / Filtered events with the state stamped on each column
/// (SortIndex / SortDirection / Filter). The consumer owns the data shape (see
/// SortPage.xaml.cs: "control owns STATE; consumer owns DATA"). This helper is
/// the canonical consumer-owned re-shape, generalized via reflection on T so
/// the same code works for Person, EmployeeRow, etc., on every sample page.
///
/// Three entry points:
///   * EnableDefaults(table, source) — wires Sorted + Filtered. Snapshots the
///     master ordering once at call time so filters can be cleared back to
///     full data and sorts apply on top of the current filter set.
///   * EnableDefaultSort(table, source) — wires Sorted only. Operates on the
///     current contents of <paramref name="source"/> and uses Move() to keep
///     selection alive. Use this on pages that already own a custom Filter
///     handler so the two re-shapes compose without fighting over a master copy.
///   * EnableDefaultFilter(table, source) — wires Filtered only. Useful on
///     pages that own their own Sort handler.
///
/// Key-selector resolution:
///   1. col.SortMemberPath if non-empty.
///   2. TableViewTextColumn / TableViewCheckBoxColumn fall back to Binding.Path.Path
///      (mirrors the runtime fallback on those derived columns). So binding-only
///      columns (no explicit SortMemberPath set in XAML) Just Work.
///   3. Dotted paths supported (e.g. "Address.City") via reflection chain.
///   4. Non-IComparable property values sort to default(null) and are stable.
///
/// Filter composition: all active <see cref="TableView.FilteredColumns"/> are
/// AND'd via <see cref="FilterDescription.Matches"/>. Sort then runs over the
/// filtered set so sort+filter compose correctly in a single re-shape.
/// </summary>
internal static class SampleShape
{
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, Func<object, IComparable?>?>> s_selectorCache =
        new();

    public static void EnableDefaults<T>(TableView table, ObservableCollection<T> source) where T : class
    {
        if (table is null || source is null)
        {
            return;
        }

        var master = new List<T>(source);
        table.Sorted += (_, _) => Reshape(table, source, master);
        table.Filtered += (_, _) => Reshape(table, source, master);
    }

    public static void EnableDefaultSort<T>(TableView table, ObservableCollection<T> source) where T : class
    {
        if (table is null || source is null)
        {
            return;
        }

        table.Sorted += (_, _) =>
        {
            var current = new List<T>(source);
            var sorted = ApplySort(table, current);
            ApplyToObservable(source, sorted);
        };
    }

    public static void EnableDefaultFilter<T>(TableView table, ObservableCollection<T> source) where T : class
    {
        if (table is null || source is null)
        {
            return;
        }

        var master = new List<T>(source);
        table.Filtered += (_, _) =>
        {
            var filtered = ApplyFilter(table, master);
            var sorted = ApplySort(table, filtered);
            ApplyToObservable(source, sorted);
        };
    }

    // --- internal pipeline ----------------------------------------------------

    private static void Reshape<T>(TableView table, ObservableCollection<T> source, List<T> master) where T : class
    {
        var filtered = ApplyFilter(table, master);
        var sorted = ApplySort(table, filtered);
        ApplyToObservable(source, sorted);
    }

    private static List<T> ApplyFilter<T>(TableView table, List<T> master) where T : class
    {
        var filters = table.FilteredColumns?
            .Select(c => c.Filter)
            .Where(f => f is not null)
            .ToList();

        if (filters is null || filters.Count == 0)
        {
            return new List<T>(master);
        }

        var result = new List<T>(master.Count);
        foreach (var item in master)
        {
            bool keep = true;
            foreach (var f in filters)
            {
                if (!f!.Matches(item))
                {
                    keep = false;
                    break;
                }
            }
            if (keep)
            {
                result.Add(item);
            }
        }
        return result;
    }

    private static List<T> ApplySort<T>(TableView table, List<T> items) where T : class
    {
        var sortedCols = table.Columns?
            .Where(c => c.SortDirection != SortDirection.None)
            .OrderBy(c => c.SortIndex)
            .ToList();

        if (sortedCols is null || sortedCols.Count == 0)
        {
            return items;
        }

        IOrderedEnumerable<T>? ordered = null;
        foreach (var col in sortedCols)
        {
            var selector = BuildKeySelector<T>(col);
            if (selector is null)
            {
                continue;
            }

            bool desc = col.SortDirection == SortDirection.Descending;
            if (ordered is null)
            {
                ordered = desc ? items.OrderByDescending(selector) : items.OrderBy(selector);
            }
            else
            {
                ordered = desc ? ordered.ThenByDescending(selector) : ordered.ThenBy(selector);
            }
        }

        return ordered is null ? items : ordered.ToList();
    }

    private static void ApplyToObservable<T>(ObservableCollection<T> source, List<T> target) where T : class
    {
        // Pure re-order with identical multiset → Move() so selection/scroll position survive.
        if (source.Count == target.Count && SameMultiset(source, target))
        {
            for (int i = 0; i < target.Count; i++)
            {
                if (ReferenceEquals(source[i], target[i]))
                {
                    continue;
                }

                int currentIdx = -1;
                for (int j = i + 1; j < source.Count; j++)
                {
                    if (ReferenceEquals(source[j], target[i]))
                    {
                        currentIdx = j;
                        break;
                    }
                }
                if (currentIdx > i)
                {
                    source.Move(currentIdx, i);
                }
            }
            return;
        }

        // Otherwise rebuild (filter changed the count, or items differ).
        source.Clear();
        foreach (var item in target)
        {
            source.Add(item);
        }
    }

    private static bool SameMultiset<T>(ObservableCollection<T> a, List<T> b) where T : class
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        var counts = new Dictionary<T, int>(a.Count, ReferenceEqualityComparer.Instance);
        foreach (var item in a)
        {
            counts[item] = counts.TryGetValue(item, out var c) ? c + 1 : 1;
        }
        foreach (var item in b)
        {
            if (!counts.TryGetValue(item, out var c) || c == 0)
            {
                return false;
            }
            counts[item] = c - 1;
        }
        return true;
    }

    private static Func<T, IComparable?>? BuildKeySelector<T>(TableViewColumn col) where T : class
    {
        var path = col.EffectiveSortMemberPath;
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var rawSelector = GetCachedSelector(typeof(T), path);
        if (rawSelector is null)
        {
            return null;
        }

        return item => item is null ? null : rawSelector(item);
    }

    // Resolve the effective sort member path using the shipped public surface
    // (SMP-CTL-3): TableViewColumn.EffectiveSortMemberPath returns SortMemberPath,
    // or the column's CLR binding path fallback for the built-in column types —
    // the same projection the sample previously hand-rolled by reflecting over
    // TableViewTextColumn/TableViewCheckBoxColumn.Binding.Path.Path.
    private static Func<object, IComparable?>? GetCachedSelector(Type rootType, string path)
    {
        var perType = s_selectorCache.GetOrAdd(rootType, _ => new ConcurrentDictionary<string, Func<object, IComparable?>?>(StringComparer.Ordinal));
        return perType.GetOrAdd(path, p => BuildSelector(rootType, p));
    }

    private static Func<object, IComparable?>? BuildSelector(Type rootType, string path)
    {
        var parts = path.Split('.');
        var props = new PropertyInfo[parts.Length];
        var cur = rootType;
        for (int i = 0; i < parts.Length; i++)
        {
            var pi = cur.GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (pi is null)
            {
                return null;
            }
            props[i] = pi;
            cur = pi.PropertyType;
        }

        return obj =>
        {
            object? v = obj;
            foreach (var pi in props)
            {
                if (v is null)
                {
                    return null;
                }
                v = pi.GetValue(v);
            }
            return v as IComparable;
        };
    }
}
