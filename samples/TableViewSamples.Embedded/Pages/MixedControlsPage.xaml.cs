// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates that <c>TableViewTemplateColumn</c> can host arbitrary
/// XAML controls — not just text.  Four picker controls (DatePicker,
/// TimePicker, ComboBox, CheckBox) are placed directly inside cells via
/// <c>CellTemplate</c> DataTemplates and bound TwoWay to the underlying
/// <see cref="Person"/> row data so user edits round-trip immediately.
/// A "live readout" of the first row's current values updates whenever
/// any of its bound properties change, proving the bindings are wired
/// in both directions.
/// </summary>
public sealed partial class MixedControlsPage : Page
{
    // SamplePageHeader rationale also applies here — host XAML's default
    // Page measure pass mismeasures Frame-hosted pages on first render.
    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
    {
        if (Content is FrameworkElement child)
        {
            child.Measure(availableSize);
            return child.DesiredSize;
        }
        return new Windows.Foundation.Size(0, 0);
    }

    protected override Windows.Foundation.Size ArrangeOverride(Windows.Foundation.Size finalSize)
    {
        if (Content is FrameworkElement child)
        {
            child.Arrange(new Windows.Foundation.Rect(0, 0, finalSize.Width, finalSize.Height));
        }
        return finalSize;
    }

    public MixedControlsPage()
    {
        InitializeComponent();
        // 18 rows is enough to demonstrate scrolling while keeping the cell
        // controls compact enough to interact with one at a time.
        People = new ObservableCollection<Person>(PersonData.Take(18));
        // Wire default consumer-owned sort + filter so the header click
        // (or programmatic SortDescriptions) re-shapes People. Template
        // columns carry explicit SortMemberPath in XAML so the helper can
        // resolve each key.
        SampleShape.EnableDefaults(PeopleTable, People);
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public ObservableCollection<Person> People { get; }

    private Person? _watchedRow;

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (People.Count > 0)
        {
            _watchedRow = People[0];
            _watchedRow.PropertyChanged += OnWatchedRowChanged;
            RefreshReadout();
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (_watchedRow is not null)
        {
            _watchedRow.PropertyChanged -= OnWatchedRowChanged;
            _watchedRow = null;
        }
    }

    private void OnWatchedRowChanged(object? sender, PropertyChangedEventArgs e) => RefreshReadout();

    private void RefreshReadout()
    {
        if (_watchedRow is null)
        {
            LiveReadout.Text = "(no rows)";
            return;
        }

        LiveReadout.Text =
            $"Name        : {_watchedRow.FullName}\n" +
            $"JoinDate    : {_watchedRow.JoinDate:yyyy-MM-dd}\n" +
            $"ShiftStart  : {_watchedRow.ShiftStart:hh\\:mm}\n" +
            $"Department  : {_watchedRow.Department}\n" +
            $"IsActive    : {_watchedRow.IsActive}";
    }
}
