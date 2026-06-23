// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView's P3.6 N-level hierarchy support.
///
///   * The control ships an opt-in HierarchicalItemsSource DP that takes any
///     tree-shaped source (each node is itself enumerable to its children, or
///     exposes them via the property named in HierarchicalChildrenPropertyName).
///     We hand it an ObservableCollection&lt;OrgNode&gt; modelling an employee
///     reporting tree (CEO → leaders → managers → individual contributors);
///     HierarchicalChildrenPropertyName="Children" tells the framework's
///     HierarchicalSourceAdapter (the P13 primitive) which property to read.
///
///   * Internally TableView creates a HierarchicalSourceAdapter and points the
///     rows ItemsRepeater at the adapter's Entries observable vector. Each
///     visible row carries a HierarchicalEntry sentinel (Item / Depth /
///     HasChildren / IsExpanded) which TableViewRow.RealizeCells unwraps for
///     cell bindings. Every realized non-empty row gets a chevron + indent
///     border prepended to PART_CellHost; the chevron's Tapped handler routes
///     to TableView.ToggleItem and stops the bubble so row-selection is not
///     toggled.
///
///   * Source precedence: HierarchicalItemsSource &gt; GroupedItemsSource &gt;
///     ItemsSource. Setting more than one logs a debug-only warning. Switching
///     hierarchical source resets selection cleanly before re-binding.
///
///   * Row reorder is gated off in hierarchical mode (mirrors grouped mode):
///     the adapter owns the displayed order so the reorder gestures would have
///     no well-defined target index. CanReorderRows reads false here.
///
///   * Per-row state — IsExpanded — is owned by the adapter. ExpandItem /
///     CollapseItem / ToggleItem / IsItemExpanded forward straight through.
///     Adapter Refreshed events fire on every toggle but the control's
///     handler is intentionally a no-op — adapter-driven VectorChanged events
///     keep the repeater + selection model in sync without needing a wipe.
/// </summary>
public sealed partial class HierarchyPage : Page
{
    public HierarchyPage()
    {
        InitializeComponent();

        Roots = BuildOrgTree();

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        DetachTableHandlers();
        OrgTable.SelectionChanged += OnSelectionChanged;
        OrgTable.Sorted += OnSorted;
        OrgTable.Filtered += OnFiltered;

        // Expand the CEO and the leader tier on first show so the tree-grid
        // immediately reads as a multi-level reporting chain (CEO → leaders →
        // managers) rather than a single collapsed root. Users still have
        // Expand-all / Collapse-all and per-chevron toggles to drill further.
        foreach (var root in Roots)
        {
            OrgTable.ExpandItem(root);
            foreach (var report in root.Children)
            {
                OrgTable.ExpandItem(report);
            }
        }

        UpdateReadout("loaded");
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        DetachTableHandlers();
    }

    private void DetachTableHandlers()
    {
        if (OrgTable is null)
        {
            return;
        }

        OrgTable.SelectionChanged -= OnSelectionChanged;
        OrgTable.Sorted -= OnSorted;
        OrgTable.Filtered -= OnFiltered;
    }

    private void OnSelectionChanged(TableView sender, TableViewSelectionChangedEventArgs e)
    {
        UpdateReadout("selection");
    }

    private void OnSorted(TableView sender, TableViewSortedEventArgs e)
    {
        UpdateReadout("sort");
    }

    private void OnFiltered(TableView sender, TableViewFilteredEventArgs e)
    {
        UpdateReadout("filter");
    }

    public ObservableCollection<OrgNode> Roots { get; }

    // ----- Button handlers -----

    private void OnExpandAllClick(object sender, RoutedEventArgs e)
    {
        // SMP-CTL-2: the control's bulk tree-wide expand. Replaces the prior
        // hand-rolled WalkTree(ExpandItem) recursion and drives the hierarchical
        // adapter directly (covers not-yet-realized descendants).
        OrgTable.ExpandAllItems();
        UpdateReadout("expand-all");
    }

    private void OnCollapseAllClick(object sender, RoutedEventArgs e)
    {
        // SMP-CTL-2: the control's bulk tree-wide collapse (adapter-driven; no
        // need to fold children-before-parents by hand as the old WalkTree did).
        OrgTable.CollapseAllItems();
        UpdateReadout("collapse-all");
    }

    private void OnToggleSelectedClick(object sender, RoutedEventArgs e)
    {
        if (OrgTable.SelectedItem is OrgNode node)
        {
            OrgTable.ToggleItem(node);
            UpdateReadout($"toggle({node.Name}) → IsExpanded={OrgTable.IsItemExpanded(node)}");
        }
        else
        {
            UpdateReadout("toggle: (no selection)");
        }
    }

    // ----- Helpers -----

