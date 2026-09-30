// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableViewColumn.FrozenEdge — leading columns stay pinned
/// while the body scrolls horizontally.
///
/// The control allocates a single shared TranslateTransform in
/// OnApplyTemplate and drives its X to the body ScrollViewer's
/// HorizontalOffset on every ViewChanged. Frozen header + cell
/// wrappers opt-in to that transform with Canvas.ZIndex=1, so they
/// paint above the scrolling region. Body-cell wrappers OneWay-bind
/// their Background to PART_RootBorder.Background so the row VSM
/// (selection / pointer-over) propagates across the pin instead of
/// being masked.
///
/// Toggling FrozenEdge at runtime routes through OnColumnFrozenEdgeChanged
/// which rebuilds the header host and walks every realized row's cells.
/// </summary>
public sealed partial class FrozenColumnsPage : Page
{
    public FrozenColumnsPage()
    {
        InitializeComponent();

        foreach (var p in PersonData.Take(60))
        {
            People.Add(p);
        }

        SampleShape.EnableDefaults(PeopleTable, People);

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public ObservableCollection<Person> People { get; } = new();

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        PeopleTable.ViewChanged -= OnTableViewChanged;
        PeopleTable.ViewChanged += OnTableViewChanged;
        UpdateReadout();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        PeopleTable.ViewChanged -= OnTableViewChanged;
    }

    // ----- Toggle handlers -----

    private void OnFreezeFirstNameToggled(object sender, RoutedEventArgs e)
    {
        if (FirstNameColumn != null && sender is ToggleSwitch toggle)
        {
            FirstNameColumn.FrozenEdge = toggle.IsOn ? TableViewFrozenEdge.Leading : TableViewFrozenEdge.None;
            UpdateReadout();
        }
    }

    private void OnFreezeLastNameToggled(object sender, RoutedEventArgs e)
    {
        if (LastNameColumn != null && sender is ToggleSwitch toggle)
        {
            LastNameColumn.FrozenEdge = toggle.IsOn ? TableViewFrozenEdge.Leading : TableViewFrozenEdge.None;
            UpdateReadout();
        }
    }

    private void OnFreezeEmailToggled(object sender, RoutedEventArgs e)
    {
        if (EmailColumn != null && sender is ToggleSwitch toggle)
        {
            EmailColumn.FrozenEdge = toggle.IsOn ? TableViewFrozenEdge.Leading : TableViewFrozenEdge.None;
            UpdateReadout();
        }
    }

    // ----- Helpers -----

    private void OnTableViewChanged(TableView sender, TableViewViewChangedEventArgs args)
    {
        UpdateReadout();
    }

    private void UpdateReadout()
    {
        // Reachable during InitializeComponent: a ToggleSwitch with IsOn="True" set in
        // XAML raises Toggled synchronously while the page tree is still being built, so
        // the Options-rail readout TextBlocks (declared after the toggles) may not exist
        // yet. Guard so the init-time call no-ops; PeopleTable.Loaded re-runs UpdateReadout.
        if (FrozenColumnsText is null || BodyOffsetText is null || PeopleTable is null)
        {
            return;
        }

        var frozenHeaders = new List<string>();
        foreach (var col in PeopleTable.Columns)
        {
            if (col is TableViewTextColumn tc && tc.FrozenEdge != TableViewFrozenEdge.None)
            {
                frozenHeaders.Add(tc.Header?.ToString() ?? "(unnamed)");
            }
        }

        FrozenColumnsText.Text = frozenHeaders.Count == 0
            ? "(none)"
            : string.Join(", ", frozenHeaders);

        BodyOffsetText.Text = $"{PeopleTable.HorizontalOffset:0}";
    }
}
