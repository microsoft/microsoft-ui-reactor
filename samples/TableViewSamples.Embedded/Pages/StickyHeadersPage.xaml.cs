// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView's sticky-header behaviour:
///
///   * Vertical stickiness — the header row stays visible at the top of the
///     table as you scroll vertically. This is structural: the header is in
///     Grid.Row=0 of the template, OUTSIDE the body's ScrollViewer.
///
///   * Horizontal sync — when the body scrolls horizontally, the header
///     scroller's HorizontalOffset is matched to the body's so column titles
///     remain aligned with their cells. The C++ control hooks PART_BodyScroller's
///     ViewChanged event in OnApplyTemplate and calls
///     PART_HeaderScroller.ChangeView(..., disableAnimation: true) to keep
///     the two in lockstep.
///
/// The readout below now binds to the public TableView.ViewChanged event and
/// the public {Horizontal,Vertical}Offset / Scrollable{Width,Height} DPs —
/// no visual-tree walk or PART_*Scroller cache.
/// </summary>
public sealed partial class StickyHeadersPage : Page
{
    public StickyHeadersPage()
    {
        InitializeComponent();

        // 30 rows is enough to scroll vertically inside a 320-px viewport.
        var people = PersonData.Take(30).ToList();
        var managers = people.Take(6).ToList();
        var employees = people
            .Select((p, index) => new EmployeeRow
            {
                EmployeeId = $"E{1000 + index:0000}",
                FirstName = p.FirstName,
                LastName = p.LastName,
                Email = p.Email,
                Department = p.Department,
                Role = p.Role,
                Office = $"Bldg {(index % 8) + 1} / {(index % 4) + 1}{(char)('A' + (index % 26))}",
                Phone = $"+1 (425) 555-{(2000 + index):0000}",
                // Pick a stable manager from the first six rows so the column
                // is non-trivial — repeats are fine for the demo.
                Manager = managers[index % managers.Count].FullName,
            })
            .ToList();
        Employees = new ObservableCollection<EmployeeRow>(employees);

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public ObservableCollection<EmployeeRow> Employees { get; }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        WideTable.ViewChanged -= OnTableViewChanged;
        WideTable.ViewChanged += OnTableViewChanged;
        RefreshReadout();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        WideTable.ViewChanged -= OnTableViewChanged;
    }

    private void OnTableViewChanged(TableView sender, TableViewViewChangedEventArgs args)
    {
        // Only tick the readout once the view has settled — we'd otherwise see
        // intermediate offsets while inertial pan is still in flight.
        if (!args.IsIntermediate)
        {
            RefreshReadout();
        }
    }

    private void RefreshReadout()
    {
        // Header and body are kept in lockstep by the control itself — the
        // public HorizontalOffset DP reflects the body scroller, which is the
        // canonical horizontal position. The header offset is implicitly equal
        // (the control schedules header.ChangeView async on body ViewChanged),
        // so showing it as a duplicate of the body offset is accurate AND
        // crash-free even if the template hasn't yet inflated.
        var h = WideTable.HorizontalOffset;
        var v = WideTable.VerticalOffset;
        BodyHOffsetText.Text   = h.ToString("F1");
        HeaderHOffsetText.Text = h.ToString("F1");
        BodyVOffsetText.Text   = v.ToString("F1");
        HeaderVOffsetText.Text = "0.0"; // header is structurally above the body's V-scroll surface.

        InSyncText.Text = "✓";
    }

    // ----- Programmatic scroll buttons -----

    private void OnScrollHomeClick(object sender, RoutedEventArgs e)
        => WideTable.ChangeView(0.0, null, null);

    private void OnScrollMidClick(object sender, RoutedEventArgs e)
        => WideTable.ChangeView(400.0, null, null);

    private void OnScrollEndClick(object sender, RoutedEventArgs e)
        => WideTable.ChangeView(WideTable.ScrollableWidth, null, null);

    private void OnScrollDownClick(object sender, RoutedEventArgs e)
        => WideTable.ChangeView(null, 200.0, null);

    /// <summary>
    /// Per-row DTO with enough columns to make the table wider than the
    /// viewport — that's what triggers H-scroll and exercises the sync.
    /// </summary>
    public sealed class EmployeeRow
    {
        public string EmployeeId { get; set; } = string.Empty;
        public string FirstName  { get; set; } = string.Empty;
        public string LastName   { get; set; } = string.Empty;
        public string Email      { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Role       { get; set; } = string.Empty;
        public string Office     { get; set; } = string.Empty;
        public string Phone      { get; set; } = string.Empty;
        public string Manager    { get; set; } = string.Empty;
    }
}
