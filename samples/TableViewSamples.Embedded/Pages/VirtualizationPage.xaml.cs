// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using TableViewSamples.Data;
using TableViewSamples.Models;
using Windows.Foundation;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates row virtualization (in-memory mode, P2.9) AND data
/// virtualization (incremental-loading mode, P3.12).
///
/// 2026-06-06: All visual-tree walks for PART_RowsRepeater / PART_BodyScroller
/// removed. The page now consumes the public TableView surface:
///   * RealizedRowCount / FirstRealizedIndex / LastRealizedIndex DPs
///   * RealizationChanged event for live readout refresh
///   * ViewChanged event to update body-offset readout
///   * ChangeView(...) to drive Home / Mid / End scroll buttons
/// No reflection, no FindDescendant, no generic instantiation gymnastics —
/// the page demonstrates the API consumers are supposed to use.
/// </summary>
public sealed partial class VirtualizationPage : Page
{
    private enum SourceMode { InMemory, Incremental }

    private const int IncrementalTotal = 1_000_000;
    private const int IncrementalPageSize = 1_000;
    private const int IncrementalSimulatedDelayMs = 30;

    public VirtualizationPage()
    {
        InitializeComponent();
        ApplyInMemoryAsActive();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public ObservableCollection<NumberedPerson> People { get; } = new();
    public IncrementalLoadingPersonCollection IncrementalPeople { get; } =
        new IncrementalLoadingPersonCollection(IncrementalTotal, IncrementalPageSize, IncrementalSimulatedDelayMs);

    private SourceMode _mode = SourceMode.InMemory;
    private bool _loadInFlight;

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        PeopleTable.RealizationChanged -= OnTableRealizationChanged;
        PeopleTable.ViewChanged -= OnTableViewChanged;
        PeopleTable.RealizationChanged += OnTableRealizationChanged;
        PeopleTable.ViewChanged += OnTableViewChanged;
        UpdateReadout();
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                if (_mode == SourceMode.InMemory && People.Count == 0)
                {
                    ApplyRowCount(10_000);
                    UpdateReadout();
                }
            });
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        PeopleTable.RealizationChanged -= OnTableRealizationChanged;
        PeopleTable.ViewChanged -= OnTableViewChanged;
    }

    private void OnTableRealizationChanged(TableView sender, TableViewRealizationChangedEventArgs args)
    {
        UpdateReadout();
    }

    private void OnTableViewChanged(TableView sender, TableViewViewChangedEventArgs args)
    {
        if (!args.IsIntermediate)
        {
            UpdateReadout();
        }
    }

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_mode != SourceMode.InMemory) return;
        if (SizeSelector?.SelectedItem is ComboBoxItem item
            && int.TryParse(item.Tag?.ToString(), out int n))
        {
            ApplyRowCount(n);
            DispatcherQueue.TryEnqueue(UpdateReadout);
        }
    }

    private void OnSourceModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceModeSelector?.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Tag?.ToString();
        if (tag == "Incremental")
        {
            ApplyIncrementalAsActive();
        }
        else
        {
            ApplyInMemoryAsActive();
        }
    }

    private void OnScrollHomeClick(object sender, RoutedEventArgs e)
        => PeopleTable.ChangeView(0.0, 0.0, null);

    private void OnScrollMidClick(object sender, RoutedEventArgs e)
        => PeopleTable.ChangeView(null, PeopleTable.ScrollableHeight / 2.0, null);

    private void OnScrollEndClick(object sender, RoutedEventArgs e)
        => PeopleTable.ChangeView(null, PeopleTable.ScrollableHeight, null);

    private void OnMeasureClick(object sender, RoutedEventArgs e) => UpdateReadout();

    private async void OnLoadMoreClick(object sender, RoutedEventArgs e)
    {
        if (_mode != SourceMode.Incremental) return;
        await LoadMoreIfPossibleAsync();
        UpdateReadout();
    }

    private void ApplyInMemoryAsActive()
    {
        _mode = SourceMode.InMemory;
        if (PeopleTable != null)
        {
            PeopleTable.ItemsSource = People;
        }
        if (SizeSelector != null) SizeSelector.IsEnabled = true;
        if (LoadMoreButton != null) LoadMoreButton.IsEnabled = false;
        if (SourceModeText != null) SourceModeText.Text = "In-memory";
        if (LoadStatsText != null) LoadStatsText.Text = "(in-memory mode)";
        UpdateReadout();
    }

    private void ApplyIncrementalAsActive()
    {
        _mode = SourceMode.Incremental;
        IncrementalPeople.Reset();
        if (PeopleTable != null)
        {
            PeopleTable.ItemsSource = IncrementalPeople;
        }
        if (SizeSelector != null) SizeSelector.IsEnabled = false;
        if (LoadMoreButton != null) LoadMoreButton.IsEnabled = true;
        if (SourceModeText != null) SourceModeText.Text = $"Incremental (~{IncrementalTotal:N0} virtual rows)";
        _ = LoadMoreIfPossibleAsync();
        UpdateReadout();
    }

    private void ApplyRowCount(int count)
    {
        People.Clear();
        var seed = PersonData.All;
        for (int i = 0; i < count; i++)
        {
            var p = seed[i % seed.Count];
            People.Add(new NumberedPerson
            {
                Id = i,
                FirstName = p.FirstName,
                LastName = p.LastName,
                Email = p.Email,
                Department = p.Department,
                Role = p.Role,
            });
        }
    }

    private async Task LoadMoreIfPossibleAsync()
    {
        if (_mode != SourceMode.Incremental) return;
        if (!IncrementalPeople.HasMoreItems) return;
        if (_loadInFlight) return;

        _loadInFlight = true;
        try
        {
            var op = IncrementalPeople.LoadMoreItemsAsync((uint)IncrementalPageSize);
            await op;
        }
        catch (Exception ex)
        {
            if (LoadStatsText != null)
            {
                LoadStatsText.Text = $"LoadMore error: {ex.Message}";
            }
        }
        finally
        {
            _loadInFlight = false;
            UpdateReadout();
        }
    }

    private void UpdateReadout()
    {
        int total = _mode == SourceMode.Incremental ? IncrementalPeople.Count : People.Count;
        if (TotalRowsText != null) TotalRowsText.Text = total.ToString("N0");

        // PeopleTable can be null while UpdateReadout runs SYNCHRONOUSLY during
        // InitializeComponent: the toolbar ComboBoxes set SelectedIndex in XAML, which
        // raises SelectionChanged -> OnSourceModeChanged -> ApplyInMemoryAsActive ->
        // UpdateReadout before the TableView (declared later in the XAML) has been
        // created and its x:Name field assigned. Reading PeopleTable.RealizedRowCount /
        // VerticalOffset here would throw inside LoadComponent and fail page navigation
        // (SEHException). Guard the table-derived readout like every other method in this
        // file already does; OnPageLoaded re-runs UpdateReadout once the table is wired.
        var table = PeopleTable;
        int realized = table?.RealizedRowCount ?? 0;
        bool ok = realized > 0;
        if (RealizedRowsText != null)
        {
            RealizedRowsText.Text = ok ? realized.ToString("N0") : "(not yet realized)";
        }
        if (RealizedRatioText != null)
        {
            if (ok && total > 0)
            {
                double pct = realized * 100.0 / total;
                RealizedRatioText.Text = pct.ToString("F2") + "%";
            }
            else
            {
                RealizedRatioText.Text = ok ? "0%" : "(not yet realized)";
            }
        }
        if (BodyOffsetText != null && table != null)
        {
            BodyOffsetText.Text = $"V={table.VerticalOffset:0}  H={table.HorizontalOffset:0}";
        }

        if (_mode == SourceMode.Incremental && LoadStatsText != null)
        {
            LoadStatsText.Text =
                $"loaded={IncrementalPeople.Count:N0}  remaining≈{IncrementalPeople.Remaining:N0}  loads={IncrementalPeople.LoadCount}  hasMore={IncrementalPeople.HasMoreItems}";
        }
    }
}

