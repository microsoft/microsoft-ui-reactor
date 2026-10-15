// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TableViewSamples.Data;
using TableViewSamples.Models;
using Windows.System;

namespace TableViewSamples.Pages;

/// <summary>
/// Pages a large source list (<see cref="PersonData.All"/> = 1,000 entries)
/// into <see cref="TableView"/> 25 / 50 / 100 rows at a time. The page-window
/// is held in <see cref="PageRows"/>; the source list itself is never bound
/// to TableView so we don't realize all 1,000 rows.
///
/// The ObservableCollection is reused (Clear + re-Add) rather than replaced
/// so x:Bind doesn't have to walk the property chain on every paging click.
/// </summary>
public sealed partial class PaginationPage : Page
{
    private readonly IReadOnlyList<Person> _source = PersonData.All;
    private int _pageSize = 50;
    private int _currentPage = 1; // 1-based for UI

    public PaginationPage()
    {
        InitializeComponent();
        PageRows = new ObservableCollection<Person>();
        Loaded += (_, _) => RefreshPage();
    }

    public ObservableCollection<Person> PageRows { get; }

    private int TotalRows => _source.Count;

    private int TotalPages => Math.Max(1, (TotalRows + _pageSize - 1) / _pageSize);

    private void RefreshPage()
    {
        if (_currentPage < 1) _currentPage = 1;
        if (_currentPage > TotalPages) _currentPage = TotalPages;

        int startIndex = (_currentPage - 1) * _pageSize;
        int endExclusive = Math.Min(startIndex + _pageSize, TotalRows);

        PageRows.Clear();
        for (int i = startIndex; i < endExclusive; i++)
        {
            PageRows.Add(_source[i]);
        }

        // Show 1-based ranges in the status caption.
        var startDisplay = startIndex + 1;
        RangeStatusText.Text = string.Create(CultureInfo.InvariantCulture,
            $"Showing rows {startDisplay:N0}-{endExclusive:N0} of {TotalRows:N0}. Page size {_pageSize}.");

        PageNumberBox.Text = _currentPage.ToString(CultureInfo.InvariantCulture);
        PageOfText.Text = string.Create(CultureInfo.InvariantCulture, $"of {TotalPages:N0}");

        FirstPageButton.IsEnabled = _currentPage > 1;
        PrevPageButton.IsEnabled = _currentPage > 1;
        NextPageButton.IsEnabled = _currentPage < TotalPages;
        LastPageButton.IsEnabled = _currentPage < TotalPages;
    }

    private void OnFirstClick(object sender, RoutedEventArgs e)
    {
        _currentPage = 1;
        RefreshPage();
    }

    private void OnPrevClick(object sender, RoutedEventArgs e)
    {
        _currentPage = Math.Max(1, _currentPage - 1);
        RefreshPage();
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        _currentPage = Math.Min(TotalPages, _currentPage + 1);
        RefreshPage();
    }

    private void OnLastClick(object sender, RoutedEventArgs e)
    {
        _currentPage = TotalPages;
        RefreshPage();
    }

    private void OnPageSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageSizeCombo?.SelectedItem is not string selected) return;
        if (!int.TryParse(selected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var newSize)) return;
        if (newSize <= 0 || newSize == _pageSize) return;

        // Try to keep the user near the same row when the page size changes:
        // find the first-row index of the current page, then recompute which
        // page it belongs to under the new size.
        int firstRowOnOldPage = (_currentPage - 1) * _pageSize;
        _pageSize = newSize;
        _currentPage = (firstRowOnOldPage / _pageSize) + 1;
        RefreshPage();
    }

    private void OnPageNumberKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitPageNumber();
            e.Handled = true;
        }
    }

    private void OnPageNumberLostFocus(object sender, RoutedEventArgs e)
    {
        CommitPageNumber();
    }

    private void CommitPageNumber()
    {
        if (PageNumberBox is null) return;
        if (int.TryParse(PageNumberBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requested) && requested >= 1)
        {
            _currentPage = Math.Min(requested, TotalPages);
        }
        // If parse failed, RefreshPage() will rewrite the box to the current
        // (still-valid) page number, giving the user clear feedback.
        RefreshPage();
    }
}
