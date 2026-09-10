// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Advanced filter UI surface (P3.8). Demonstrates four layers of opt-in:
///
///   1. Default — FirstName / LastName columns: nothing is set on the column.
///      Clicking the funnel button opens TableView's auto-flyout with the full
///      operator vocabulary (Contains / Equals / StartsWith / EndsWith /
///      IsEmpty / IsNotEmpty) over a TextBox. Apply assigns a
///      TableViewSimpleFilter to column.Filter; Clear nulls it.
///
///   2. Custom FilterOperators — Role column: the auto-flyout still opens,
///      but the ComboBox is constrained to {Equals, IsEmpty, IsNotEmpty}.
///      The runtime hides the value TextBox for IsEmpty / IsNotEmpty.
///
///   3. Custom FilterFlyout — Department column: a consumer-built Flyout
///      (defined in XAML) replaces the auto-flyout. The consumer is
///      responsible for synthesising a FilterDescription and assigning it
///      to column.Filter from inside the custom flyout's handler.
///
///   4. FilterFlyoutOpening Cancel — Email column: subscriber sets
///      args.Cancel = true so no flyout opens. Used by consumers who want
///      to drive their own modal UX (e.g. a dedicated "filters" dialog
///      hosted elsewhere in the page) on funnel-button clicks.
///
/// The Filtered handler at the bottom feeds the consumer-owned re-shape
/// loop the same way the basic filter sample does — TableView owns the
/// state (which columns are filtered, header glyphs); the consumer owns
/// the data shape.
/// </summary>
public sealed partial class AdvancedFilterPage : Page
{
    private readonly List<Person> _master;
    private int _filteredFiredCount;
    private int _flyoutOpeningCount;
    private Flyout? _departmentPickerFlyout;
    private ListBox? _departmentList;
    private Button? _clearDepartmentButton;

    public AdvancedFilterPage()
    {
        InitializeComponent();
        _master = PersonData.Take(120).ToList();
        People = new ObservableCollection<Person>(_master);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ObservableCollection<Person> People { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DetachDepartmentPickerHandlers();

        // Layer 2: narrow the operator vocabulary on the Role column. The
        // auto-flyout still opens; its operator ComboBox shows only what
        // we hand it. Setting an empty IVector would suppress the auto-
        // flyout entirely.
        RoleColumn.FilterOperators = new ObservableCollection<TableViewFilterOperator>
        {
            TableViewFilterOperator.Equals,
            TableViewFilterOperator.IsEmpty,
            TableViewFilterOperator.IsNotEmpty,
        };

        // Layer 3: build a consumer-owned department picker flyout in code.
        // (Building it from XAML inside <TableViewColumn.FilterFlyout> is
        // possible, but the column subtree is rooted in a DependencyObject
        // - not a FrameworkElement - so x:Name resolution from code-behind
        // is not guaranteed. Building it from code keeps the demo robust.)
        _departmentList = new ListBox
        {
            MaxHeight = 220,
            SelectionMode = SelectionMode.Single,
        };
        _departmentList.SelectionChanged += OnDepartmentPicked;
        AutomationProperties.SetAutomationId(_departmentList, "DepartmentList");

        var departments = _master
            .Select(p => p.Department)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var dept in departments)
        {
            _departmentList.Items.Add(dept);
        }

        _clearDepartmentButton = new Button
        {
            Content = "Clear",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _clearDepartmentButton.Click += OnClearDepartmentClick;
        AutomationProperties.SetAutomationId(_clearDepartmentButton, "ClearDepartmentButton");

        var pickerStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, MinWidth = 220 };
        pickerStack.Children.Add(new TextBlock
        {
            Text = "Pick a department",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        pickerStack.Children.Add(_departmentList);
        pickerStack.Children.Add(_clearDepartmentButton);

        _departmentPickerFlyout = new Flyout { Content = pickerStack };
        DepartmentColumn.FilterFlyout = _departmentPickerFlyout;

        RefreshReadouts();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachDepartmentPickerHandlers();
    }

    private void DetachDepartmentPickerHandlers()
    {
        if (_departmentList is not null)
        {
            _departmentList.SelectionChanged -= OnDepartmentPicked;
        }

        if (_clearDepartmentButton is not null)
        {
            _clearDepartmentButton.Click -= OnClearDepartmentClick;
        }

        if (DepartmentColumn is not null)
        {
            DepartmentColumn.FilterFlyout = null;
        }

        _departmentPickerFlyout = null;
        _departmentList = null;
        _clearDepartmentButton = null;
    }

    // ----- Layer 4: FilterFlyoutOpening — Cancel for Email -----

    private void OnFilterFlyoutOpening(TableView sender, TableViewFilterFlyoutOpeningEventArgs args)
    {
        _flyoutOpeningCount++;
        FlyoutOpeningCountText.Text = _flyoutOpeningCount.ToString();
        LastOpeningColumnText.Text = ColumnLabel(args.Column);

        // Email opts out of any flyout. A real consumer would surface their
        // own dialog/pane here keyed off args.Column.
        if (ReferenceEquals(args.Column, EmailColumn))
        {
            args.Cancel = true;
            LastOpeningColumnText.Text = $"{ColumnLabel(args.Column)} (canceled — your app would show its own filter UI)";
        }
    }

    // ----- Layer 3: Department picker (custom FilterFlyout) -----

    private void OnDepartmentPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_departmentList?.SelectedItem is not string department)
        {
            return;
        }

        // Synthesise a TableViewSimpleFilter that exact-matches the chosen
        // department. The consumer owns both the picker UX and the
        // FilterDescription instance — TableView only cares that
        // column.Filter is non-null and IsActive.
        DepartmentColumn.Filter = new TableViewSimpleFilter
        {
            PropertyName = "Department",
            Operator = TableViewFilterOperator.Equals,
            Value = department,
        };

        _departmentPickerFlyout?.Hide();
    }

