// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView's multi-column sort surface and the consumer-owned
/// re-shape model:
///
///   * The control owns sort STATE — direction, priority index, header
///     SortIndicator visuals (chevron + multi-sort priority badge), the
///     public SortByColumn / SetSortColumn / ToggleSortDirection / ClearSort
///     API, and the Sorted event.
///   * The consumer owns the DATA — when Sorted fires, walk
///     args.SortedColumns in priority order and re-order your items source
///     however you like (LINQ multi-key sort here; could equally be
///     ShapedCollectionView.SortDescriptions, a server-side query, or a
///     hand-written comparer).
///
/// The dataset is a fictional Champions-League-style group stage (8 groups
/// × 4 teams, 6 games each) because the official UEFA standings rule —
/// Group asc, then Points desc, then Goal Difference desc as tiebreaker —
/// is the canonical scenario where multi-key sort actually matters: within
/// a group, teams routinely finish on equal points and the second-tier
/// sort is what separates them.
///
/// The accompanying multi-sort priority badge (1, 2, 3…) is suppressed
/// when only a single column is sorted, matching modern data-grid
/// convention (AG Grid, Material, Notion, Airtable, macOS Finder).
/// </summary>
public sealed partial class SortPage : Page
{
    private int _sortedFiredCount;
    private int _filteredFiredCount;

    // Master snapshot of the unfiltered, unsorted row order. When Sorted or
    // Filtered fires, we rebuild Teams = master.Where(allFilters).Then(sort).
    // ClearSort + ClearAllFilters both fire their respective events with empty
    // state; the pipeline naturally restores the original order.
    private readonly List<LeagueTeam> _master;

    private static readonly Dictionary<string, Func<LeagueTeam, IComparable?>> s_keySelectors =
        new(StringComparer.Ordinal)
        {
            ["Group"]          = t => t.Group,
            ["Team"]           = t => t.Team,
            ["Wins"]           = t => t.Wins,
            ["Draws"]          = t => t.Draws,
            ["Losses"]         = t => t.Losses,
            ["GoalsFor"]       = t => t.GoalsFor,
            ["GoalsAgainst"]   = t => t.GoalsAgainst,
            ["GoalDifference"] = t => t.GoalDifference,
            ["Points"]         = t => t.Points,
        };

    public SortPage()
    {
        InitializeComponent();
        Teams = LeagueData.All();
        _master = Teams.ToList();
        Loaded += (_, _) => RefreshReadouts(triggerColumn: null);
    }

    public ObservableCollection<LeagueTeam> Teams { get; }

    // ----- Programmatic sort affordances -----

    private void OnSortPointsDescClick(object sender, RoutedEventArgs e)
    {
        TeamsTable.SortByColumn(PointsColumn, SortDirection.Descending);
    }

    private void OnAddGroupAscClick(object sender, RoutedEventArgs e)
    {
        TeamsTable.SetSortColumn(GroupColumn, SortDirection.Ascending);
    }

    private void OnAddGoalDifferenceDescClick(object sender, RoutedEventArgs e)
    {
        TeamsTable.SetSortColumn(GoalDifferenceColumn, SortDirection.Descending);
    }

    /// <summary>
    /// The showpiece: apply the canonical UEFA group-stage sort — Group
    /// ascending, then Points descending, then Goal Difference descending
    /// — with three back-to-back API calls.
    /// </summary>
    private void OnOfficialStandingsClick(object sender, RoutedEventArgs e)
    {
        TeamsTable.SortByColumn(GroupColumn, SortDirection.Ascending);
        TeamsTable.SetSortColumn(PointsColumn, SortDirection.Descending);
        TeamsTable.SetSortColumn(GoalDifferenceColumn, SortDirection.Descending);
    }

    private void OnTogglePointsClick(object sender, RoutedEventArgs e)
    {
        TeamsTable.ToggleSortDirection(PointsColumn, TableViewSortToggleMode.Replace);
    }

