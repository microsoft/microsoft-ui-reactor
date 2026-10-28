// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates the P2.10 keyboard nav + the public RowCount / ColumnCount /
/// VisibleRowCount surface.
///
/// On-table key handling routes Up/Down/Home/End/PageUp/PageDown
/// through GridCoordinateHelper(rowCount, 1).TryGetNextFocusableCell
/// then realizes the target row via ItemsRepeater.GetOrCreateElement
/// + StartBringIntoView + Focus(Keyboard).
///
/// The readout binds to TableView.RowCount / ColumnCount directly — the
/// UIA IGridProvider surface is computed from the same source, so this
/// page no longer needs to walk the automation tree to display the
/// dimensions.
/// </summary>
public sealed partial class KeyboardNavPage : Page
{
    public KeyboardNavPage()
    {
        InitializeComponent();

        // Person has no Id/Index property, so project the shared dataset into
        // a lightweight row that carries a stable 1-based "#" for the leading
        // column (same idiom as StickyHeadersPage / RTLPlaygroundPage). The
        // ordinal also makes Home/End/PageUp/PageDown movement obvious.
        int number = 1;
        foreach (var p in PersonData.All)
        {
            People.Add(new KeyboardNavRow
            {
                Number = number++,
                FirstName = p.FirstName,
                LastName = p.LastName,
                Email = p.Email,
                Department = p.Department,
                Role = p.Role,
            });
        }

        Loaded += (_, _) => UpdateReadout();
        PeopleTable.SizeChanged += (_, _) => UpdateReadout();
    }

    public ObservableCollection<KeyboardNavRow> People { get; } = new();

    private void OnSelectionChanged(TableView sender, TableViewSelectionChangedEventArgs args)
    {
        SelectedIndexText.Text = PeopleTable.SelectedIndex.ToString();
    }

    private void UpdateReadout()
    {
        RowCountText.Text = PeopleTable.RowCount.ToString();
        ColumnCountText.Text = PeopleTable.ColumnCount.ToString();
        MajorText.Text = "RowMajor";
    }

    /// <summary>
    /// Per-row DTO so the leading "#" column shows a stable 1-based row number.
    /// Models.Person exposes no Id/Index, so the page projects the shared
    /// dataset into this lightweight row (mirrors StickyHeadersPage /
    /// RTLPlaygroundPage).
    /// </summary>
    public sealed class KeyboardNavRow
    {
        public int Number { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}
