// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using TableViewSamples.Data;
using TableViewSamples.Models;
using Windows.ApplicationModel.DataTransfer;

namespace TableViewSamples.Pages;

public sealed partial class ShowcasePage : Page, INotifyPropertyChanged
{
    private enum ShowcaseMode
    {
        Flat,
        Grouped,
        Hierarchical,
    }

    private static readonly string[] s_showcaseDepartments =
    {
        "Marketing",
        "Sales",
        "Design",
        "Product",
        "Finance",
    };

    private static readonly Dictionary<string, Func<Person, IComparable?>> s_keySelectors =
        new(StringComparer.Ordinal)
        {
            ["FirstName"] = p => p.FirstName,
            ["LastName"] = p => p.LastName,
            ["Email"] = p => p.Email,
            ["Department"] = p => p.Department,
            ["Role"] = p => p.Role,
            ["JoinDate"] = p => p.JoinDate,
            ["Salary"] = p => p.Salary,
        };

    private readonly ObservableCollection<ShowcaseOrgNode> _hierarchyRoots;
    private List<Person> _master = new();
    private List<DepartmentGroup> _groupedSource;
    private ShowcaseMode _mode = ShowcaseMode.Hierarchical;
    private string _statusText = string.Empty;

    // Active preset for the cell-tint converters (instantiated by XAML as page
    // resources, so they read this static rather than holding a page back-ref).
    // Matches the Per-cell conditional styling page's pattern.
    public static bool Vibrant = true;

    // Live-update plumbing (same shape as CellStylingPage): a cancellable UI-thread
    // loop mutates Salary on a few visible rows so the bound stoplight tint re-runs
    // for just those cells — no per-column rebuild.
    private static readonly double[] s_liveSalaries = { 48_000.0, 82_000.0, 138_000.0 };
    private int _liveTick;
    private CancellationTokenSource? _liveCts;
    private CancellationTokenSource? _feedbackCts;

    public ShowcasePage()
    {
        People = PersonData.Take(100);
        _groupedSource = BuildGroupedView(BuildCuratedPeopleSource());
        _hierarchyRoots = BuildHierarchy();

        InitializeComponent();
        ApplyMode(ShowcaseMode.Hierarchical);
        ApplyBanding();
        ApplySelectionGutter();
        UpdateStatus();

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public ObservableCollection<Person> People { get; private set; }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    private void OnModeRadioChecked(object sender, RoutedEventArgs e)
    {
        if (PeopleTable is null || RowCountCombo is null || RowReorderPanel is null || RowReorderToggle is null || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        if (Enum.TryParse<ShowcaseMode>(tag, out var mode))
        {
            ApplyMode(mode);
        }
    }

    private void OnRowCountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeopleTable is null || RowCountCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag || !int.TryParse(tag, out var count))
        {
            return;
        }

        People = PersonData.Take(count);
        _master = new List<Person>(People);
        OnPropertyChanged(nameof(People));

        if (_mode == ShowcaseMode.Flat)
        {
            PeopleTable.ItemsSource = People;
            UpdateStatus();
        }
    }

    private void OnBandingRadioChecked(object sender, RoutedEventArgs e)
    {
        if (PeopleTable is null || CustomBandingRadio is null)
        {
            return;
        }

        ApplyBanding();
        UpdateStatus();
    }

    private void OnSelectionGutterToggled(object sender, RoutedEventArgs e)
    {
        if (PeopleTable is null)
        {
            return;
        }

        ApplySelectionGutter();
        UpdateStatus();
    }

    private void OnRowReorderToggled(object sender, RoutedEventArgs e)
    {
        if (PeopleTable is null)
        {
            return;
        }

        ApplyRowReorderGate();
        UpdateStatus();
    }

