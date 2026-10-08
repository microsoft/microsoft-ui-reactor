// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using TableViewSamples.Data;
using TableViewSamples.Models;

namespace TableViewSamples.Pages;

/// <summary>
/// Demonstrates data-bound conditional cell tints - the performant pattern for
/// dynamic conditional styling in TableView. Instead of re-assigning a column's
/// CellStyleSelector when a value crosses a threshold (a structural change that
/// rebuilds the whole column's cell tree), each tinted cell binds its Border
/// background to the row value through an <see cref="IValueConverter"/>. A value
/// change raises PropertyChanged on the bound Person property, which re-runs ONLY
/// that one cell's converter and recolors it instantly - no per-column rebuild,
/// so the table stays smooth even under fast live updates.
///
/// Three converters read the shared <see cref="Scheme"/> field to decide whether
/// (and how vibrantly) to tint:
///   * <see cref="SalaryTintConverter"/> - green for high, amber for mid, red for
///     low. Tints the Salary column.
///   * <see cref="DepartmentTintConverter"/> - per-department hue. Tints the
///     Department column.
///   * <see cref="ActiveTintConverter"/> - green when active, red when not. Tints
///     the Active column.
/// </summary>
public sealed partial class CellStylingPage : Page
{
    /// <summary>
    /// Active styling preset (0-4, matching <c>SelectorPicker.SelectedIndex</c>).
    /// Read by the three tint converters to decide whether a column is tinted and
    /// whether the vibrant (saturated) palette is used. Static so the converters,
    /// instantiated by XAML as page resources, can read it without a
    /// back-reference to the page.
    /// </summary>
    public static int Scheme = 4;

    private int _liveUpdateTick;
    private CancellationTokenSource? _liveUpdatesCts;
    private bool _pageLoaded;
    private static readonly double[] s_liveUpdateSalaries = { 45_000.0, 78_000.0, 135_000.0 };
    private static readonly string[] s_departments = {
        "Engineering", "Sales", "Marketing", "HR",
        "Operations", "Design", "Product", "Finance"
    };

    public CellStylingPage()
    {
        // Initialize People before InitializeComponent: the LiveUpdatesToggle is
        // declared IsOn="True" in markup, so its Toggled handler can fire during
        // XAML parse - before this assignment would otherwise run - and must not
        // observe a null collection.
        People = new ObservableCollection<Person>(PersonData.Take(100));
        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public ObservableCollection<Person> People { get; }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _pageLoaded = true;

        // Apply whatever preset is currently selected (default: index 4 = Vibrant
        // sales dashboard). SelectionChanged may have fired before PeopleTable was
        // realized during InitializeComponent, so re-apply explicitly here.
        OnSelectorChanged(SelectorPicker, null!);

        // Auto-start live updates when the toggle defaults to on, so reviewers see
        // bound cell text mutating in place without manual interaction.
        if (LiveUpdatesToggle?.IsOn == true)
        {
            StartLiveUpdates();
        }
    }

    private void OnSelectorChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged can fire during InitializeComponent, before the
        // TableView (and its columns) are realized - bail until OnPageLoaded
        // re-invokes us with the table in place.
        if (PeopleTable is null)
        {
            return;
        }

        // Publish the active preset so the bound tint converters pick it up.
        Scheme = SelectorPicker.SelectedIndex;

        ActiveSelectorText.Text = Scheme switch
        {
            0 => "None - cells are untinted",
            1 => "Salary tier - green for high, amber for mid, red for low (Salary column)",
            2 => "Department + Active - category tint on Department, status tint on Active",
            3 => "All three columns - Salary tier + Department + Active",
            4 => "Vibrant sales dashboard - saturated Salary, Department, and Active tints",
            _ => "(none)",
        };