    private void UpdateReadout(string action)
    {
        IsHierarchicalText.Text = OrgTable.IsHierarchical.ToString();

        var name = string.IsNullOrEmpty(OrgTable.HierarchicalChildrenPropertyName)
            ? "(default — duck-typed iteration)"
            : OrgTable.HierarchicalChildrenPropertyName;
        ChildrenPropertyNameText.Text = name;

        SelectedNodeText.Text = OrgTable.SelectedItem is OrgNode node
            ? $"{node.Name} (Depth={DepthOf(node)}, HasChildren={node.Children.Count > 0}, IsExpanded={OrgTable.IsItemExpanded(node)})"
            : "(none)";

        SortedColumnsText.Text = FormatColumnList(OrgTable.SortedColumns, column =>
            $"{column.Header} ({column.SortDirection})");
        FilteredColumnsText.Text = FormatColumnList(OrgTable.FilteredColumns, column =>
            $"{column.Header}");
        LastActionText.Text = action;
    }

    private static string FormatColumnList<T>(IReadOnlyList<T> columns, System.Func<T, string> formatter)
    {
        if (columns == null || columns.Count == 0)
        {
            return "(none)";
        }

        return string.Join(", ", columns.Select(formatter));
    }

    private int DepthOf(OrgNode target)
    {
        return DepthOf(Roots, target, 0);
    }

    private static int DepthOf(IEnumerable<OrgNode> nodes, OrgNode target, int depth)
    {
        foreach (var n in nodes)
        {
            if (ReferenceEquals(n, target))
            {
                return depth;
            }
            var found = DepthOf(n.Children, target, depth + 1);
            if (found >= 0)
            {
                return found;
            }
        }
        return -1;
    }

    // ----- Sample data -----

    private static ObservableCollection<OrgNode> BuildOrgTree()
    {
        // A single CEO root with the org fanning out CEO → leaders → managers →
        // individual contributors — a genuine N-level reporting tree, not a
        // department→members grouping. Leaders are named; managers and ICs are
        // drawn deterministically from the shared PersonData pool so every row is
        // a real person.
        var pools = new Dictionary<string, Queue<Person>>(StringComparer.Ordinal);
        Person Next(string dept)
        {
            if (!pools.TryGetValue(dept, out var queue))
            {
                queue = new Queue<Person>(
                    PersonData.All.Where(p => string.Equals(p.Department, dept, StringComparison.Ordinal)));
                pools[dept] = queue;
            }
            return queue.Count > 0
                ? queue.Dequeue()
                : new Person { FirstName = "Open", LastName = "Role", Department = dept, Role = "Open role" };
        }

        static string Building(string dept) => dept switch
        {
            "Executive" => "Bldg 34",
            "Product" => "Bldg 36",
            "Sales" => "Bldg 88",
            "Marketing" => "Studio B",
            "Finance" => "Bldg 17",
            "Design" => "Studio H",
            _ => "Bldg 1",
        };

        OrgNode FromPerson(Person p, string? title = null) =>
            new() { Name = $"{p.FirstName} {p.LastName}", Owner = title ?? p.Role, Location = Building(p.Department) };

        var ceo = new OrgNode { Name = "Satya Nadella", Owner = "Chief Executive Officer", Location = Building("Executive") };

        void AddOrg(string leaderName, string title, string dept)
        {
            var leader = new OrgNode { Name = leaderName, Owner = title, Location = Building(dept) };
            for (var m = 0; m < 2; m++)
            {
                var manager = FromPerson(Next(dept), $"{dept} Manager");
                for (var ic = 0; ic < 3; ic++)
                {
                    manager.Children.Add(FromPerson(Next(dept)));
                }
                leader.Children.Add(manager);
            }
            ceo.Children.Add(leader);
        }

        // Satya's direct reports — each leads one department.
        AddOrg("Rajesh Jha",     "EVP, Experiences & Devices",     "Product");
        AddOrg("Judson Althoff", "EVP & Chief Commercial Officer", "Sales");
        AddOrg("Takeshi Numoto", "EVP & Chief Marketing Officer",  "Marketing");
        AddOrg("Amy Hood",       "EVP & Chief Financial Officer",  "Finance");
        AddOrg("Jon Friedman",   "CVP, Design & Research",         "Design");

        AssignHeadcount(ceo);
        return new ObservableCollection<OrgNode> { ceo };
    }

    // Headcount = number of people in each node's org including the node itself
    // (IC = 1, manager = 1 + reports, … CEO = the whole company).
    private static int AssignHeadcount(OrgNode node)
    {
        var size = 1;
        foreach (var child in node.Children)
        {
            size += AssignHeadcount(child);
        }
        node.Headcount = size;
        return size;
    }
}

/// <summary>
/// Tree node the page hands to TableView.HierarchicalItemsSource.
/// HierarchicalChildrenPropertyName="Children" tells the adapter to read
/// the Children property for each node.
/// </summary>
public sealed class OrgNode : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _owner = string.Empty;
    private string _location = string.Empty;
    private int _headcount;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public string Owner
    {
        get => _owner;
        set => Set(ref _owner, value);
    }

    public string Location
    {
        get => _location;
        set => Set(ref _location, value);
    }

    public int Headcount
    {
        get => _headcount;
        set => Set(ref _headcount, value);
    }

    public ObservableCollection<OrgNode> Children { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName!));
        }
    }
}
