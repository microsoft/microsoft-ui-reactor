// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView's per-column filter surface and the consumer-owned
/// re-shape model:
///
///   * Per column: TableViewColumn.Filter (a FilterDescription instance) +
///     TableViewColumn.IsFiltered (read-only, drives the header glyph) +
///     TableViewColumn.CanUserFilter (gates the visual glyph only).
///   * Per table: TableView.FilteredColumns (snapshot), TableView.ClearAllFilters(),
///     TableView.Filtered event.
///
/// The control owns the STATE (which columns are filtered, header visuals,
/// the event). The consumer owns the DATA shape — when Filtered fires,
/// re-shape the items source however you like.
///
/// This page shows the simplest reactive pattern: a master list, an
/// observable filtered list bound to the table, and a Filtered handler
/// that recomputes the filtered list on every change.
/// </summary>
public sealed partial class FilterPage : Page
{
    private readonly List<Person> _master;
    private int _filteredFiredCount;

    public FilterPage()
    {
        InitializeComponent();
        // Master snapshot stays stable for the page's lifetime; People is the
        // filtered view bound to the table.
        _master = PersonData.Take(200).ToList();
        People = new ObservableCollection<Person>(_master);
        Loaded += (_, _) => RefreshReadouts(triggerColumn: null);
    }

    public ObservableCollection<Person> People { get; }

    // ----- Per-column filter input handlers -----

    private void OnLastNameFilterChanged(object sender, TextChangedEventArgs e)
        => ApplyOrClearFilter(LastNameColumn, "LastName", LastNameFilterBox.Text);

    private void OnDepartmentFilterChanged(object sender, TextChangedEventArgs e)
        => ApplyOrClearFilter(DepartmentColumn, "Department", DepartmentFilterBox.Text);

    private void OnRoleFilterChanged(object sender, TextChangedEventArgs e)
        => ApplyOrClearFilter(RoleColumn, "Role", RoleFilterBox.Text);

    private void OnEmailFilterChanged(object sender, TextChangedEventArgs e)
        => ApplyOrClearFilter(EmailColumn, "Email", EmailFilterBox.Text);

    private void OnClearAllFiltersClick(object sender, RoutedEventArgs e)
    {
        // Reset all four input boxes silently — assigning empty strings
        // would trigger TextChanged → ApplyOrClearFilter → 4 individual
        // Filter writes → 4 Filtered raises. Detach handlers, reset, reattach.
        LastNameFilterBox.TextChanged   -= OnLastNameFilterChanged;
        DepartmentFilterBox.TextChanged -= OnDepartmentFilterChanged;
        RoleFilterBox.TextChanged       -= OnRoleFilterChanged;
        EmailFilterBox.TextChanged      -= OnEmailFilterChanged;

        LastNameFilterBox.Text = string.Empty;
        DepartmentFilterBox.Text = string.Empty;
        RoleFilterBox.Text = string.Empty;
        EmailFilterBox.Text = string.Empty;

        LastNameFilterBox.TextChanged   += OnLastNameFilterChanged;
        DepartmentFilterBox.TextChanged += OnDepartmentFilterChanged;
        RoleFilterBox.TextChanged       += OnRoleFilterChanged;
        EmailFilterBox.TextChanged      += OnEmailFilterChanged;

        // Single batch — TableView raises Filtered exactly once with Column=null.
        PeopleTable.ClearAllFilters();
    }

    /// <summary>
    /// Empty / whitespace text → clear the column's filter. Otherwise install
    /// a SubstringFilter for the typed text. Each write feeds straight into
    /// TableView's notify path; we don't recompute People here — that happens
    /// in the Filtered handler so the same code path runs for both
    /// TextBox-driven changes and ClearAllFilters() / programmatic writes.
    /// </summary>
    private static void ApplyOrClearFilter(TableViewColumn column, string propertyName, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            column.Filter = null;
            return;
        }

        // Reuse the existing FD if present — only patch the Needle. This
        // keeps the FD instance stable so consumers that snapshot it don't
        // see a churn of new objects per keystroke.
        if (column.Filter is SubstringFilter existing)
        {
            existing.Needle = text;
            // FD.Changed isn't auto-raised on reflection-style mutations;
            // call Invalidate so TableView re-evaluates IsFiltered + raises
            // Filtered for downstream consumers.
            existing.Invalidate();
        }
        else
        {
            column.Filter = new SubstringFilter
            {
                PropertyName = propertyName,
                Needle = text,
            };
        }
    }

    // ----- Filtered handler: re-shape the items source -----

    private void OnTableFiltered(TableView sender, TableViewFilteredEventArgs args)
    {
        _filteredFiredCount++;
        Recompute();
        RefreshReadouts(args.Column);
    }

    /// <summary>
    /// Recompute People from _master, applying every active column filter.
    /// A row is visible iff every active filter matches it. We rebuild the
    /// visible list rather than diffing — the dataset is small enough that
    /// this is simpler and clearer than tracking individual Add/Remove deltas.
    /// </summary>
    private void Recompute()
    {
        var filters = PeopleTable.FilteredColumns
            .Select(c => c.Filter)
            .Where(f => f != null)
            .ToList();

        IEnumerable<Person> visible = _master;
        if (filters.Count > 0)
        {
            visible = _master.Where(p => filters.All(f => f.Matches(p)));
        }

        // ObservableCollection rewrite. We could use Move + Insert/Remove for
        // a delta-style update (preserves selection on rows that survive),
        // but Clear+Add is fine for this sample.
        var snapshot = visible.ToList();
        People.Clear();
        foreach (var p in snapshot)
        {
            People.Add(p);
        }
    }

    // ----- Live readouts -----

    private void RefreshReadouts(TableViewColumn? triggerColumn)
    {
        if (PeopleTable is null) return;

        FilteredFiredCountText.Text = _filteredFiredCount.ToString();
        TriggerColumnText.Text = triggerColumn is null
            ? "(cleared / batch)"
            : ColumnLabel(triggerColumn);

        var filteredColumns = PeopleTable.FilteredColumns;
        if (filteredColumns.Count == 0)
        {
            FilteredColumnsText.Text = "(none)";
        }
        else
        {
            var parts = filteredColumns.Select(ColumnLabel);
            FilteredColumnsText.Text = string.Join("  ·  ", parts);
        }

        VisibleRowCountText.Text = $"{People.Count} of {_master.Count}";
    }

    private static string ColumnLabel(TableViewColumn column) =>
        column.Header?.ToString() ?? "(unnamed)";

    // Sample FilterDescription subclass: case-insensitive substring match on
    // a Person property identified by PropertyName. Real consumers would write
    // one per data shape (or use a common reflection-based helper).
    private sealed partial class SubstringFilter : FilterDescription
    {
        public string Needle { get; set; } = string.Empty;

        protected override bool MatchesCore(object item)
        {
            if (item is not Person p || string.IsNullOrEmpty(Needle))
            {
                return true;
            }

            string? value = PropertyName switch
            {
                "FirstName"  => p.FirstName,
                "LastName"   => p.LastName,
                "Email"      => p.Email,
                "Department" => p.Department,
                "Role"       => p.Role,
                _            => null,
            };

            return value != null
                && value.IndexOf(Needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
