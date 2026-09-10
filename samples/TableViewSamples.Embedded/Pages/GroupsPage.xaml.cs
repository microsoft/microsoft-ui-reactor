// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates grouped rows over the native TableView pipeline.
///
/// Source shape is List&lt;DepartmentGroup&gt; where DepartmentGroup : List&lt;Person&gt;,
/// which projects to IIterable&lt;IInspectable&gt; at both outer and inner levels —
/// the contract <see cref="TableView.GroupedItemsSourceProperty"/> requires.
/// Columns are declared natively in XAML; clicking a header drives
/// OnSortDescriptionsChanged → PushColumnShapingToGroupedAdapter →
/// GSA.ShapeGroupItems → Entries.ReplaceAll, so rows within each group reshape
/// per P3.13.
/// </summary>
public sealed partial class GroupsPage : Page
{
    public GroupsPage()
    {
        // GroupedSourceAdapter casts each level as IIterable<IInspectable>; a
        // concrete List<List<T>> shape satisfies that on both outer and inner
        // levels. Initialize data BEFORE InitializeComponent so the XAML
        // IsOn="True" Toggled callback sees populated fields.
        _people = new ObservableCollection<Person>(PersonData.Take(50));
        _groupedSource = BuildGroupedView(_people);

        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
        ApplyGroupingMode(grouped: true);
    }

    private readonly ObservableCollection<Person> _people;
    private List<DepartmentGroup> _groupedSource;
    private bool _groupingEnabled;

    private ToggleSwitch GroupingToggle => GroupingToggleControl;
    private TextBlock IsGroupingText => IsGroupingTextBlock;
    private TextBlock GroupCountText => GroupCountTextBlock;
    private TextBlock TotalPeopleText => TotalPeopleTextBlock;
    private TextBlock SourceShapeText => SourceShapeTextBlock;
    private TextBlock PerGroupCountsText => PerGroupCountsTextBlock;
    private TableView PeopleTable => PeopleTableControl;

    private bool IsGroupingActive() => _groupingEnabled;

    // ----- Toggle / button -----

    private void OnGroupingToggled(object sender, RoutedEventArgs e)
    {
        // IsOn="True" in XAML raises Toggled during InitializeComponent, before
        // later-declared named elements are inflated. Bail out — the
        // constructor calls ApplyGroupingMode itself once the page is built.
        if (IsGroupingText is null)
        {
            return;
        }

        ApplyGroupingMode(GroupingToggle.IsOn);
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        PeopleTable.GroupExpanding -= OnGroupExpanding;
        PeopleTable.GroupCollapsing -= OnGroupCollapsing;
        PeopleTable.GroupExpanded -= OnGroupExpanded;
        PeopleTable.GroupCollapsed -= OnGroupCollapsed;
        PeopleTable.GroupExpanding += OnGroupExpanding;
        PeopleTable.GroupCollapsing += OnGroupCollapsing;
        PeopleTable.GroupExpanded += OnGroupExpanded;
        PeopleTable.GroupCollapsed += OnGroupCollapsed;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        PeopleTable.GroupExpanding -= OnGroupExpanding;
        PeopleTable.GroupCollapsing -= OnGroupCollapsing;
        PeopleTable.GroupExpanded -= OnGroupExpanded;
        PeopleTable.GroupCollapsed -= OnGroupCollapsed;
    }

    // ----- Source mode -----

    private void ApplyGroupingMode(bool grouped)
    {
        _groupingEnabled = grouped;

        if (grouped)
        {
            PeopleTable.ItemsSource = null;
            PeopleTable.GroupedItemsSource = _groupedSource;
        }
        else
        {
            PeopleTable.GroupedItemsSource = null;
            PeopleTable.ItemsSource = _people;
        }

        PeopleTable.UpdateLayout();
        UpdateReadout(grouped);
    }

    private void OnShuffleClick(object sender, RoutedEventArgs e)
    {
        // Reassign Department on ~25% of rows then rebuild the grouped wrapper.
        // The adapter sees the outer-collection swap (we assign a new
        // GroupedItemsSource) and re-renders.
        var random = new Random();
        var depts = _people.Select(p => p.Department).Distinct().ToArray();
        for (int i = 0; i < _people.Count; i += 4)
        {
            _people[i].Department = depts[random.Next(depts.Length)];
        }

        if (_groupingEnabled)
        {
            _groupedSource = BuildGroupedView(_people);
            PeopleTable.GroupedItemsSource = _groupedSource;
            PeopleTable.UpdateLayout();
            UpdateReadout(grouped: true);
        }
    }