        // The converters are pure functions of (value, Scheme). Changing Scheme
        // does not touch any bound value, so already-realized cells won't re-run
        // their converters on their own. Force a ONE-TIME re-realization of just
        // the three tinted columns so the new scheme takes effect immediately.
        // This is a user-click action, not a per-tick cost.
        ReevaluateTintedColumns();
    }

    private void ReevaluateTintedColumns()
    {
        // SMP-CTL-2: TableViewColumn.RefreshCells() re-realizes the owning
        // TableView's cells (re-running every cell's bindings, and thus the tint
        // converters) against the current Scheme. One call rebuilds all realized
        // rows, so refreshing any one tinted column refreshes them all — replaces
        // the prior per-column CellTemplate null-cycle hack.
        DepartmentCol.RefreshCells();
    }

    private void OnLiveUpdatesToggled(object sender, RoutedEventArgs e)
    {
        // Toggled fires while XAML is still parsing (IsOn="True" in markup), before
        // the page is loaded and its named fields/data are ready - and again on real
        // user toggles. Ignore the parse-time firing; OnPageLoaded auto-starts the
        // loop when the toggle defaults on. Without this guard the live-updates loop
        // starts mid-construction and dereferences not-yet-initialized state, faulting
        // the process during a page-realizing UIA traversal (A11Y-UIA-FAILFAST repro).
        if (!_pageLoaded)
        {
            return;
        }

        if (LiveUpdatesToggle.IsOn)
        {
            StartLiveUpdates();
        }
        else
        {
            StopLiveUpdates();
        }
    }

    private void StartLiveUpdates()
    {
        StopLiveUpdates();
        _liveUpdateTick = 0;
        _liveUpdatesCts = new CancellationTokenSource();
        var token = _liveUpdatesCts.Token;
        _ = LiveUpdatesLoopAsync(token);
    }

    private void StopLiveUpdates()
    {
        _liveUpdatesCts?.Cancel();
        _liveUpdatesCts?.Dispose();
        _liveUpdatesCts = null;
    }

    private async Task LiveUpdatesLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && People.Count > 0)
            {
                int delay = 500;
                if (DispatcherQueue?.HasThreadAccess == true && UpdateIntervalSlider is not null)
                {
                    delay = (int)UpdateIntervalSlider.Value;
                }
                await Task.Delay(Math.Max(25, delay), token);

                await ApplyLiveUpdateAsync(token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ApplyLiveUpdateAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        if (DispatcherQueue?.HasThreadAccess == true)
        {
            ApplyLiveUpdate();
            return;
        }

        var dispatcherQueue = DispatcherQueue;
        if (dispatcherQueue is null)
        {
            return;
        }

        var tcs = new TaskCompletionSource();
        using var _ = token.Register(() => tcs.TrySetCanceled(token));
        if (!dispatcherQueue.TryEnqueue(() =>
        {
            if (token.IsCancellationRequested)
            {
                tcs.TrySetCanceled(token);
                return;
            }

            try
            {
                ApplyLiveUpdate();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
        {
            return;
        }

        await tcs.Task;
    }

    private void ApplyLiveUpdate()
    {
        // Bail if the loop was cancelled (toggled off, or we navigated away)
        // between Task.Delay resuming and this body running. All Start/Stop and
        // this method run on the UI thread, so reading the CTS here is race-free.
        if (_liveUpdatesCts is null || _liveUpdatesCts.IsCancellationRequested)
        {
            return;
        }

        if (People.Count == 0)
        {
            return;
        }

        var visibleSampleCount = Math.Min(People.Count, 12);
        var tick = _liveUpdateTick++;
        var idx = tick % visibleSampleCount;
        var p = People[idx];

        // Cycle salaries so every row eventually visits all three tiers:
        // (tick / visibleSampleCount) advances each time the cursor wraps, so
        // row[idx] sees a different tier each pass.
        p.Salary = s_liveUpdateSalaries[(tick / visibleSampleCount + idx) % s_liveUpdateSalaries.Length] + (idx * 173);

        // Throttle Department / Active mutations so the readout cycles at a
        // legible pace. No style bookkeeping is needed: the Person setters raise
        // PropertyChanged, the bound Border background re-runs its tint converter
        // for just this one cell, and the color updates in place.
        if (tick % 2 == 0)
        {
            p.Department = s_departments[(tick + idx) % s_departments.Length];
        }
        if (tick % 3 == 0)
        {
            p.IsActive = !p.IsActive;
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        StopLiveUpdates();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // Stop the live-update loop the instant navigation away begins. This is
        // earlier and more deterministic than Unloaded, which can fire AFTER the
        // next page has begun loading. Because NavigationCacheMode="Enabled" keeps
        // this page instance (and its TableView) alive, a loop left running would
        // keep mutating the cached, off-screen rows for no reason - stop it
        // promptly so no background work outlives the page's time on screen.
        StopLiveUpdates();
        base.OnNavigatedFrom(e);
    }
}

/// <summary>
/// Tints the Salary cell by tier. Active in schemes 1, 3 and 4; vibrant
/// (saturated) only in scheme 4. Returns a theme-independent, semi-transparent
/// brush so the tint reads correctly in both Light and Dark, or a transparent
/// brush when the active scheme does not tint Salary.
/// </summary>
public sealed class SalaryTintConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language)
    {
        var scheme = CellStylingPage.Scheme;
        if (value is not double salary || (scheme != 1 && scheme != 3 && scheme != 4))
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        var alpha = scheme == 4 ? CellTint.Vibrant : CellTint.Subtle;
        var color = salary >= 100_000 ? ColorHelper.FromArgb(alpha, 0x16, 0xA3, 0x4A)  // green
                  : salary >= 60_000 ? ColorHelper.FromArgb(alpha, 0xF5, 0x9E, 0x0B)   // amber
                  : ColorHelper.FromArgb(alpha, 0xDC, 0x26, 0x26);                      // red
        return new SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Tints the Department cell by category hue. Active in schemes 2, 3 and 4;
/// vibrant only in scheme 4. Unknown departments get a neutral slate tint.
/// </summary>
public sealed class DepartmentTintConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language)
    {
        var scheme = CellStylingPage.Scheme;
        if (value is not string department || (scheme != 2 && scheme != 3 && scheme != 4))
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        var alpha = scheme == 4 ? CellTint.Vibrant : CellTint.Subtle;
        var color = department switch
        {
            "Engineering" => ColorHelper.FromArgb(alpha, 0x00, 0x78, 0xD4),
            "Sales" => ColorHelper.FromArgb(alpha, 0x14, 0xB8, 0xA6),
            "Marketing" => ColorHelper.FromArgb(alpha, 0xA8, 0x55, 0xF7),
            "HR" => ColorHelper.FromArgb(alpha, 0xF5, 0x9E, 0x0B),
            "Operations" => ColorHelper.FromArgb(alpha, 0xEF, 0x44, 0x44),
            "Design" => ColorHelper.FromArgb(alpha, 0xEC, 0x48, 0x99),
            "Product" => ColorHelper.FromArgb(alpha, 0x0E, 0xA5, 0xE9),
            "Finance" => ColorHelper.FromArgb(alpha, 0x22, 0xC5, 0x5E),
            _ => ColorHelper.FromArgb(alpha, 0x64, 0x74, 0x8B),  // neutral slate
        };
        return new SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Tints the Active cell green when true, red when false. Active in schemes
/// 2, 3 and 4; vibrant only in scheme 4.
/// </summary>
public sealed class ActiveTintConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language)
    {
        var scheme = CellStylingPage.Scheme;
        if (value is not bool isActive || (scheme != 2 && scheme != 3 && scheme != 4))
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        var alpha = scheme == 4 ? CellTint.Vibrant : CellTint.Subtle;
        var color = isActive
            ? ColorHelper.FromArgb(alpha, 0x16, 0xA3, 0x4A)  // green
            : ColorHelper.FromArgb(alpha, 0xDC, 0x26, 0x26);  // red
        return new SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Shared alpha levels for the tint converters. Semi-transparent so the same
/// literal color reads correctly over both Light and Dark row backgrounds.
/// </summary>
internal static class CellTint
{
    public const byte Subtle = 0x26;   // ~15%
    public const byte Vibrant = 0x4D;  // ~30%
}
