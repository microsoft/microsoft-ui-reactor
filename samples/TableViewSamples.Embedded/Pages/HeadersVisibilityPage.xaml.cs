// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView.HeadersVisibility for column headers, row-header
/// selection gutter, both, or neither.
/// </summary>
public sealed partial class HeadersVisibilityPage : Page
{
    public HeadersVisibilityPage()
    {
        InitializeComponent();

        for (var row = 1; row <= 20; row++)
        {
            Rows.Add(new HeaderVisibilitySampleRow
            {
                Value1 = $"R{row}C1",
                Value2 = $"R{row}C2",
                Value3 = $"R{row}C3",
                Value4 = $"R{row}C4",
                Value5 = $"R{row}C5",
            });
        }
    }

    public ObservableCollection<HeaderVisibilitySampleRow> Rows { get; } = new();

    private void OnHeadersVisibilityChecked(object sender, RoutedEventArgs e)
    {
        if (DemoTable is null || sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        DemoTable.HeadersVisibility = tag switch
        {
            "Column" => TableViewHeadersVisibility.Column,
            "Row" => TableViewHeadersVisibility.Row,
            "None" => TableViewHeadersVisibility.None,
            _ => TableViewHeadersVisibility.All,
        };
    }
}

public sealed class HeaderVisibilitySampleRow
{
    public string Value1 { get; set; } = string.Empty;
    public string Value2 { get; set; } = string.Empty;
    public string Value3 { get; set; } = string.Empty;
    public string Value4 { get; set; } = string.Empty;
    public string Value5 { get; set; } = string.Empty;
}
