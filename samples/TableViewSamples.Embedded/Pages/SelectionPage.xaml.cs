// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView's four selection modes (None / Single / Multiple /
/// Extended) and the public selection surface: SelectedItem, SelectedIndex,
/// SelectedItems, SelectedIndices, SelectAll/ClearSelection helpers, and the
/// SelectionChanged event.
/// </summary>
public sealed partial class SelectionPage : Page
{
    private const string VerifySelectionArgument = "--verify-selection-gutter";

    private int _changeCount;
    private bool _verificationStarted;

    public SelectionPage()
    {
        People = PersonData.Take(50);

        try
        {
            InitializeComponent();
            App.AppendSelectionVerificationLog("SelectionPageInitializeComponentComplete");
        }
        catch (Exception ex)
        {
            App.AppendSelectionVerificationLog($"SelectionPageInitializeComponentFailed {ex.GetType().FullName}: {ex.Message}");
            App.AppendSelectionVerificationLog(ex.ToString());
            throw;
        }

        Loaded += (_, _) =>
        {
            UpdateModeDescription((ModeCombo?.SelectedItem as ComboBoxItem)?.Content as string);
            RefreshReadout();
        };

        if (Environment.GetCommandLineArgs().Any(static arg => string.Equals(arg, VerifySelectionArgument, StringComparison.OrdinalIgnoreCase)))
        {
            Loaded += OnLoadedForVerification;
            App.AppendSelectionVerificationLog("SelectionPageLoadedHandlerAttached");
        }
    }