public sealed class NumberedPerson
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public sealed class IncrementalLoadingPersonCollection
    : ObservableCollection<NumberedPerson>, ISupportIncrementalLoading
{
    private readonly object _gate = new();
    private bool _loadInFlight;
    private int _generation;

    public IncrementalLoadingPersonCollection(int total, int pageSize, int delayMs)
    {
        Total = total;
        PageSize = pageSize;
        DelayMs = delayMs;
    }

    public int Total { get; }
    public int PageSize { get; }
    public int DelayMs { get; }
    public int LoadCount { get; private set; }
    public int Remaining => Math.Max(0, Total - Count);

    public bool HasMoreItems => Count < Total;

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
    {
        uint take;
        int generation;

        lock (_gate)
        {
            if (_loadInFlight)
            {
                return Task.FromResult(new LoadMoreItemsResult { Count = 0 }).AsAsyncOperation();
            }

            take = (uint)Math.Min((int)count, Math.Min(PageSize, Math.Max(0, Total - Count)));
            if (take == 0)
            {
                return Task.FromResult(new LoadMoreItemsResult { Count = 0 }).AsAsyncOperation();
            }

            _loadInFlight = true;
            generation = _generation;
        }

        return LoadMoreItemsAsyncCore(take, generation).AsAsyncOperation();
    }

    private async Task<LoadMoreItemsResult> LoadMoreItemsAsyncCore(uint count, int generation)
    {
        try
        {
            if (DelayMs > 0)
            {
                await Task.Delay(DelayMs).ConfigureAwait(true);
            }

            var seed = PersonData.All;
            uint appended = 0;
            lock (_gate)
            {
                if (generation != _generation)
                {
                    return new LoadMoreItemsResult { Count = 0 };
                }

                int startIndex = Count;
                int end = Math.Min(Total, startIndex + (int)count);
                for (int i = startIndex; i < end; i++)
                {
                    var p = seed[i % seed.Count];
                    Add(new NumberedPerson
                    {
                        Id = i,
                        FirstName = p.FirstName,
                        LastName = p.LastName,
                        Email = p.Email,
                        Department = p.Department,
                        Role = p.Role,
                    });
                    appended++;
                }

                if (appended > 0)
                {
                    LoadCount++;
                }
            }

            return new LoadMoreItemsResult { Count = appended };
        }
        finally
        {
            lock (_gate)
            {
                if (generation == _generation)
                {
                    _loadInFlight = false;
                }
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _generation++;
            _loadInFlight = false;
            Clear();
            LoadCount = 0;
        }
    }
}
