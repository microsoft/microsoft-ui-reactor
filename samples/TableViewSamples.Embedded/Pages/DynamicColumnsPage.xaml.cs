// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates the <c>TableViewColumn.Visibility</c> DP (A2 — Wave-4a):
/// toggling a checkbox flips a single column's Visibility between Visible and
/// Collapsed. The column object stays in the Columns vector at the same index,
/// so Width, sort state, filter, and frozen-edge are all preserved across
/// toggles — no vector mutation, no column rebuild.
/// </summary>
public sealed partial class DynamicColumnsPage : Page, INotifyPropertyChanged
{
    private bool _visibleFirstName = true;
    private bool _visibleLastName = true;
    private bool _visibleDepartment = true;
    private bool _visibleRole = true;
    private bool _visibleSalary = true;

    public DynamicColumnsPage()
    {
        InitializeComponent();
        People = PersonData.Take(50);
    }

    public ObservableCollection<Person> People { get; }

    public bool VisibleFirstName  { get => _visibleFirstName;  set => SetVisibility(ref _visibleFirstName,  value, VisFirstNameColumn); }
    public bool VisibleLastName   { get => _visibleLastName;   set => SetVisibility(ref _visibleLastName,   value, VisLastNameColumn); }
    public bool VisibleDepartment { get => _visibleDepartment; set => SetVisibility(ref _visibleDepartment, value, VisDepartmentColumn); }
    public bool VisibleRole       { get => _visibleRole;       set => SetVisibility(ref _visibleRole,       value, VisRoleColumn); }
    public bool VisibleSalary     { get => _visibleSalary;     set => SetVisibility(ref _visibleSalary,     value, VisSalaryColumn); }

    private void SetVisibility(ref bool field, bool value, TableViewTextColumn? column, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        // The single point of truth: flip the column's Visibility DP. The
        // TableView's per-column callback cancels any in-flight edit, rebuilds
        // the header strip, and walks every realized row so the matching cell
        // wrapper tracks the new value. No Columns-vector mutation; column
        // identity and Width are preserved.
        if (column is not null)
        {
            column.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
