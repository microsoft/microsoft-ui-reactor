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
/// Demonstrates TableViewColumn.FrozenEdge=Trailing — columns pinned to the
/// RIGHT edge of the viewport stay anchored while the body scrolls
/// horizontally. Mirrors the leading-pin behaviour shipped in P2.11; the
/// FrozenEdge enum is the single source of truth for the pin treatment.
///
/// Layering, transform sharing and selection-visual propagation work
/// identically to leading-frozen — the only differences are:
///   * trailing transform.X = HorizontalOffset - ScrollableWidth (vs
///     just HorizontalOffset for leading), so the pin anchors at the
///     right edge regardless of scroll position;
///   * the control subscribes to bodyScroller.SizeChanged in addition
///     to ViewChanged, so window-resize keeps the trailing pin glued
///     to the new right edge even when scroll position is 0.
///
/// Toggling FrozenEdge at runtime routes through OnColumnFrozenEdgeChanged
/// which rebuilds the header host and walks every realized row's cells.
/// </summary>
public sealed partial class FrozenTrailingColumnsPage : Page
{
    public FrozenTrailingColumnsPage()
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
        PeopleTable.SizeChanged -= OnTableSizeChanged;
        PeopleTable.ViewChanged += OnTableViewChanged;
        PeopleTable.SizeChanged += OnTableSizeChanged;
        UpdateReadout();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        PeopleTable.ViewChanged -= OnTableViewChanged;
        PeopleTable.SizeChanged -= OnTableSizeChanged;
    }

    // ----- Toggle handlers -----

    private void OnPinSalaryToggled(object sender, RoutedEventArgs e)
    {
        if (SalaryColumn != null && sender is ToggleSwitch toggle)
        {
            SalaryColumn.FrozenEdge = toggle.IsOn ? TableViewFrozenEdge.Trailing : TableViewFrozenEdge.None;
            UpdateReadout();
        }
    }

    private void OnPinActiveToggled(object sender, RoutedEventArgs e)
    {
        if (ActiveColumn != null && sender is ToggleSwitch toggle)
        {
            ActiveColumn.FrozenEdge = toggle.IsOn ? TableViewFrozenEdge.Trailing : TableViewFrozenEdge.None;
            UpdateReadout();
        }
    }

    private void OnPinRoleToggled(object sender, RoutedEventArgs e)
    {
        if (RoleColumn != null && sender is ToggleSwitch toggle)
        {
            // Note: Role is in the middle of the column collection, so flipping
            // it Trailing produces a "non-contiguous trailing" arrangement that
            // is documented as undefined visual behaviour in v1. The toggle is
            // kept here so reviewers can observe the no-crash invariant directly.
            RoleColumn.FrozenEdge = toggle.IsOn ? TableViewFrozenEdge.Trailing : TableViewFrozenEdge.None;
            UpdateReadout();
        }
    }

    // ----- Helpers -----

    private void OnTableViewChanged(TableView sender, TableViewViewChangedEventArgs args)
    {
        UpdateReadout();
    }

    private void OnTableSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateReadout();
    }

    private void UpdateReadout()
    {
        // Reachable during InitializeComponent (a ToggleSwitch with IsOn set in XAML raises
        // Toggled before the Options-rail readout TextBlocks are created). Guard so the
        // init-time call no-ops; PeopleTable.Loaded re-runs UpdateReadout.
        if (TrailingColumnsText is null || BodyOffsetText is null || ScrollableWidthText is null || PeopleTable is null)
        {
            return;
        }

        var trailingHeaders = new List<string>();
        foreach (var col in PeopleTable.Columns)
        {
            if (col is TableViewTextColumn tc && tc.FrozenEdge == TableViewFrozenEdge.Trailing)
            {
                trailingHeaders.Add(tc.Header?.ToString() ?? "(unnamed)");
            }
        }

        TrailingColumnsText.Text = trailingHeaders.Count == 0
            ? "(none)"
            : string.Join(", ", trailingHeaders);

        BodyOffsetText.Text = $"{PeopleTable.HorizontalOffset:0}";
        ScrollableWidthText.Text = $"{PeopleTable.ScrollableWidth:0}";
    }
}
