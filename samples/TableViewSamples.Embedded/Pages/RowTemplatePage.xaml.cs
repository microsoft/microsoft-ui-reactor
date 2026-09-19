// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView.RowTemplate — the custom row-template hook
/// shipped in P3.2. RowTemplate is a DataTemplate. When set, TableView
/// replaces the per-column cell loop with a single instantiation of the
/// template's root for every realized data row. The template's
/// DataContext is the row's data item (a Person here). Setting the DP
/// to null restores the standard per-column cell path.
///
/// 2026-06-06: With fix-n7 the control auto-caches the Columns collection
/// when RowTemplate transitions non-null and auto-restores it on null —
/// consumers no longer need to capture/restore default columns manually.
///
/// Three independent templates (defined in XAML resources):
///   * <c>CardRowTemplate</c> — avatar disc + name + role/department + Active flag.
///   * <c>CompactRowTemplate</c> — two-line stacked name and email.
///   * <c>MixedRowTemplate</c> — avatar + name/role + department badge + salary.
///
/// Header row, selection visuals, and keyboard navigation continue to
/// work because they live OUTSIDE the cell host — RowTemplate only
/// replaces the cell-host content of data rows. Group-header rows are
/// unaffected (same scope rule as RowStyleSelector).
/// </summary>
public sealed partial class RowTemplatePage : Page
{
    public RowTemplatePage()
    {
        InitializeComponent();
        People = new ObservableCollection<Person>(PersonData.Take(60));
        ActiveTemplateText.Text = "(none) — default per-column cells";
    }

    public ObservableCollection<Person> People { get; }

    private void OnTemplateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeopleTable is null)
        {
            return;
        }

        switch (TemplatePicker.SelectedIndex)
        {
            case 0:
                PeopleTable.RowTemplate = null;
                ActiveTemplateText.Text = "(none) — default per-column cells";
                break;

            case 1:
                PeopleTable.RowTemplate = (DataTemplate)Resources["CardRowTemplate"];
                ActiveTemplateText.Text = "Card — avatar + name + role/department";
                break;

            case 2:
                PeopleTable.RowTemplate = (DataTemplate)Resources["CompactRowTemplate"];
                ActiveTemplateText.Text = "Compact — stacked name + email";
                break;

            case 3:
                PeopleTable.RowTemplate = (DataTemplate)Resources["MixedRowTemplate"];
                ActiveTemplateText.Text = "Mixed — avatar + name/role + dept badge + salary";
                break;
        }
    }
}
