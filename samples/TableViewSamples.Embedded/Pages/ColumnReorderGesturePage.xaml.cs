// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

public sealed partial class ColumnReorderGesturePage : Page
{
    public ColumnReorderGesturePage()
    {
        InitializeComponent();

        foreach (var p in PersonData.Take(60))
        {
            People.Add(p);
        }

        Loaded += (_, _) => UpdateReadout();
    }

    public ObservableCollection<Person> People { get; } = new();

    private void OnGestureGateToggled(object sender, RoutedEventArgs e)
    {
        // ToggleSwitch.IsOn=True in XAML fires Toggled during InitializeComponent BEFORE
        // PeopleTable / LastEventText fields are populated. Guard against null sibling refs.
        if (PeopleTable is null || LastEventText is null) return;
        PeopleTable.CanUserReorderColumns = GestureGateToggle.IsOn;
        LastEventText.Text = $"CanUserReorderColumns={GestureGateToggle.IsOn} (gesture only)";
    }

    private void OnEmailCanReorderToggled(object sender, RoutedEventArgs e)
    {
        if (PeopleTable is null || LastEventText is null) return;
        if (ColumnById("email") is { } emailColumn)
        {
            emailColumn.CanUserReorder = EmailCanReorderToggle.IsOn;
            LastEventText.Text = $"Email.CanUserReorder={emailColumn.CanUserReorder}";
        }
    }

    private void OnFreezeNameClick(object sender, RoutedEventArgs e)
    {
        if (PeopleTable is null || LastEventText is null) return;
        if (ColumnById("name") is { } nameColumn)
        {
            nameColumn.FrozenEdge = nameColumn.FrozenEdge == TableViewFrozenEdge.None
                ? TableViewFrozenEdge.Leading
                : TableViewFrozenEdge.None;
            LastEventText.Text = $"Name.FrozenEdge={nameColumn.FrozenEdge}; frozen columns can't be dragged.";
        }
        UpdateReadout();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (PeopleTable is null || LastEventText is null) return;
        foreach (var col in PeopleTable.Columns)
        {
            col.FrozenEdge = TableViewFrozenEdge.None;
            col.CanUserReorder = true;
        }
        GestureGateToggle.IsOn = true;
        EmailCanReorderToggle.IsOn = true;

        // 2026-06-06 — replaced manual MoveColumn replay loop with the public
        // ResetColumnOrder() shipped in fix-n5.
        PeopleTable.ResetColumnOrder();
        LastEventText.Text = "Reset order and gesture gates.";
        UpdateReadout();
    }

    private void OnColumnReordering(TableView sender, TableViewColumnReorderingEventArgs args)
    {
        if (LastEventText is null) return;
        LastEventText.Text = $"ColumnReordering: {args.Column.Header} {args.FromIndex}->{args.ToIndex}";
    }

    private void OnColumnReordered(TableView sender, TableViewColumnReorderedEventArgs args)
    {
        if (LastEventText is null) return;
        LastEventText.Text = $"ColumnReordered: {args.Column.Header} {args.FromIndex}->{args.ToIndex}";
        UpdateReadout();
    }

    private TableViewColumn? ColumnById(string columnId)
        => PeopleTable?.Columns.FirstOrDefault(c => c.ColumnId == columnId);

    private void UpdateReadout()
    {
        if (PeopleTable is null || ColumnOrderText is null) return;
        ColumnOrderText.Text = string.Join(" -> ", PeopleTable.Columns.Select(c =>
        {
            string pin = c.FrozenEdge == TableViewFrozenEdge.None ? string.Empty : " (frozen)";
            string reorder = c.CanUserReorder ? string.Empty : " (CanUserReorder=false)";
            return $"{c.Header}{pin}{reorder}";
        }));
    }
}
