// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates TableView.CanUserReorderRows + the read-only
/// CanReorderRows DP — the row drag-and-drop reorder gesture
/// shipped in P3.5. Toggle the gate, drag a row by its body cells,
/// drop it on another row to commit a move.
///
/// Key invariants this page lets reviewers verify by hand:
///   * Default-off — toggle the switch to false and CanReorderRows
///     immediately reads false; rows lose their CanDrag/AllowDrop.
///   * Effective gate — CanReorderRows tracks (CanUserReorderRows
///     AND IBindableVector source AND no grouping). The readout
///     panel surfaces it so the AND is visible at a glance.
///   * Group-header rows are non-draggable — but this page doesn't
///     enable groups (the Groups page is the place to see it).
///   * Selection is preserved across a move — the same data item
///     stays selected even though its index changes.
///   * Drop adornment — a single horizontal line above the hovered
///     row indicates the insertion point.
///   * v1 contract — only DataPackageOperation.Move is accepted;
///     Ctrl-drag-to-copy is a follow-up.
/// </summary>
public sealed partial class RowReorderPage : Page
{
    public RowReorderPage()
    {
        InitializeComponent();

        SeedPeople();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public ObservableCollection<Person> People { get; } = new();

    private int _moveCount;
    private int _pendingFromIndex = -1;
    private Person? _pendingItem;

    // ----- Toggle / reset handlers -----

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Subscribe to source-collection mutations so the page surfaces the
        // committed (from→to) pair to the user. The reorder codec applies as
        // Remove(sourceIdx) + Insert(adjustedTarget), so we treat any
        // (Remove,Add) pair within a single tick as one move.
        People.CollectionChanged -= OnPeopleCollectionChanged;
        People.CollectionChanged += OnPeopleCollectionChanged;
        UpdateEffectiveGateReadout();

    }

    private readonly StringBuilder _events = new();
    private void LogEvent(string tag)
    {
        if (_events.Length > 0) _events.Append(' ');
        _events.Append(tag);
        if (EventText != null) EventText.Text = _events.ToString();
    }

    private void OnDiagnoseClick(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.Append($"CanReorderRows={PeopleTable.CanReorderRows} CanUserReorderRows={PeopleTable.CanUserReorderRows} ");
        sb.Append($"SelMode={PeopleTable.SelectionMode} ItemsSourceType={PeopleTable.ItemsSource?.GetType().Name} ");
        sb.Append($"PeopleCount={People.Count} ");
        // Consume the public RealizedRows iterator — no visual-tree walk.
        int rowCount = 0;
        int draggable = 0;
        int allowsDrop = 0;
        foreach (var row in PeopleTable.RealizedRows)
        {
            rowCount++;
            if (row.CanDrag) draggable++;
            if (row.AllowDrop) allowsDrop++;
        }
        sb.Append($"Rows={rowCount} CanDrag={draggable} AllowDrop={allowsDrop}");
        if (DiagText != null) DiagText.Text = sb.ToString();
    }

    private void OnMoveClick(object sender, RoutedEventArgs e)
    {
        // Direct API-level move to test the readout pipeline end-to-end.
        if (People.Count > 3)
        {
            People.Move(0, 3);
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        People.CollectionChanged -= OnPeopleCollectionChanged;
    }

    private void OnReorderToggled(object sender, RoutedEventArgs e)
    {
        if (PeopleTable != null && sender is ToggleSwitch toggle)
        {
            PeopleTable.CanUserReorderRows = toggle.IsOn;
            UpdateEffectiveGateReadout();
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        SeedPeople();
        _moveCount = 0;
        _pendingFromIndex = -1;
        _pendingItem = null;
        if (LastMoveText != null) LastMoveText.Text = "(none)";
        if (MoveCountText != null) MoveCountText.Text = "0";
        UpdateEffectiveGateReadout();
    }

    private void OnPeopleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Fast path — XAML/WinUI ObservableCollection<T> can raise a single
        // NotifyCollectionChangedAction.Move directly when Move() is called,
        // but the TableView reorder pipeline uses RemoveAt+InsertAt on a
        // generic IBindableVector, which raises Remove then Add separately.
        // Handle both shapes.
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Move:
                ReportMove(e.OldStartingIndex, e.NewStartingIndex);
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is { Count: 1 } removed && removed[0] is Person p)
                {
                    _pendingFromIndex = e.OldStartingIndex;
                    _pendingItem = p;
                }
                break;
            case NotifyCollectionChangedAction.Add:
                if (_pendingItem is not null
                    && e.NewItems is { Count: 1 } added
                    && ReferenceEquals(added[0], _pendingItem))
                {
                    ReportMove(_pendingFromIndex, e.NewStartingIndex);
                }
                _pendingFromIndex = -1;
                _pendingItem = null;
                break;
            case NotifyCollectionChangedAction.Reset:
                _pendingFromIndex = -1;
                _pendingItem = null;
                break;
        }
    }

    private void ReportMove(int from, int to)
    {
        if (from < 0 || to < 0) return;
        _moveCount++;
        if (LastMoveText != null) LastMoveText.Text = $"{from} → {to}";
        if (MoveCountText != null) MoveCountText.Text = _moveCount.ToString();
    }

    // ----- Helpers -----

    private void SeedPeople()
    {
        People.Clear();
        var snapshot = PersonData.Take(40);
        foreach (var p in snapshot)
        {
            People.Add(p);
        }
    }

    private void UpdateEffectiveGateReadout()
    {
        if (PeopleTable == null || EffectiveGateText == null) return;
        // Read the read-only effective gate exposed by P3-blocker B2.
        // Templates / consumers can bind UI affordances directly to it
        // without re-deriving (CanUserReorderRows AND IBindableVector
        // source AND !grouping).
        EffectiveGateText.Text = PeopleTable.CanReorderRows ? "True" : "False";
    }
}