    public ObservableCollection<Person> People { get; }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeopleTable is null || ModeCombo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var label = item.Content as string;
        PeopleTable.SelectionMode = label switch
        {
            "None"     => TableViewSelectionMode.None,
            "Single"   => TableViewSelectionMode.Single,
            "Multiple" => TableViewSelectionMode.Multiple,
            _          => TableViewSelectionMode.Extended,
        };
        UpdateModeDescription(label);
        RefreshReadout();
    }

    private void OnGutterToggled(object sender, RoutedEventArgs e)
    {
        if (PeopleTable is null)
        {
            return;
        }

        // With the control honoring IsSelectionGutterVisible on every path, the
        // gutter is decoupled from multi-select: the leading checkbox column shows
        // (on) or hides (off) while Multiple/Extended selection keeps working.
        var on = GutterToggle.IsOn;
        PeopleTable.IsSelectionGutterVisible = on;
        PeopleTable.HeadersVisibility = on
            ? TableViewHeadersVisibility.All
            : TableViewHeadersVisibility.Column;
    }

    private void UpdateModeDescription(string? mode)
    {
        if (ModeDescriptionText is null)
        {
            return;
        }

        // NOTE: TableView currently treats Multiple and Extended identically —
        // same SelectionModel.SingleSelect(false). The checkbox gutter is now
        // controlled independently by the Selection gutter toggle, so multi-select
        // works whether or not the gutter is showing.
        ModeDescriptionText.Text = mode switch
        {
            "None"     => "Rows can't be selected — the selection readouts stay empty.",
            "Single"   => "Click a row to select it. Picks exactly one row at a time — the previous selection clears.",
            "Multiple" => "Multi-select on. Toggle the Selection gutter switch to show or hide the leading checkbox column — selection works either way.",
            _          => "Multi-select on. Toggle the Selection gutter switch to show or hide the leading checkbox column — selection works either way.",
        };
    }

    private void OnLoadedForVerification(object sender, RoutedEventArgs e)
    {
        if (_verificationStarted)
        {
            return;
        }

        _verificationStarted = true;
        Loaded -= OnLoadedForVerification;
        App.AppendSelectionVerificationLog("SelectionPageLoaded");
        _ = RunVerificationAsync();
    }

    private async Task RunVerificationAsync()
    {
        try
        {
            await Task.Delay(250);
            ModeCombo.SelectedIndex = 2;
            await Task.Delay(250);

            // Exercise the new Selection gutter toggle: with it on, the checkbox
            // gutter must appear in Multiple mode (the assertion below checks it).
            GutterToggle.IsOn = true;
            await Task.Delay(250);

            PeopleTable.UpdateLayout();
            UpdateLayout();
            await Task.Delay(250);

            App.AppendSelectionVerificationLog($"SelectionMode={PeopleTable.SelectionMode}");
            App.AppendSelectionVerificationLog($"IsSelectionGutterVisible={PeopleTable.IsSelectionGutterVisible}");
            App.AppendSelectionVerificationLog($"SelectAllState={PeopleTable.SelectAllState}");
            App.AppendSelectionVerificationLog($"RowCount={PeopleTable.RowCount}");

            PeopleTable.Select(0);
            await Task.Delay(250);
            PeopleTable.UpdateLayout();
            App.AppendSelectionVerificationLog($"SelectedCountAfterSelect={PeopleTable.SelectedItems.Count}");
            App.AppendSelectionVerificationLog($"SelectAllStateAfterSelect={PeopleTable.SelectAllState}");

            PeopleTable.SelectAll();
            await Task.Delay(150);
            App.AppendSelectionVerificationLog($"SelectAllStateAfterSelectAll={PeopleTable.SelectAllState}");

            PeopleTable.DeselectAll();
            await Task.Delay(150);
            App.AppendSelectionVerificationLog($"SelectAllStateAfterClear={PeopleTable.SelectAllState}");

            var passed = PeopleTable.SelectionMode == TableViewSelectionMode.Multiple &&
                         PeopleTable.IsSelectionGutterVisible &&
                         PeopleTable.RowCount > 0;
            App.AppendSelectionVerificationLog($"SelectionGutterVerification={(passed ? "PASS" : "FAIL")}");
        }
        catch (Exception ex)
        {
            App.AppendSelectionVerificationLog($"SelectionGutterVerificationFailed {ex.GetType().FullName}: {ex.Message}");
            App.AppendSelectionVerificationLog(ex.ToString());
            throw;
        }
    }

    private void OnSelectFirstClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (People.Count == 0) return;
        PeopleTable.Select(0);
    }

    private void OnSelectLastClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (People.Count == 0) return;
        PeopleTable.Select(People.Count - 1);
    }

    private void OnSelectAllClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        PeopleTable.SelectAll();
    }

    private void OnClearClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        PeopleTable.DeselectAll();
    }

    private void OnInvertClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var currentlySelected = new System.Collections.Generic.HashSet<int>(PeopleTable.SelectedIndices);
        for (int i = 0; i < People.Count; i++)
        {
            if (currentlySelected.Contains(i))
            {
                PeopleTable.Deselect(i);
            }
            else
            {
                PeopleTable.Select(i);
            }
        }
    }

    private void OnTableSelectionChanged(TableView sender, TableViewSelectionChangedEventArgs args)
    {
        _changeCount++;
        RefreshReadout();
    }

    private void RefreshReadout()
    {
        // Reachable during InitializeComponent (ModeCombo sets SelectedIndex in XAML, which
        // raises SelectionChanged synchronously before the Options-rail readout TextBlocks
        // below it are created). Guard so the init-time call no-ops; Loaded re-runs it.
        if (PeopleTable is null || SelectedCountText is null) return;

        SelectedCountText.Text = PeopleTable.SelectedItems.Count.ToString();
        SelectedIndexText.Text = PeopleTable.SelectedIndex.ToString();
        ChangeCountText.Text = _changeCount.ToString();

        var indices = PeopleTable.SelectedIndices;
        SelectedIndicesText.Text = indices.Count == 0
            ? "(none)"
            : string.Join(", ", indices);

        var items = PeopleTable.SelectedItems;
        if (items.Count == 0)
        {
            SelectedItemsText.Text = "(none)";
        }
        else
        {
            var preview = items
                .Cast<Person>()
                .Take(5)
                .Select(p => $"{p.FirstName} {p.LastName}");
            var sb = new StringBuilder(string.Join(", ", preview));
            if (items.Count > 5)
            {
                sb.Append(", … (+").Append(items.Count - 5).Append(" more)");
            }
            SelectedItemsText.Text = sb.ToString();
        }
    }
}