    private void OnClearDepartmentClick(object sender, RoutedEventArgs e)
    {
        if (_departmentList != null) _departmentList.SelectedItem = null;
        DepartmentColumn.Filter = null;
        _departmentPickerFlyout?.Hide();
    }

    // ----- Clear all -----

    private void OnClearAllFiltersClick(object sender, RoutedEventArgs e)
    {
        if (_departmentList != null) _departmentList.SelectedItem = null;
        // Single batch — TableView raises Filtered exactly once.
        PeopleTable.ClearAllFilters();
    }

    // ----- Filtered handler: consumer-owned re-shape -----

    private void OnTableFiltered(TableView sender, TableViewFilteredEventArgs args)
    {
        _filteredFiredCount++;
        Recompute();
        RefreshReadouts();
    }

    private void Recompute()
    {
        var filters = PeopleTable.FilteredColumns
            .Select(c => c.Filter)
            .Where(f => f != null)
            .ToList();

        IEnumerable<Person> visible = _master;
        if (filters.Count > 0)
        {
            visible = _master.Where(p => filters.All(f => f.Matches(p)));
        }

        var snapshot = visible.ToList();
        People.Clear();
        foreach (var p in snapshot)
        {
            People.Add(p);
        }
    }

    private void RefreshReadouts()
    {
        if (PeopleTable is null) return;

        FilteredFiredCountText.Text = _filteredFiredCount.ToString();

        var filteredColumns = PeopleTable.FilteredColumns;
        if (filteredColumns.Count == 0)
        {
            ActiveFiltersText.Text = "(none)";
        }
        else
        {
            ActiveFiltersText.Text = string.Join(
                "  ·  ",
                filteredColumns.Select(c => $"{ColumnLabel(c)} → {DescribeFilter(c.Filter)}"));
        }

        VisibleRowCountText.Text = $"{People.Count} of {_master.Count}";
    }

    private static string ColumnLabel(TableViewColumn column) =>
        column?.Header?.ToString() ?? "(unnamed)";

    private static string DescribeFilter(FilterDescription? filter)
    {
        if (filter is TableViewSimpleFilter simple)
        {
            return simple.Operator switch
            {
                TableViewFilterOperator.IsEmpty    => "IsEmpty",
                TableViewFilterOperator.IsNotEmpty => "IsNotEmpty",
                _ => $"{simple.Operator} \"{simple.Value}\"",
            };
        }
        return filter?.GetType().Name ?? "(none)";
    }
}