    private void OnExpandAllClick(object sender, RoutedEventArgs e)
    {
        if (!_groupingEnabled)
        {
            return;
        }

        PeopleTable.ExpandAllGroups();
        PeopleTable.UpdateLayout();
        UpdateReadout(grouped: true);
    }

    private void OnCollapseAllClick(object sender, RoutedEventArgs e)
    {
        if (!_groupingEnabled)
        {
            return;
        }

        PeopleTable.CollapseAllGroups();
        PeopleTable.UpdateLayout();
        UpdateReadout(grouped: true);
    }

    // === A3 (Wave-4a) — group lifecycle event handlers ===

    private readonly Queue<string> _eventLogLines = new();
    private const int EventLogMaxLines = 12;

    private void OnGroupExpanding(TableView sender, TableViewGroupExpandingEventArgs args)
    {
        var key = args.GroupKey?.ToString() ?? "<null>";
        if (CancelExpandingCheckBox?.IsChecked == true)
        {
            args.Cancel = true;
            AppendEventLog($"GroupExpanding  key={key} Cancel=true (sample-cancel)");
        }
        else
        {
            AppendEventLog($"GroupExpanding  key={key} Cancel=false");
        }
    }

    private void OnGroupCollapsing(TableView sender, TableViewGroupCollapsingEventArgs args)
    {
        var key = args.GroupKey?.ToString() ?? "<null>";
        if (CancelCollapsingCheckBox?.IsChecked == true)
        {
            args.Cancel = true;
            AppendEventLog($"GroupCollapsing key={key} Cancel=true (sample-cancel)");
        }
        else
        {
            AppendEventLog($"GroupCollapsing key={key} Cancel=false");
        }
    }

    private void OnGroupExpanded(TableView sender, TableViewGroupExpandedEventArgs args)
    {
        var key = args.GroupKey?.ToString() ?? "<null>";
        AppendEventLog($"GroupExpanded   key={key}");
    }

    private void OnGroupCollapsed(TableView sender, TableViewGroupCollapsedEventArgs args)
    {
        var key = args.GroupKey?.ToString() ?? "<null>";
        AppendEventLog($"GroupCollapsed  key={key}");
    }

    private void OnClearEventLogClick(object sender, RoutedEventArgs e)
    {
        _eventLogLines.Clear();
        if (GroupEventLogTextBlock is not null)
        {
            GroupEventLogTextBlock.Text = string.Empty;
        }
    }

    private void AppendEventLog(string line)
    {
        _eventLogLines.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {line}");
        while (_eventLogLines.Count > EventLogMaxLines)
        {
            _eventLogLines.Dequeue();
        }
        if (GroupEventLogTextBlock is not null)
        {
            GroupEventLogTextBlock.Text = string.Join(Environment.NewLine, _eventLogLines);
        }
    }

    private void UpdateReadout(bool grouped)
    {
        IsGroupingText.Text = IsGroupingActive().ToString();

        if (grouped)
        {
            var groupCount = _groupedSource.Count;
            var totalCount = _groupedSource.Sum(g => g.Count);
            GroupCountText.Text = groupCount.ToString();
            TotalPeopleText.Text = totalCount.ToString();
            SourceShapeText.Text = $"GroupedItemsSource → List<DepartmentGroup> ({groupCount} groups, {totalCount} people)";
            PerGroupCountsText.Text = string.Join(", ",
                _groupedSource.OrderBy(g => g.Department).Select(g => $"{g.Department}: {g.Count}"));
        }
        else
        {
            GroupCountText.Text = "(n/a)";
            TotalPeopleText.Text = _people.Count.ToString();
            SourceShapeText.Text = $"ItemsSource = ObservableCollection<Person> ({_people.Count} items)";
            PerGroupCountsText.Text = "(grouping off)";
        }
    }

    // GroupedSourceAdapter walks the outer source as IIterable<IInspectable>
    // and each inner group the same way. Concrete List<List<T>> projects
    // cleanly to both interfaces, so the adapter materializes one entry per
    // group + one per item.
    private static List<DepartmentGroup> BuildGroupedView(IEnumerable<Person> people)
    {
        return people
            .OrderBy(p => p.Department)
            .GroupBy(p => p.Department)
            .Select(g => new DepartmentGroup(g.Key, g))
            .ToList();
    }

    /// <summary>
    /// Concrete inner-group shape so GroupedSourceAdapter's IIterable&lt;IInspectable&gt;
    /// cast succeeds at both outer and inner levels. Carries the department name
    /// alongside the person list so the adapter surfaces it as the group key.
    /// </summary>
    public sealed class DepartmentGroup : List<Person>
    {
        public DepartmentGroup(string department, IEnumerable<Person> people) : base(people)
        {
            Department = department;
        }

        public string Department { get; }

        public override string ToString() => Department;
    }
}
