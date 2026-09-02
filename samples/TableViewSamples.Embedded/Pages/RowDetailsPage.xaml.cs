// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView's N8 RowDetailsTemplate + RowDetailsVisibilityMode.
///
///   * RowDetailsTemplate is a DataTemplate instantiated per row, hosted in
///     a dedicated ContentPresenter (PART_RowDetailsHost) BELOW the row body.
///     DataContext is the row's bound item, so the template binds to the
///     same model the cells already do.
///
///   * RowDetailsVisibilityMode drives the panel's Visibility per row:
///       Collapsed            — never show
///       Visible              — always show on every row
///       VisibleWhenSelected  — show on the selected row(s) only
///
///   * RowDetailsVisibilityChanged fires whenever a row's panel transitions
///     between Collapsed and Visible. Useful for telemetry and lazy
///     content materialization.
///
///   * Per-row overrides (SetRowDetailsExpanded / GetRowDetailsExpanded)
///     pin the panel open for a specific item regardless of the global
///     mode. Not wired into this sample's UI but exposed on the API.
/// </summary>
public sealed partial class RowDetailsPage : Page
{
    private int _eventCount;

    public RowDetailsPage()
    {
        InitializeComponent();

        People = new ObservableCollection<Person>(PersonData.Take(20));
    }

    public ObservableCollection<Person> People { get; }

    private void OnVisibilityModeChecked(object sender, RoutedEventArgs e)
    {
        if (PeopleTable is null || sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        PeopleTable.RowDetailsVisibilityMode = tag switch
        {
            "Collapsed" => TableViewRowDetailsVisibilityMode.Collapsed,
            "Visible" => TableViewRowDetailsVisibilityMode.Visible,
            _ => TableViewRowDetailsVisibilityMode.VisibleWhenSelected,
        };
    }

    private void OnRowDetailsVisibilityChanged(TableView sender, TableViewRowDetailsEventArgs args)
    {
        _eventCount++;
        EventCountText.Text = _eventCount.ToString();
        var name = (args.Item as Person)?.FullName ?? "(unknown)";
        LastEventText.Text = $"{name} — {(args.IsVisible ? "details shown" : "details hidden")}";
    }
}