    private void ApplyMode(ShowcaseMode mode)
    {
        _mode = mode;

        PeopleTable.ItemsSource = null;
        PeopleTable.GroupedItemsSource = null;
        PeopleTable.HierarchicalItemsSource = null;

        // In hierarchical mode, the first column carries chevron + indent +
        // mixed root/leaf content, so its header label "First name" is
        // misleading, so we relabel it "Name". The name column stays frozen
        // (Leading) in every mode so it remains pinned while the rich columns
        // scroll horizontally, and per-column filtering stays available in every
        // mode (the control shapes grouped/hierarchical sources through its
        // adapter; flat is shaped by OnPeopleTableFiltered).
        var hierarchical = _mode == ShowcaseMode.Hierarchical;
        if (FirstNameColumn is not null)
        {
            FirstNameColumn.Header = hierarchical ? "Name" : "First name";
            FirstNameColumn.CanUserFilter = true;
            FirstNameColumn.FrozenEdge = Microsoft.UI.Xaml.Controls.TableViewFrozenEdge.Leading;
        }
        if (LastNameColumn is not null) LastNameColumn.CanUserFilter = true;
        if (EmailColumn is not null) EmailColumn.CanUserFilter = true;
        if (DepartmentColumn is not null) DepartmentColumn.CanUserFilter = true;
        if (RoleColumn is not null) RoleColumn.CanUserFilter = true;
        // JoinDateColumn / ShiftStartColumn / ActiveColumn keep CanUserFilter=False
        // from XAML — a filter funnel on a DatePicker / TimePicker / status-chip
        // cell isn't meaningful, and their headers stay sort-only.
        if (SalaryColumn is not null) SalaryColumn.CanUserFilter = !hierarchical;

        switch (_mode)
        {
            case ShowcaseMode.Flat:
                _master = new List<Person>(People);
                PeopleTable.ItemsSource = People;
                break;

            case ShowcaseMode.Grouped:
                _groupedSource = BuildGroupedView(BuildCuratedPeopleSource());
                PeopleTable.GroupedItemsSource = _groupedSource;
                break;

            case ShowcaseMode.Hierarchical:
                PeopleTable.HierarchicalChildrenPropertyName = nameof(ShowcaseOrgNode.Children);
                PeopleTable.HierarchicalItemsSource = _hierarchyRoots;
                PeopleTable.UpdateLayout();
                // Expand the CEO and the leader tier on first show so the grid
                // immediately reads as a multi-level reporting tree (CEO →
                // leaders → managers) rather than a single collapsed root.
                foreach (var root in _hierarchyRoots)
                {
                    PeopleTable.ExpandItem(root);
                    foreach (var report in root.Children)
                    {
                        PeopleTable.ExpandItem(report);
                    }
                }
                break;
        }

        RowCountCombo.IsEnabled = _mode == ShowcaseMode.Flat;
        RowReorderPanel.Visibility = _mode == ShowcaseMode.Flat ? Visibility.Visible : Visibility.Collapsed;
        ApplyRowReorderGate();
        UpdateStatus();
    }

    private void ApplyRowReorderGate()
    {
        PeopleTable.CanUserReorderRows = _mode == ShowcaseMode.Flat && RowReorderToggle.IsOn;
    }

