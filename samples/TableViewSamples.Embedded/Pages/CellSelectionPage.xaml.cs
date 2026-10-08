// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView's cell-level selection surface: the SelectionUnit
/// property (Row / Cell / CellOrRow), the SelectedCells read-only view, the
/// CurrentCell accessor, and the SelectedCellsChanged event. Click a cell to
/// select it; Ctrl+click toggles a cell; Shift+click selects a rectangular
/// range from the anchor cell.
/// </summary>
public sealed partial class CellSelectionPage : Page
{
    private int _changeCount;

    public CellSelectionPage()
    {
        People = PersonData.Take(50);
        InitializeComponent();

        Loaded += (_, _) =>
        {
            UpdateUnitDescription((UnitCombo?.SelectedItem as ComboBoxItem)?.Content as string);
            RefreshReadout();
        };
    }

    public ObservableCollection<Person> People { get; }

    private void OnSelectionUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeopleTable is null || UnitCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var label = item.Content as string;
        PeopleTable.SelectionUnit = label switch
        {
            "Row"       => TableViewSelectionUnit.Row,
            "CellOrRow" => TableViewSelectionUnit.CellOrRow,
            _           => TableViewSelectionUnit.Cell,
        };
        UpdateUnitDescription(label);
        RefreshReadout();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (PeopleTable is null)
        {
            return;
        }

        // The public cell-selection surface has no bulk-clear method; entering Row mode
        // clears the cell store (SelectionUnit transition semantics), so flip to Row and
        // back to the current unit to clear without changing the visible selection unit.
        var unit = PeopleTable.SelectionUnit;
        if (unit != TableViewSelectionUnit.Row)
        {
            PeopleTable.SelectionUnit = TableViewSelectionUnit.Row;
            PeopleTable.SelectionUnit = unit;
        }
        RefreshReadout();
    }

    private void OnSelectedCellsChanged(TableView sender, TableViewSelectedCellsChangedEventArgs args)
    {
        _changeCount++;
        RefreshReadout();
    }

    private void UpdateUnitDescription(string? unit)
    {
        if (UnitDescriptionText is null)
        {
            return;
        }

        UnitDescriptionText.Text = unit switch
        {
            "Row"       => "Whole-row selection — the cell readouts stay empty. This is the classic TableView behavior.",
            "CellOrRow" => "Cell selection within the cell area. Click a cell to select it; the cell readouts update.",
            _           => "Individual cell selection. Click a cell to select it; Ctrl+click toggles; Shift+click selects a range.",
        };
    }

    private void RefreshReadout()
    {
        // Reachable during InitializeComponent (UnitCombo sets SelectedIndex in XAML, which
        // can raise events before the Options-rail readout TextBlocks are created). Guard so
        // the init-time call no-ops; Loaded re-runs it.
        if (PeopleTable is null || SelectedCellCountText is null)
        {
            return;
        }

        var selected = PeopleTable.SelectedCells;
        SelectedCellCountText.Text = (selected?.Count ?? 0).ToString();
        ChangeCountText.Text = _changeCount.ToString();

        var cell = PeopleTable.CurrentCell;
        if (cell is null || !cell.IsValid)
        {
            CurrentCellText.Text = "(none)";
        }
        else
        {
            var header = cell.Column?.Header?.ToString() ?? "?";
            var who = cell.Item is Person p ? $"{p.FirstName} {p.LastName}" : cell.Item?.ToString() ?? "?";
            CurrentCellText.Text = $"{who} · {header}";
        }
    }
}
