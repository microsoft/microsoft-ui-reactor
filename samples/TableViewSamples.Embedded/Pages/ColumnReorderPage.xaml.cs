// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates the P2.12 programmatic surfaces:
///   TableView.MoveColumn(int from, int to)         — reorder
///   TableView.AutoSizeColumn(col)                  — fit one column
///   TableView.AutoSizeAllColumns()                 — fit all
///   TableView.CanUserReorderColumns                — control-wide gate
///   TableViewColumn.CanUserReorder                 — per-column gate
///
/// MoveColumn returns false on same-index no-op or when either gate
/// blocks; it throws hresult_out_of_bounds on truly invalid indices
/// (preserves IVector convention).
///
/// The page uses index-by-current-position semantics: when you pick
/// "Email" and click Move-left, we look up the column's current index
/// and call MoveColumn(currentIndex, currentIndex - 1).
/// </summary>
public sealed partial class ColumnReorderPage : Page
{
    public ColumnReorderPage()
    {
        InitializeComponent();

        foreach (var p in PersonData.Take(60))
        {
            People.Add(p);
        }

        Loaded += (_, _) =>
        {
            CapturePicker();
            UpdateReadout();
        };
    }

    public ObservableCollection<Person> People { get; } = new();

    // ----- Button handlers -----

    private void OnMoveLeftClick(object sender, RoutedEventArgs e)
        => MoveSelected(direction: -1);

    private void OnMoveRightClick(object sender, RoutedEventArgs e)
        => MoveSelected(direction: +1);

    private void OnAutoSizeClick(object sender, RoutedEventArgs e)
    {
        var col = SelectedColumn();
        if (col == null)
        {
            LastActionText.Text = "AutoSizeColumn skipped — no column selected.";
            return;
        }

        PeopleTable.AutoSizeColumn(col);
        LastActionText.Text =
            $"AutoSizeColumn(\"{col.Header}\") -> Width={col.ActualWidth:0}";
        UpdateReadout();
    }

    private void OnAutoSizeAllClick(object sender, RoutedEventArgs e)
    {
        PeopleTable.AutoSizeAllColumns();
        LastActionText.Text = "AutoSizeAllColumns() -> all columns sized to content.";
        UpdateReadout();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        // 2026-06-06 — replaced manual MoveColumn replay loop with the public
        // ResetColumnOrder() shipped in fix-n5. Snapshot is captured inside
        // the control at Loaded time, so consumers no longer need to maintain
        // their own _originalOrder list.
        PeopleTable.ResetColumnOrder();
        LastActionText.Text = "Reset -> restored original column order.";
        CapturePicker();
        UpdateReadout();
    }

    private void OnGlobalGateToggled(object sender, RoutedEventArgs e)
    {
        // ToggleSwitch.IsOn=True in XAML fires Toggled during InitializeComponent BEFORE
        // PeopleTable / LastActionText fields are populated. Guard against null sibling refs.
        if (PeopleTable is null || LastActionText is null) return;
        if (sender is ToggleSwitch toggle)
        {
            PeopleTable.CanUserReorderColumns = toggle.IsOn;
            LastActionText.Text =
                $"CanUserReorderColumns -> {toggle.IsOn} (MoveColumn now "
                + (toggle.IsOn ? "allowed" : "blocked")
                + ").";
        }
    }

    // ----- Helpers -----

    private void MoveSelected(int direction)
    {
        var col = SelectedColumn();
        if (col == null)
        {
            LastActionText.Text = "MoveColumn skipped — no column selected.";
            return;
        }

        int from = PeopleTable.Columns.IndexOf(col);
        int to = from + direction;
        if (from < 0)
        {
            LastActionText.Text = "MoveColumn skipped — column not found.";
            return;
        }
        if (to < 0 || to >= PeopleTable.Columns.Count)
        {
            LastActionText.Text =
                $"MoveColumn({from}, {to}) skipped — would move past edge.";
            return;
        }

        bool moved = PeopleTable.MoveColumn(from, to);
        LastActionText.Text =
            $"MoveColumn({from}, {to}) -> {moved} (\"{col.Header}\")";
        if (moved)
        {
            CapturePicker();
        }
        UpdateReadout();
    }

    private TableViewColumn? SelectedColumn()
    {
        if (ColumnPicker.SelectedItem is ComboBoxItem item
            && item.Tag is string columnId)
        {
            // SMP-CTL-6: match on the stable ColumnId, not the localizable /
            // duplicate-prone Header text.
            return PeopleTable.Columns
                .FirstOrDefault(c => string.Equals(c.ColumnId, columnId));
        }
        return null;
    }

    private void CapturePicker()
    {
        string? previous = (ColumnPicker.SelectedItem as ComboBoxItem)?.Tag as string;
        ColumnPicker.Items.Clear();
        for (int i = 0; i < PeopleTable.Columns.Count; i++)
        {
            var column = PeopleTable.Columns[i];
            string header = column.Header?.ToString() ?? $"#{i}";
            // Identity = stable ColumnId (SMP-CTL-6); fall back to Header only if a
            // column ships without one. Display still shows the Header for readability.
            string id = string.IsNullOrEmpty(column.ColumnId) ? header : column.ColumnId;
            var item = new ComboBoxItem
            {
                Content = $"{i}. {header}",
                Tag = id,
            };
            ColumnPicker.Items.Add(item);
        }

        // Reselect previous header so user's pick survives a reorder.
        for (int i = 0; i < ColumnPicker.Items.Count; i++)
        {
            if (ColumnPicker.Items[i] is ComboBoxItem item
                && item.Tag is string tag
                && string.Equals(tag, previous))
            {
                ColumnPicker.SelectedIndex = i;
                return;
            }
        }
        if (ColumnPicker.Items.Count > 0)
        {
            ColumnPicker.SelectedIndex = 0;
        }
    }

    private void UpdateReadout()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < PeopleTable.Columns.Count; i++)
        {
            if (i > 0) sb.Append(" -> ");
            sb.Append(PeopleTable.Columns[i].Header?.ToString());
        }
        ColumnOrderText.Text = sb.ToString();
    }
}