    private void ApplyBanding()
    {
        // Clear any prior preset's local DP values AND any Style we may have
        // applied. ClearValue (not `= null`) is required so Style.Setters can
        // win when we re-apply the Default banding Style below; null is a
        // local value and would trump the Style per WinUI DP precedence.
        PeopleTable.Style = null;
        PeopleTable.ClearValue(TableView.RowBackgroundProperty);
        PeopleTable.ClearValue(TableView.AlternatingRowBackgroundProperty);
        PeopleTable.ClearValue(TableView.RowForegroundProperty);
        PeopleTable.ClearValue(TableView.AlternatingRowForegroundProperty);

        if (CustomBandingRadio.IsChecked == true)
        {
            PeopleTable.RowBackground = Application.Current.Resources["SampleCustomRowBackgroundBrush"] as Brush;
            PeopleTable.AlternatingRowBackground = Application.Current.Resources["SampleCustomAlternatingRowBackgroundBrush"] as Brush;
        }
        else
        {
            // Default option: apply the control-shipped TableViewDefaultBandingStyle.
            // The Setter VALUEs use {ThemeResource TableViewDefault*Brush}, so the
            // brushes re-resolve per-element on Light↔Dark switches — tracks the
            // root RequestedTheme flip from the Theme & settings page without any
            // ActualThemeChanged plumbing on this page.
            if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(
                    "TableViewDefaultBandingStyle", out var styleObj) &&
                styleObj is Style style)
            {
                PeopleTable.Style = style;
            }
        }
    }

    private void ApplySelectionGutter()
    {
        // With the control fix (TableViewRow::RebuildCells now honors
        // IsSelectionGutterVisible via ShouldShowSelectionGutter, matching the
        // OnApplyTemplate / columns-changed paths), multi-select and the per-row
        // checkbox gutter are decoupled: the grid stays Extended in BOTH states
        // and the toggle simply shows (on) or hides (off) the gutter. "Off" is a
        // true gutter-free multi-select grid — Ctrl/Shift/marquee selection all
        // still work, there is just no checkbox column.
        var on = SelectionGutterToggle.IsOn;
        PeopleTable.SelectionMode = TableViewSelectionMode.Extended;
        PeopleTable.IsSelectionGutterVisible = on;
        PeopleTable.HeadersVisibility = on
            ? TableViewHeadersVisibility.All
            : TableViewHeadersVisibility.Column;
    }

    // ----- Page lifecycle + toolbar wiring -----

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Sync the static tint flag to the toggle (it can be stale from a prior
        // cached instance) and re-realize the tinted columns so they match on
        // first show. Declarative toggle sets in XAML fire their handlers DURING
        // InitializeComponent — before the table columns exist — so those handlers
        // bail early; this is where the real wiring lands.
        Vibrant = VibrantToggle?.IsOn ?? true;
        ReevaluateTintedColumns();
        UpdateSelectionCount();
        if (LiveToggle?.IsChecked == true)
        {
            StartLiveUpdates();
        }
        UpdateStatus();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => StopLiveUpdates();

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // Stop the loop the instant navigation away begins — earlier and more
        // deterministic than Unloaded, which can fire AFTER the next page starts
        // loading. NavigationCacheMode=Enabled keeps this page (and its TableView)
        // alive, so a loop left running would keep mutating cached off-screen rows.
        StopLiveUpdates();
        base.OnNavigatedFrom(e);
    }

    private void OnSelectionChanged(TableView sender, TableViewSelectionChangedEventArgs args) => UpdateSelectionCount();

    private void UpdateSelectionCount()
    {
        if (SelectionCountText is null || PeopleTable is null)
        {
            return;
        }

        var count = PeopleTable.SelectedItems?.Count ?? 0;
        SelectionCountText.Text = count switch
        {
            0 => "No rows selected",
            1 => "1 row selected",
            _ => $"{count:N0} rows selected",
        };
    }

    private void OnGridLinesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeopleTable is null || GridLinesPicker is null)
        {
            return;
        }

        PeopleTable.GridLinesVisibility = GridLinesPicker.SelectedIndex switch
        {
            0 => TableViewGridLinesVisibility.None,
            1 => TableViewGridLinesVisibility.Horizontal,
            2 => TableViewGridLinesVisibility.Vertical,
            _ => TableViewGridLinesVisibility.All,
        };
    }

    private void OnVibrantToggled(object sender, RoutedEventArgs e)
    {
        // Fires during InitializeComponent (declarative IsOn) before the table
        // columns are realized — bail until OnPageLoaded does the real sync.
        if (PeopleTable is null || DepartmentColumn is null || SalaryColumn is null)
        {
            return;
        }

        Vibrant = VibrantToggle.IsOn;
        ReevaluateTintedColumns();
        UpdateStatus();
    }

    private void ReevaluateTintedColumns()
    {
        if (DepartmentColumn is null || SalaryColumn is null)
        {
            return;
        }

        // The tint converters are pure functions of (value, Vibrant). Toggling
        // Vibrant doesn't touch any bound value, so already-realized cells won't
        // re-run on their own. Clear+restore CellTemplate to re-realize just the
        // two tinted columns once — a per-click cost, never per-tick.
        ReassignCellTemplate(DepartmentColumn);
        ReassignCellTemplate(SalaryColumn);
    }

    private static void ReassignCellTemplate(TableViewTemplateColumn column)
    {
        var template = column.CellTemplate;
        column.CellTemplate = null;
        column.CellTemplate = template;
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        // The grid is always Extended (multi-select), so SelectAll works whether
        // or not the checkbox gutter is showing -- no need to flip the gutter on.
        PeopleTable.SelectAll();
        UpdateSelectionCount();
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        PeopleTable.DeselectAll();
        UpdateSelectionCount();
    }

    private void OnExportAllTsv(object sender, RoutedEventArgs e)
    {
        PeopleTable.CopyAllToClipboard();
        ShowExportFeedback($"Copied {SourceRowCount():N0} rows (TSV) to the clipboard");
    }

    private void OnExportAllCsv(object sender, RoutedEventArgs e)
    {
        SetClipboardText(PeopleTable.GetDataAsText(TableViewDataFormat.CommaSeparated, true));
        ShowExportFeedback($"Copied {SourceRowCount():N0} rows (CSV) to the clipboard");
    }

    private void OnExportSelectionTsv(object sender, RoutedEventArgs e)
    {
        var count = PeopleTable.SelectedItems?.Count ?? 0;
        if (count == 0)
        {
            ShowExportFeedback("Select one or more rows first");
            return;
        }

        SetClipboardText(PeopleTable.GetSelectedDataAsText(TableViewDataFormat.TabSeparated, true));
        ShowExportFeedback($"Copied {count:N0} selected row(s) (TSV) to the clipboard");
    }

    private static void SetClipboardText(string text)
    {
        var package = new DataPackage();
        package.SetText(text ?? string.Empty);
        Clipboard.SetContent(package);
    }

    private int SourceRowCount() => _mode switch
    {
        ShowcaseMode.Flat => People.Count,
        ShowcaseMode.Grouped => _groupedSource.Sum(g => g.Count),
        ShowcaseMode.Hierarchical => FlattenNodes(_hierarchyRoots).Count(),
        _ => 0,
    };

    private async void ShowExportFeedback(string message)
    {
        if (SelectionCountText is null)
        {
            return;
        }

        _feedbackCts?.Cancel();
        _feedbackCts?.Dispose();
        _feedbackCts = new CancellationTokenSource();
        var token = _feedbackCts.Token;

        SelectionCountText.Text = message;
        try
        {
            await Task.Delay(2200, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            UpdateSelectionCount();
        }
    }

    // ----- Live updates -----

    private void OnLiveToggleChanged(object sender, RoutedEventArgs e)
    {
        // Fires during InitializeComponent (declarative IsChecked) before the
        // table is realized — bail; OnPageLoaded starts the loop after load.
        if (PeopleTable is null)
        {
            return;
        }

        if (LiveToggle.IsChecked == true)
        {
            StartLiveUpdates();
        }
        else
        {
            StopLiveUpdates();
        }
        UpdateStatus();
    }

    private void StartLiveUpdates()
    {
        StopLiveUpdates();
        _liveTick = 0;
        _liveCts = new CancellationTokenSource();
        _ = LiveUpdatesLoopAsync(_liveCts.Token);
    }

    private void StopLiveUpdates()
    {
        _liveCts?.Cancel();
        _liveCts?.Dispose();
        _liveCts = null;
    }

    private async Task LiveUpdatesLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var delay = 700;
                if (DispatcherQueue?.HasThreadAccess == true && UpdateIntervalSlider is not null)
                {
                    delay = (int)UpdateIntervalSlider.Value;
                }
                await Task.Delay(Math.Max(50, delay), token);

                if (DispatcherQueue?.HasThreadAccess == true)
                {
                    ApplyLiveUpdate();
                }
                else
                {
                    await EnqueueLiveUpdateAsync(token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task EnqueueLiveUpdateAsync(CancellationToken token)
    {
        var queue = DispatcherQueue;
        if (queue is null)
        {
            return;
        }

        var tcs = new TaskCompletionSource();
        using var _ = token.Register(() => tcs.TrySetCanceled(token));
        if (!queue.TryEnqueue(() =>
        {
            try
            {
                if (!token.IsCancellationRequested)
                {
                    ApplyLiveUpdate();
                }
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
        if (_liveCts is null || _liveCts.IsCancellationRequested)
        {
            return;
        }

        var targets = GetLiveTargets();
        if (targets.Count == 0)
        {
            return;
        }

        // Refresh a small window of rows each tick so the stoplight Salary tints
        // visibly cycle without ever crossing into per-frame churn.
        var window = Math.Min(targets.Count, 12);
        var tick = _liveTick++;
        var idx = tick % window;
        var tier = s_liveSalaries[(tick / window + idx) % s_liveSalaries.Length];
        SetSalary(targets[idx], tier + (idx * 137));

        // Every third tick, flip the status chip on the row so the Active column
        // visibly animates too (the bound chip converter re-runs for just it).
        if (tick % 3 == 0)
        {
            ToggleActive(targets[idx]);
        }
    }

    private static void ToggleActive(object row)
    {
        switch (row)
        {
            case Person person:
                person.IsActive = !person.IsActive;
                break;
            case ShowcaseOrgNode node:
                node.IsActive = !node.IsActive;
                break;
        }
    }

    private IReadOnlyList<object> GetLiveTargets() => _mode switch
    {
        ShowcaseMode.Flat => People.Cast<object>().ToList(),
        ShowcaseMode.Grouped => _groupedSource.SelectMany(g => g).Cast<object>().ToList(),
        ShowcaseMode.Hierarchical => FlattenNodes(_hierarchyRoots).Cast<object>().ToList(),
        _ => Array.Empty<object>(),
    };

    private static void SetSalary(object row, double value)
    {
        switch (row)
        {
            case Person person:
                person.Salary = value;
                break;
            case ShowcaseOrgNode node:
                node.Salary = value;
                break;
        }
    }

    private void OnPeopleTableSorted(TableView sender, TableViewSortedEventArgs args)
    {
        if (_mode != ShowcaseMode.Flat)
        {
            UpdateStatus();
            return;
        }

        var sortedColumns = args.SortedColumns;
        if (sortedColumns is null || sortedColumns.Count == 0)
        {
            return;
        }

        IOrderedEnumerable<Person>? ordered = null;
        foreach (var column in sortedColumns.OrderBy(c => c.SortIndex))
        {
            var path = column.SortMemberPath;
            if (string.IsNullOrEmpty(path) || !s_keySelectors.TryGetValue(path, out var key))
            {
                continue;
            }

            ordered = ordered is null
                ? (column.SortDirection == SortDirection.Descending ? People.OrderByDescending(key) : People.OrderBy(key))
                : (column.SortDirection == SortDirection.Descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
        }

        if (ordered is null)
        {
            return;
        }

        var sorted = ordered.ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int currentIdx = People.IndexOf(sorted[i]);
            if (currentIdx >= 0 && currentIdx != i)
            {
                People.Move(currentIdx, i);
            }
        }

        UpdateStatus();
    }

    private void OnPeopleTableFiltered(TableView sender, TableViewFilteredEventArgs args)
    {
        if (_mode != ShowcaseMode.Flat)
        {
            UpdateStatus();
            return;
        }

        var filters = PeopleTable.FilteredColumns
            .Select(c => c.Filter)
            .Where(f => f is not null)
            .ToList();

        IEnumerable<Person> visible = _master;
        if (filters.Count > 0)
        {
            visible = _master.Where(p => filters.All(f => f!.Matches(p)));
        }

        visible = ApplyCurrentFlatSort(visible);

        People.Clear();
        foreach (var person in visible)
        {
            People.Add(person);
        }

        UpdateStatus();
    }

    private IEnumerable<Person> ApplyCurrentFlatSort(IEnumerable<Person> visible)
    {
        var sortedColumns = PeopleTable.Columns
            .Where(c => c.SortDirection != SortDirection.None)
            .OrderBy(c => c.SortIndex)
            .ToList();

        IOrderedEnumerable<Person>? ordered = null;
        foreach (var column in sortedColumns)
        {
            var path = column.SortMemberPath;
            if (string.IsNullOrEmpty(path) || !s_keySelectors.TryGetValue(path, out var key))
            {
                continue;
            }

            ordered = ordered is null
                ? (column.SortDirection == SortDirection.Descending ? visible.OrderByDescending(key) : visible.OrderBy(key))
                : (column.SortDirection == SortDirection.Descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
        }

        return ordered ?? visible;
    }

    private void UpdateStatus()
    {
        var gutter = SelectionGutterToggle is not null && SelectionGutterToggle.IsOn ? "multi-select + gutter" : "gutter-free multi-select";
        var banding = CustomBandingRadio is not null && CustomBandingRadio.IsChecked == true ? "custom banding" : "theme banding";
        var vibrant = Vibrant ? "vibrant cells" : "plain cells";
        var live = _liveCts is not null ? "live on" : "live off";
        var tail = $"{banding} · {vibrant} · {live} · {gutter}";

        StatusText = _mode switch
        {
            ShowcaseMode.Flat => $"Flat · {People.Count:N0} visible of {_master.Count:N0} flat rows · row reorder {(PeopleTable.CanUserReorderRows ? "on" : "off")} · First name frozen · {tail}",
            ShowcaseMode.Grouped => $"Grouped · {_groupedSource.Count:N0} departments · {_groupedSource.Sum(g => g.Count):N0} people · First name frozen · {tail}",
            ShowcaseMode.Hierarchical => $"Hierarchical · employee org rooted at the CEO · {FlattenNodes(_hierarchyRoots).Count():N0} employees · chevron-led name column · {tail}",
            _ => string.Empty,
        };
    }

    private static IReadOnlyList<Person> BuildCuratedPeopleSource()
    {
        return s_showcaseDepartments
            .SelectMany(department => PersonData.All.Where(p => string.Equals(p.Department, department, StringComparison.Ordinal)).Take(12))
            .ToList();
    }

    private static List<DepartmentGroup> BuildGroupedView(IEnumerable<Person> people)
    {
        return s_showcaseDepartments
            .Select(department => new DepartmentGroup(department, people.Where(p => string.Equals(p.Department, department, StringComparison.Ordinal))))
            .Where(group => group.Count > 0)
            .ToList();
    }

    // Builds a genuine employee reporting tree: a single CEO root with the org
    // fanning out CEO → leaders → managers → individual contributors (4 levels).
    // This is what makes Hierarchical mode meaningfully different from Grouped —
    // a real N-level reporting chain rooted at one person, not a department→members
    // two-level grouping. Every node is a real employee drawn deterministically
    // from PersonData, so each row carries genuine Salary / Status / Join-date /
    // Shift values; there are no synthetic "aggregate" rows.
    private static ObservableCollection<ShowcaseOrgNode> BuildHierarchy()
    {
        // Per-department draws from the deterministic people pool so no employee
        // appears twice and every node has real bound values.
        var pools = new Dictionary<string, Queue<Person>>(StringComparer.Ordinal);
        Person Next(string department)
        {
            if (!pools.TryGetValue(department, out var queue))
            {
                queue = new Queue<Person>(
                    PersonData.All.Where(p => string.Equals(p.Department, department, StringComparison.Ordinal)));
                pools[department] = queue;
            }
            return queue.Count > 0
                ? queue.Dequeue()
                : new Person { FirstName = "Open", LastName = "Role", Department = department, Role = "Open role" };
        }

        static ShowcaseOrgNode FromPerson(Person p, string? role = null) => new()
        {
            FirstName = p.FirstName,
            LastName = p.LastName,
            Department = p.Department,
            Role = role ?? p.Role,
            Email = p.Email,
            JoinDate = p.JoinDate,
            JoinDateText = p.JoinDateText,
            ShiftStart = p.ShiftStart,
            Salary = p.Salary,
            IsActive = p.IsActive,
        };

        static ShowcaseOrgNode Leader(string first, string last, string role, string department, int joinYear, double salary)
        {
            var join = new DateTimeOffset(joinYear, 3, 1, 0, 0, 0, TimeSpan.Zero);
            return new ShowcaseOrgNode
            {
                FirstName = first,
                LastName = last,
                Department = department,
                Role = role,
                Email = $"{first}.{last}@example.com".ToLowerInvariant(),
                JoinDate = join,
                JoinDateText = join.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ShiftStart = TimeSpan.FromHours(8),
                Salary = salary,
                IsActive = true,
            };
        }

        var ceo = Leader("Satya", "Nadella", "Chief Executive Officer", "Executive", 1992, 4_999_000);

        void AddOrg(string first, string last, string role, string department, double salary)
        {
            var leader = Leader(first, last, role, department, 2001, salary);
            for (var m = 0; m < 2; m++)
            {
                var manager = FromPerson(Next(department), $"{department} Manager");
                for (var ic = 0; ic < 3; ic++)
                {
                    manager.Children.Add(FromPerson(Next(department)));
                }
                leader.Children.Add(manager);
            }
            ceo.Children.Add(leader);
        }

        // Satya's direct reports — each leads one of the curated departments,
        // so its whole subtree shows a single consistent Department pill color.
        AddOrg("Rajesh",  "Jha",      "EVP, Experiences & Devices",     "Product",   2_400_000);
        AddOrg("Judson",  "Althoff",  "EVP & Chief Commercial Officer", "Sales",     2_300_000);
        AddOrg("Takeshi", "Numoto",   "EVP & Chief Marketing Officer",  "Marketing", 2_100_000);
        AddOrg("Amy",     "Hood",     "EVP & Chief Financial Officer",  "Finance",   2_500_000);
        AddOrg("Jon",     "Friedman", "CVP, Design & Research",         "Design",    1_700_000);

        return new ObservableCollection<ShowcaseOrgNode> { ceo };
    }

    // Depth-first flatten of the reporting tree — used for the live-update target
    // set and the employee count (the tree is now N levels deep, so a 2-level
    // roots+children sum would undercount).
    private static IEnumerable<ShowcaseOrgNode> FlattenNodes(IEnumerable<ShowcaseOrgNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var descendant in FlattenNodes(node.Children))
            {
                yield return descendant;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public sealed class DepartmentGroup : List<Person>
    {
        public DepartmentGroup(string department, IEnumerable<Person> people) : base(people)
        {
            Department = department;
        }

        public string Department { get; }

        public override string ToString() => Department;
    }

    public sealed class ShowcaseOrgNode : INotifyPropertyChanged
    {
        private double? _salary;
        private bool _isActive;

        public string FirstName { get; init; } = string.Empty;

        public string LastName { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public string Department { get; init; } = string.Empty;

        public string Role { get; init; } = string.Empty;

        // Every hierarchy node is now a real employee, so JoinDate / ShiftStart
        // carry genuine values and the in-cell pickers are editable on every row.
        // IsAggregate stays false throughout (retained only for the picker-enable
        // binding, in case synthetic summary rows are reintroduced later).
        public string JoinDateText { get; init; } = string.Empty;

        // Settable so the in-cell DatePicker / TimePicker can TwoWay-write back
        // in Hierarchical mode (the same templates bind Person rows too).
        public DateTimeOffset JoinDate { get; set; }

        public TimeSpan ShiftStart { get; set; }

        public bool IsAggregate { get; init; }

        // Settable + observable so the live-update loop can mutate child-row
        // salaries in place; the bound stoplight tint re-runs for just that cell.
        public double? Salary
        {
            get => _salary;
            set
            {
                if (_salary != value)
                {
                    _salary = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Salary)));
                }
            }
        }

        // Observable so the live loop can flip the status chip in place.
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
                }
            }
        }

        public ObservableCollection<ShowcaseOrgNode> Children { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

/// <summary>
/// Tints the Department cell with a per-category pill hue, gated by
/// <see cref="ShowcasePage.Vibrant"/>. Returns a theme-independent,
/// semi-transparent brush (reads correctly over both Light and Dark row
/// backgrounds), or transparent when vibrant cells are turned off.
/// </summary>
public sealed class ShowcaseDepartmentTintConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (!ShowcasePage.Vibrant || value is not string department)
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        var color = department switch
        {
            "Engineering" => ColorHelper.FromArgb(0x4D, 0x00, 0x78, 0xD4),
            "Sales" => ColorHelper.FromArgb(0x4D, 0x14, 0xB8, 0xA6),
            "Marketing" => ColorHelper.FromArgb(0x4D, 0xA8, 0x55, 0xF7),
            "HR" => ColorHelper.FromArgb(0x4D, 0xF5, 0x9E, 0x0B),
            "Operations" => ColorHelper.FromArgb(0x4D, 0xEF, 0x44, 0x44),
            "Design" => ColorHelper.FromArgb(0x4D, 0xEC, 0x48, 0x99),
            "Product" => ColorHelper.FromArgb(0x4D, 0x0E, 0xA5, 0xE9),
            "Finance" => ColorHelper.FromArgb(0x4D, 0x22, 0xC5, 0x5E),
            _ => ColorHelper.FromArgb(0x4D, 0x64, 0x74, 0x8B),
        };
        return new SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Stoplight tint for the Salary cell — green when high, amber for mid, red for
/// low — gated by <see cref="ShowcasePage.Vibrant"/>. Transparent when vibrant
/// cells are off or the value is unset (department aggregate rows).
/// </summary>
public sealed class ShowcaseSalaryTintConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (!ShowcasePage.Vibrant || value is not double salary)
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        var color = salary >= 100_000 ? ColorHelper.FromArgb(0x4D, 0x16, 0xA3, 0x4A)   // green
                  : salary >= 60_000 ? ColorHelper.FromArgb(0x4D, 0xF5, 0x9E, 0x0B)     // amber
                  : ColorHelper.FromArgb(0x4D, 0xDC, 0x26, 0x26);                       // red
        return new SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Formats the Salary cell as whole-dollar currency in the current culture, or
/// an empty string when the value is unset (department aggregate rows).
/// </summary>
public sealed class ShowcaseSalaryTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is double salary)
        {
            return salary.ToString("C0", CultureInfo.CurrentCulture);
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Solid (full-opacity) per-category dot drawn at the leading edge of the
/// Department pill, gated by <see cref="ShowcasePage.Vibrant"/>. Mirrors the
/// pill's hue but fully saturated so it reads as a crisp status dot.
/// </summary>
public sealed class ShowcaseDepartmentDotConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (!ShowcasePage.Vibrant || value is not string department)
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        var color = department switch
        {
            "Engineering" => ColorHelper.FromArgb(0xFF, 0x00, 0x78, 0xD4),
            "Sales" => ColorHelper.FromArgb(0xFF, 0x14, 0xB8, 0xA6),
            "Marketing" => ColorHelper.FromArgb(0xFF, 0xA8, 0x55, 0xF7),
            "HR" => ColorHelper.FromArgb(0xFF, 0xF5, 0x9E, 0x0B),
            "Operations" => ColorHelper.FromArgb(0xFF, 0xEF, 0x44, 0x44),
            "Design" => ColorHelper.FromArgb(0xFF, 0xEC, 0x48, 0x99),
            "Product" => ColorHelper.FromArgb(0xFF, 0x0E, 0xA5, 0xE9),
            "Finance" => ColorHelper.FromArgb(0xFF, 0x22, 0xC5, 0x5E),
            _ => ColorHelper.FromArgb(0xFF, 0x64, 0x74, 0x8B),
        };
        return new SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Status-chip background — green when Active, slate when Inactive — gated by
/// <see cref="ShowcasePage.Vibrant"/>. Transparent when vibrant cells are off.
/// </summary>
public sealed class ShowcaseActiveChipConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (!ShowcasePage.Vibrant || value is not bool isActive)
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        var color = isActive
            ? ColorHelper.FromArgb(0x59, 0x16, 0xA3, 0x4A)   // green
            : ColorHelper.FromArgb(0x59, 0x64, 0x74, 0x8B);  // slate
        return new SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Status-chip label — "Active" / "Inactive".
/// </summary>
public sealed class ShowcaseActiveTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language) =>
        value is bool isActive ? (isActive ? "Active" : "Inactive") : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, string language) =>
        throw new NotImplementedException();
}

/// <summary>
/// Keeps shared DatePicker / TimePicker cell templates interactive for person
/// rows while disabling the placeholder values on department aggregate roots.
/// </summary>
public sealed class ShowcaseAggregateEditorEnabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is not bool isAggregate || !isAggregate;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