    private void OnClearSortClick(object sender, RoutedEventArgs e)
    {
        TeamsTable.ClearSort();
    }

    // ----- Sorted + Filtered handlers: rebuild the items source -----
    //
    // Control owns the STATE (column.Filter, column.SortDirection / SortIndex,
    // the header glyphs, the events). Consumer owns the DATA shape: whenever
    // either event fires, rebuild Teams = master.Where(filters).Then(sort) so
    // sort + filter compose regardless of which one changed.

    private void OnTableSorted(TableView sender, TableViewSortedEventArgs args)
    {
        _sortedFiredCount++;
        RebuildVisibleRows();
        RefreshReadouts(args.Column);
    }

    private void OnTableFiltered(TableView sender, TableViewFilteredEventArgs args)
    {
        _filteredFiredCount++;
        RebuildVisibleRows();
        RefreshReadouts(args.Column);
    }

    private void RebuildVisibleRows()
    {
        // 1. Start from the master list, intersect with every active filter.
        var activeFilters = TeamsTable.FilteredColumns
            .Select(c => c.Filter)
            .Where(f => f is not null)
            .ToList();

        IEnumerable<LeagueTeam> visible = _master;
        if (activeFilters.Count > 0)
        {
            visible = _master.Where(t => activeFilters.All(f => f!.Matches(t)));
        }

        // 2. Apply the active sort chain (control walks priority via SortIndex).
        var sortedColumns = TeamsTable.SortedColumns
            .OrderBy(c => c.SortIndex)
            .ToList();
        if (sortedColumns.Count > 0)
        {
            IOrderedEnumerable<LeagueTeam>? ordered = null;
            foreach (var column in sortedColumns)
            {
                var path = column.SortMemberPath;
                if (string.IsNullOrEmpty(path) ||
                    !s_keySelectors.TryGetValue(path, out var keySelector))
                {
                    continue;
                }

                var dir = column.SortDirection;
                ordered = ordered is null
                    ? (dir == SortDirection.Descending ? visible.OrderByDescending(keySelector) : visible.OrderBy(keySelector))
                    : (dir == SortDirection.Descending ? ordered.ThenByDescending(keySelector) : ordered.ThenBy(keySelector));
            }
            if (ordered is not null) visible = ordered;
        }

        // 3. Replace Teams in place. Clear + Add is simplest for a 32-row
        // sample; partners with large datasets or selection-preservation
        // requirements should diff with .Move()/.Insert()/.RemoveAt() instead.
        var snapshot = visible.ToList();
        Teams.Clear();
        foreach (var t in snapshot) Teams.Add(t);
    }

    // ----- Live readouts -----

    private void RefreshReadouts(TableViewColumn? triggerColumn)
    {
        if (TeamsTable is null) return;

        SortedFiredCountText.Text = _sortedFiredCount.ToString();
        TriggerColumnText.Text = triggerColumn is null
            ? "(cleared)"
            : ColumnLabel(triggerColumn);

        var sortedColumns = TeamsTable.SortedColumns;
        if (sortedColumns.Count == 0)
        {
            SortPriorityListText.Text = "(none)";
        }
        else
        {
            var parts = sortedColumns
                .OrderBy(c => c.SortIndex)
                .Select(c => $"{c.SortIndex}. {ColumnLabel(c)} {c.SortDirection}");
            SortPriorityListText.Text = string.Join("  ·  ", parts);
        }

        VisibleRowsText.Text = $"{Teams.Count} / {_master.Count}";

        var preview = Teams.Take(5).Select((t, i) =>
            $"{i + 1}. {t.Group} {t.Team} ({t.Points}pts, {t.GoalDifference:+0;-0;0} GD)");
        TopRowsPreviewText.Text = preview.Any() ? string.Join(" · ", preview) : "(no rows)";
    }

    private static string ColumnLabel(TableViewColumn column) =>
        column.Header?.ToString() ?? "(unnamed)";
}
