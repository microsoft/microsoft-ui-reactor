// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView.GridLinesVisibility (None / Horizontal / Vertical / All)
/// for WPF DataGrid parity. The same DP is exercised across flat, grouped, and
/// hierarchical modes — and against both default theme banding and a custom row
/// background — so reviewers can verify the lines stay correct through group
/// headers, chevron expanders, and tinted rows.
/// </summary>
public sealed partial class GridLinesVisibilityPage : Page, INotifyPropertyChanged
{
    private enum LayoutMode
    {
        Flat,
        Grouped,
        Hierarchical,
    }

    private static readonly string[] s_curatedDepartments =
    {
        "Marketing",
        "Sales",
        "Design",
        "Product",
        "Finance",
    };

    private readonly ObservableCollection<Person> _flatRows = new();
    private readonly List<DepartmentGroup> _groupedRows;
    private readonly ObservableCollection<DepartmentNode> _hierarchyRoots;

    private LayoutMode _mode = LayoutMode.Flat;
    private TableViewGridLinesVisibility _lines = TableViewGridLinesVisibility.All;
    private string _statusText = string.Empty;

    public GridLinesVisibilityPage()
    {
        foreach (var person in PersonData.Take(40))
        {
            _flatRows.Add(person);
        }

        var curated = BuildCuratedPeople();
        _groupedRows = BuildGroupedView(curated);
        _hierarchyRoots = BuildHierarchy(_groupedRows);

        InitializeComponent();

        ApplyMode(LayoutMode.Flat);
        ApplyBanding();
        UpdateStatus();
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    private void OnGridLinesVisibilityChecked(object sender, RoutedEventArgs e)
    {
        if (DemoTable is null || sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        // Case-sensitive on purpose so a typo trips Debug.Fail rather than
        // silently defaulting to Horizontal.
        if (!Enum.TryParse<TableViewGridLinesVisibility>(tag, ignoreCase: false, out var value))
        {
            Debug.Fail($"GridLinesVisibilityPage: unrecognised grid-lines Tag '{tag}'.");
            return;
        }

        _lines = value;
        DemoTable.GridLinesVisibility = value;
        UpdateStatus();
    }

    private void OnModeRadioChecked(object sender, RoutedEventArgs e)
    {
        if (DemoTable is null || sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        if (!Enum.TryParse<LayoutMode>(tag, ignoreCase: false, out var mode))
        {
            Debug.Fail($"GridLinesVisibilityPage: unrecognised mode Tag '{tag}'.");
            return;
        }

        ApplyMode(mode);
        UpdateStatus();
    }

    private void OnBandingRadioChecked(object sender, RoutedEventArgs e)
    {
        if (DemoTable is null)
        {
            return;
        }

        ApplyBanding();
        UpdateStatus();
    }

    private void ApplyMode(LayoutMode mode)
    {
        // Reachable during InitializeComponent if a mode RadioButton has IsChecked="True"
        // in XAML (Checked raises synchronously). DemoTable lives in the Example slot and is
        // created before the Options radios, but guard defensively; the ctor re-applies.
        if (DemoTable is null) return;

        _mode = mode;

        DemoTable.ItemsSource = null;
        DemoTable.GroupedItemsSource = null;
        DemoTable.HierarchicalItemsSource = null;

        switch (_mode)
        {
            case LayoutMode.Flat:
                DemoTable.ItemsSource = _flatRows;
                break;

            case LayoutMode.Grouped:
                DemoTable.GroupedItemsSource = _groupedRows;
                break;

            case LayoutMode.Hierarchical:
                DemoTable.HierarchicalChildrenPropertyName = nameof(DepartmentNode.Children);
                DemoTable.HierarchicalItemsSource = _hierarchyRoots;
                DemoTable.UpdateLayout();
                foreach (var root in _hierarchyRoots)
                {
                    DemoTable.ExpandItem(root);
                }
                break;
        }
    }

    private void ApplyBanding()
    {
        // Reachable during InitializeComponent (a banding RadioButton with IsChecked="True"
        // raises Checked before CustomBandingRadio, declared later in the Options rail, is
        // created). Guard so the init-time call no-ops; the ctor re-runs ApplyBanding.
        if (DemoTable is null || CustomBandingRadio is null) return;

        DemoTable.RowBackground = null;
        DemoTable.AlternatingRowBackground = null;

        if (CustomBandingRadio.IsChecked == true)
        {
            DemoTable.RowBackground = Application.Current.Resources["SampleCustomRowBackgroundBrush"] as Brush;
            DemoTable.AlternatingRowBackground = Application.Current.Resources["SampleCustomAlternatingRowBackgroundBrush"] as Brush;
        }
    }

    private void UpdateStatus()
    {
        var banding = CustomBandingRadio is not null && CustomBandingRadio.IsChecked == true ? "custom banding" : "theme banding";
        var lines = _lines switch
        {
            TableViewGridLinesVisibility.None       => "no grid lines",
            TableViewGridLinesVisibility.Horizontal => "horizontal lines (row separators)",
            TableViewGridLinesVisibility.Vertical   => "vertical lines (column separators)",
            TableViewGridLinesVisibility.All        => "all grid lines (rows + columns)",
            _ => _lines.ToString(),
        };

        StatusText = _mode switch
        {
            LayoutMode.Flat         => $"Flat · {_flatRows.Count:N0} rows · {lines} · {banding}",
            LayoutMode.Grouped      => $"Grouped · {_groupedRows.Count:N0} departments · {_groupedRows.Sum(g => g.Count):N0} people · {lines} · {banding}",
            LayoutMode.Hierarchical => $"Hierarchical · {_hierarchyRoots.Count:N0} department roots · {_hierarchyRoots.Sum(r => r.Children.Count):N0} child rows · {lines} · {banding}",
            _ => string.Empty,
        };
    }

    private static IReadOnlyList<Person> BuildCuratedPeople()
    {
        return s_curatedDepartments
            .SelectMany(department => PersonData.All
                .Where(p => string.Equals(p.Department, department, StringComparison.Ordinal))
                .Take(8))
            .ToList();
    }

    private static List<DepartmentGroup> BuildGroupedView(IEnumerable<Person> people)
    {
        return s_curatedDepartments
            .Select(department => new DepartmentGroup(department, people.Where(p => string.Equals(p.Department, department, StringComparison.Ordinal))))
            .Where(group => group.Count > 0)
            .ToList();
    }

    private static ObservableCollection<DepartmentNode> BuildHierarchy(IEnumerable<DepartmentGroup> groups)
    {
        var roots = new ObservableCollection<DepartmentNode>();
        foreach (var group in groups)
        {
            var root = new DepartmentNode
            {
                FirstName = group.Department,
                LastName = "Department",
                Department = group.Department,
                Role = $"{group.Count} people",
                Email = "Expand to see team members",
            };

            foreach (var person in group.Take(6))
            {
                root.Children.Add(new DepartmentNode
                {
                    FirstName = person.FirstName,
                    LastName = person.LastName,
                    Department = person.Department,
                    Role = person.Role,
                    Email = person.Email,
                });
            }

            roots.Add(root);
        }

        return roots;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public sealed class DepartmentGroup : List<Person>
    {
        public DepartmentGroup(string department, IEnumerable<Person> people) : base(people)
        {
            Department = department;
        }

        public string Department { get; }

        public override string ToString() => Department;
    }

    public sealed class DepartmentNode
    {
        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public string Department { get; init; } = string.Empty;

        public string Role { get; init; } = string.Empty;

        public ObservableCollection<DepartmentNode> Children { get; } = new();
    }
}
