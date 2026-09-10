// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView.CanUserMarqueeSelect — the drag-selection rectangle
/// (marquee) gesture shipped in P3.4. The control composes the Phase 1
/// MarqueeSelector primitive against the new PART_BodyContent template part
/// (a Grid wrapping PART_RowsRepeater) and renders the lasso into the new
/// PART_MarqueeAdornment Rectangle inside it.
///
/// Key invariants this page lets reviewers verify by hand:
///   * Default-off — toggle the switch to false and the gesture stops firing.
///   * Touch is excluded — touching + dragging on the body pans the
///     ScrollView instead of starting a marquee (try with a touchscreen).
///   * Mode-gated — flip SelectionMode to None or Single from the combo and
///     the marquee gesture short-circuits in OnBodyContentPointerPressed.
///   * Replace-not-extend — pre-select rows by Ctrl-clicking, then start a
///     marquee somewhere else; the prior selection clears in favour of the
///     marquee's contiguous range (v1 contract; Ctrl-additive is a follow-up).
///   * Group-header rows are skipped — but this page doesn't enable groups
///     to keep the focus on the marquee mechanics; the Groups page is the
///     place to see the no-crash invariant.
///
/// The readout panel surfaces SelectedCount / SelectedIndices and a coalesced
/// "ranges" line so reviewers can see CommitMarqueeSelection's batch shape
/// (singletons via Select(int), runs via SelectRange(IndexPath, IndexPath)).
/// </summary>
public sealed partial class MarqueePage : Page
{
    private const int DemoRowCount = 12;

    public MarqueePage()
    {
        InitializeComponent();

        foreach (var p in PersonData.Take(DemoRowCount))
        {
            People.Add(p);
        }

        RowsLoadedText.Text = People.Count.ToString();
        UpdateGestureStatus();
    }

    public ObservableCollection<Person> People { get; } = new();

    // ----- Toggle / mode handlers -----

    private void OnMarqueeSelectToggled(object sender, RoutedEventArgs e)
    {
        if (PeopleTable != null && sender is ToggleSwitch toggle)
        {
            PeopleTable.CanUserMarqueeSelect = toggle.IsOn;
            UpdateGestureStatus();
        }
    }

    private void OnSelectionModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeopleTable == null || sender is not ComboBox combo) return;
        var label = combo.SelectedItem?.ToString();
        PeopleTable.SelectionMode = label switch
        {
            "None" => TableViewSelectionMode.None,
            "Single" => TableViewSelectionMode.Single,
            "Multiple" => TableViewSelectionMode.Multiple,
            "Extended" => TableViewSelectionMode.Extended,
            _ => PeopleTable.SelectionMode,
        };

        UpdateGestureStatus();
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        PeopleTable?.DeselectAll();
    }

    private void UpdateGestureStatus()
    {
        // Reachable during InitializeComponent (the marquee ToggleSwitch sets IsOn in XAML,
        // raising Toggled before the Options-rail status TextBlocks are created). Guard so the
        // init-time call no-ops; the ctor re-runs UpdateGestureStatus after InitializeComponent.
        if (MarqueeSelectToggle is null || PeopleTable is null
            || GestureStatusText is null || ReadoutGestureStatusText is null)
        {
            return;
        }

        var status = !MarqueeSelectToggle.IsOn
            ? "Gesture disabled — turn CanUserMarqueeSelect on."
            : PeopleTable.SelectionMode is TableViewSelectionMode.None or TableViewSelectionMode.Single
                ? "Gesture unavailable in None / Single mode — choose Multiple or Extended."
                : $"Ready — drag in the blank area below the last row ({People.Count} rows loaded).";

        GestureStatusText.Text = status;
        ReadoutGestureStatusText.Text = status;
    }

    // ----- Selection readout -----

    private void OnTableSelectionChanged(TableView sender, TableViewSelectionChangedEventArgs args)
    {
        if (PeopleTable == null) return;

        var indices = PeopleTable.SelectedIndices?.ToList() ?? new List<int>();
        indices.Sort();

        SelectedCountText.Text = indices.Count.ToString();
        SelectedIndicesText.Text = indices.Count == 0
            ? "(none)"
            : string.Join(", ", indices);

        CommitRangesText.Text = indices.Count == 0
            ? "(none)"
            : DescribeRanges(indices);
    }

    /// <summary>
    /// Coalesces a sorted index list into the same kind of contiguous-range
    /// description that CommitMarqueeSelection drives the SelectionModel
    /// with — singletons stay as "N", runs collapse to "A..B". Reviewers can
    /// glance at this and confirm that a marquee over rows 3..7 emits "3..7"
    /// (one SelectRange call) rather than five Select(int) calls.
    /// </summary>
    private static string DescribeRanges(List<int> sortedIndices)
    {
        var ranges = new List<string>();
        int start = sortedIndices[0];
        int prev = start;
        for (int i = 1; i < sortedIndices.Count; i++)
        {
            int v = sortedIndices[i];
            if (v == prev + 1)
            {
                prev = v;
                continue;
            }

            ranges.Add(start == prev ? $"{start}" : $"{start}..{prev}");
            start = v;
            prev = v;
        }
        ranges.Add(start == prev ? $"{start}" : $"{start}..{prev}");
        return string.Join(", ", ranges);
    }
}
