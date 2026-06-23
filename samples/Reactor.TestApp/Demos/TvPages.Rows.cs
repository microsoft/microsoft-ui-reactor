using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Reactor.Controls.Factories;
using static Reactor.TestApp.TableViewGallery.TableViewSampleData;
using WinTV = Microsoft.UI.Xaml.Controls.TableView;
using TVGrid = Microsoft.UI.Xaml.Controls.TableViewGridLinesVisibility;
using TVSel = Microsoft.UI.Xaml.Controls.TableViewSelectionMode;

namespace Reactor.TestApp.TableViewGallery;

// ── Rows & cells section ─────────────────────────────────────────────────────────────────────────

class TvRowReorderPage : Component
{
    readonly ObservableCollection<Person> _rows = new(People);
    WinTV? _tv;
    bool _watching;
    bool _suppressMoves;
    int _pendingFromIndex = -1;
    Person? _pendingItem;
    int _moveCount;
    Action<string>? _setLastMove;
    Action<int>? _setMoveCount;
    Action<string>? _setDiag;

    public override Element Render()
    {
        var (on, setOn) = UseState(true);
        var (lastMove, setLastMove) = UseState("(none)");
        var (moveCount, setMoveCount) = UseState(0);
        var (diag, setDiag) = UseState("(press Diagnose)");
        _setLastMove = setLastMove;
        _setMoveCount = setMoveCount;
        _setDiag = setDiag;
        _moveCount = moveCount;

        void Reset()
        {
            _suppressMoves = true;
            _rows.Clear();
            foreach (var p in People) _rows.Add(p);
            _suppressMoves = false;
            _pendingFromIndex = -1;
            _pendingItem = null;
            _moveCount = 0;
            setLastMove("(none)");
            setMoveCount(0);
            setDiag("(press Diagnose)");
        }

        var table = TableView(_rows, TextColumns()) with
        {
            SelectionMode = TVSel.Single,
            CanReorderColumns = false,
            OnControlReady = tv =>
            {
                _tv = tv;
                if (!_watching)
                {
                    _rows.CollectionChanged += OnRowsChanged;
                    _watching = true;
                }
            },
            Setters = new Action<WinTV>[] { tv => tv.CanUserReorderRows = on },
        };

        var options = VStack(16,
            TvSample.Section("Reorder controls", "Toggle CanUserReorderRows, then drag any row by its cells onto another row to drop it before or after. Column headers are fixed here, so the gesture always means a row reorder. The 'Last move' readout shows the (from, to) indices of the most recent move.",
                ToggleSwitch(on, setOn, onContent: "On", offContent: "Off", header: "CanUserReorderRows"),
                VStack(8,
                    Button("Reset order", Reset),
                    Button("Diagnose", () => setDiag($"CanReorderRows={_tv?.CanReorderRows.ToString() ?? "(unknown)"} CanUserReorderRows={_tv?.CanUserReorderRows.ToString() ?? on.ToString()} SelMode={_tv?.SelectionMode.ToString() ?? "Single"} PeopleCount={_rows.Count}")),
                    Button("Move 0→3 (API)", () => { if (_rows.Count > 3) _rows.Move(0, 3); }))),
            TvSample.Section("Status", null,
                TvSample.Readout("CanReorderRows", _tv?.CanReorderRows.ToString() ?? on.ToString()),
                TvSample.Readout("Last move", lastMove),
                TvSample.Readout("Move count", moveCount.ToString())),
            TvSample.Section("Diagnostics", null,
                Caption(diag)));

        return TvSample.Page("RowReorder",
            "Click and drag any row by its cells onto another row to drop it before or after. Toggle CanUserReorderRows to enable or disable the gesture.",
            table, options);
    }

    void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressMoves) return;
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
                if (_pendingItem is not null && e.NewItems is { Count: 1 } added && ReferenceEquals(added[0], _pendingItem))
                    ReportMove(_pendingFromIndex, e.NewStartingIndex);
                _pendingFromIndex = -1;
                _pendingItem = null;
                break;
            case NotifyCollectionChangedAction.Reset:
                _pendingFromIndex = -1;
                _pendingItem = null;
                break;
        }
    }

    void ReportMove(int from, int to)
    {
        if (from < 0 || to < 0) return;
        _moveCount++;
        _setLastMove?.Invoke($"{from} → {to}");
        _setMoveCount?.Invoke(_moveCount);
        _setDiag?.Invoke($"Move committed: {from} → {to}");
    }
}

class TvGroupsPage : Component
{
    WinTV? _tv;

    public override Element Render()
    {
        var (grouping, setGrouping) = UseState(true);
        var (shuffle, setShuffle) = UseState(0);
        var (lastAction, setLastAction) = UseState("(none)");

        var source = ShuffledPeople(shuffle);
        var rows = grouping ? source.OrderBy(p => p.Department).ThenBy(p => p.FirstName).ToList() : source;
        var groups = rows.GroupBy(p => p.Department).OrderBy(g => g.Key).ToList();
        var perGroup = string.Join(" · ", groups.Select(g => $"{g.Key}: {g.Count()}"));

        var table = TableView(rows, TextColumns()) with
        {
            CanSortColumns = true,
            CanFilterColumns = true,
            OnControlReady = tv => _tv = tv,
        };

        var options = VStack(16,
            TvSample.NativeNote("GroupedItemsSource is still a native-only source shape for the consumable Reactor wrapper. This page keeps a Department-ordered flat view, calls the native bulk group APIs when requested, and reports real group counts from the bound data."),
            TvSample.Section("Grouping controls", "Toggle grouping, reshuffle departments, or expand/collapse every group using the sample's grouped-row headers.",
                ToggleSwitch(grouping, setGrouping, onContent: "On", offContent: "Off", header: "Group by Department"),
                VStack(8,
                    Button("Shuffle ~25% of people across departments", () => { setShuffle(shuffle + 1); setLastAction("Departments reshuffled"); }),
                    Button("Expand all", () => { _tv?.ExpandAllGroups(); setLastAction(grouping ? "ExpandAllGroups called" : "Expand all ignored — grouping off"); }),
                    Button("Collapse all", () => { _tv?.CollapseAllGroups(); setLastAction(grouping ? "CollapseAllGroups called" : "Collapse all ignored — grouping off"); }))),
            TvSample.Section("Status", null,
                TvSample.Readout("IsGrouping", grouping.ToString()),
                TvSample.Readout("Group count", grouping ? groups.Count.ToString() : "(n/a)"),
                TvSample.Readout("Total people", rows.Count.ToString()),
                TvSample.Readout("Source shape", grouping ? $"Flat rows ordered by Department ({groups.Count} groups)" : $"Flat rows ({rows.Count} items)"),
                TvSample.Readout("Per-group counts", grouping ? perGroup : "(grouping off)"),
                TvSample.Readout("Last action", lastAction)));

        return TvSample.Page("Groups",
            "Toggle grouping, then expand / collapse groups or click a header to sort within each group. Funnels filter rows inside each group.",
            table, options);
    }

    static List<Person> ShuffledPeople(int shuffle)
    {
        if (shuffle == 0) return People.ToList();
        var departments = People.Select(p => p.Department).Distinct().OrderBy(d => d).ToArray();
        return People.Select((p, i) => i % 4 == shuffle % 4
            ? p with { Department = departments[(Array.IndexOf(departments, p.Department) + shuffle) % departments.Length] }
            : p).ToList();
    }
}

class TvHierarchyPage : Component
{
    WinTV? _tv;
    readonly List<LivePerson> _roots = HierarchyRoots();

    public override Element Render()
    {
        var (selectedNode, setSelectedNode) = UseState("(none)");
        var (sorted, setSorted) = UseState("(none)");
        var (filtered, setFiltered) = UseState("(none)");
        var (lastAction, setLastAction) = UseState("(none)");

        void RefreshReadouts(string action)
        {
            if (_tv != null)
            {
                var node = _tv.SelectedItems?.OfType<LivePerson>().FirstOrDefault();
                setSelectedNode(node == null
                    ? "(none)"
                    : $"{node.Name} (Depth={DepthOf(_roots, node)}, HasChildren={node.Children.Count > 0})");
                setSorted(FormatColumnList(_tv.SortedColumns, c => $"{c.Header} ({c.SortDirection})"));
                setFiltered(FormatColumnList(_tv.FilteredColumns, c => $"{c.Header}"));
            }
            setLastAction(action);
        }

        var table = TableView(Array.Empty<object>(), VibrantColumns()) with
        {
            SelectionMode = TVSel.Single,
            CanSortColumns = true,
            CanFilterColumns = true,
            CanResizeColumns = true,
            HierarchicalItems = _roots,
            HierarchicalChildrenPath = nameof(LivePerson.Children),
            ExpandFirstLevel = true,
            OnControlReady = tv =>
            {
                _tv = tv;
                tv.Sorted += (_, __) => RefreshReadouts("sort");
                tv.Filtered += (_, __) => RefreshReadouts("filter");
            },
            OnSelectionChanged = _ => RefreshReadouts("selection"),
        };

        var options = VStack(16,
            TvSample.Section("Hierarchy actions", "Expand, collapse, or toggle the selected node without using chevrons.",
                VStack(8,
                    Button("Expand all", () => InvokeHierarchy("ExpandAllItems", null, "expand-all", setLastAction)),
                    Button("Collapse all", () => InvokeHierarchy("CollapseAllItems", null, "collapse-all", setLastAction)),
                    Button("Toggle selected", () =>
                    {
                        var node = _tv?.SelectedItems?.OfType<LivePerson>().FirstOrDefault();
                        if (node == null) setLastAction("toggle: (no selection)");
                        else InvokeHierarchy("ToggleItem", node, $"toggle({node.Name})", setLastAction);
                    }))),
            TvSample.Section("Status", null,
                TvSample.Readout("IsHierarchical", "True"),
                TvSample.Readout("ChildrenPropertyName", nameof(LivePerson.Children)),
                TvSample.Readout("Selected node", selectedNode),
                TvSample.Readout("Sorted columns", sorted),
                TvSample.Readout("Filtered columns", filtered),
                TvSample.Readout("Last action", lastAction)));

        return TvSample.Page("Hierarchy",
            "Click a chevron to expand / collapse a node. Sorting and filtering work per level and keep the tree path visible. Use Expand all / Collapse all too.",
            table, options);
    }

    void InvokeHierarchy(string methodName, object? arg, string success, Action<string> setLastAction)
    {
        if (_tv == null) { setLastAction($"{success} requested before table ready"); return; }
        var method = _tv.GetType().GetMethods()
            .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == (arg == null ? 0 : 1));
        if (method == null)
        {
            setLastAction($"{success} requested — native {methodName} API not available on this build");
            return;
        }
        method.Invoke(_tv, arg == null ? null : new[] { arg });
        setLastAction(success);
    }

    static string FormatColumnList<T>(IEnumerable<T>? columns, Func<T, string> formatter) =>
        columns == null || !columns.Any() ? "(none)" : string.Join(", ", columns.Select(formatter));

    static int DepthOf(IEnumerable<LivePerson> nodes, LivePerson target, int depth = 0)
    {
        foreach (var n in nodes)
        {
            if (ReferenceEquals(n, target)) return depth;
            var found = DepthOf(n.Children, target, depth + 1);
            if (found >= 0) return found;
        }
        return -1;
    }
}

class TvRowColorsPage : Component
{
    static readonly string[] Presets = { "None", "Default theme", "Custom colors" };
    static readonly string[] Intervals = { "Off", "1000 ms", "500 ms", "200 ms" };
    static readonly int[] IntervalVals = { 0, 1000, 500, 200 };
    readonly Random _rng = new(2468);
    readonly ObservableCollection<LivePerson> _rows = LivePeople(24);
    int _liveTicks;

    public override Element Render()
    {
        var (preset, setPreset) = UseState(1);
        var (live, setLive) = UseState(0);
        var (ticks, setTicks) = UseState(0);

        UseEffect(() =>
        {
            if (live == 0) return () => { };
            var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IntervalVals[live]) };
            timer.Tick += (_, __) =>
            {
                int top = Math.Min(_rows.Count, 12);
                for (int k = 0; k < 3 && top > 0; k++)
                {
                    var p = _rows[_rng.Next(top)];
                    p.Salary = 60000 + _rng.Next(0, 180000);
                }
                setTicks(++_liveTicks);
            };
            timer.Start();
            return () => timer.Stop();
        }, live);

        var setters = preset switch
        {
            2 => new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x33, 0x38, 0xB5, 0xFF) },
            1 => new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x18, 0x80, 0x80, 0x80) },
            _ => new Action<WinTV>[] { tv => tv.AlternatingRowBackground = null! },
        };

        var table = TableView(_rows, VibrantColumns()) with
        {
            SelectionMode = TVSel.Extended,
            Setters = setters,
        };

        var options = VStack(16,
            TvSample.Section("Row banding", "Pick a preset to set alternating-row background colors. Each click instantly re-tints the rows; None clears them.",
                RadioButtons(Presets, preset, setPreset)),
            TvSample.Section("Live updates", "Mutate several visible LivePerson.Salary values in place; INotifyPropertyChanged refreshes the tint cells without rebinding.",
                RadioButtons(Intervals, live, setLive)),
            TvSample.Section("Status", null,
                TvSample.Readout("Active mode", Presets[preset]),
                TvSample.Readout("Live updates", live == 0 ? "Off" : Intervals[live]),
                TvSample.Readout("Salary update ticks", ticks.ToString())));

        return TvSample.Page("RowColors",
            "Pick a preset to set row and alternating-row background / foreground colors. Each click instantly re-tints the rows; 'No banding' clears them.",
            table, options);
    }
}

class TvGridLinesPage : Component
{
    static readonly string[] Lines = { "None", "Horizontal", "Vertical", "All" };
    static readonly string[] Modes = { "Flat", "Grouped", "Hierarchical" };
    static readonly string[] Banding = { "Default theme", "Custom colors" };
    readonly List<LivePerson> _roots = HierarchyRoots();

    public override Element Render()
    {
        var (lines, setLines) = UseState(3);
        var (mode, setMode) = UseState(0);
        var (banding, setBanding) = UseState(0);

        var grid = lines switch { 0 => TVGrid.None, 1 => TVGrid.Horizontal, 2 => TVGrid.Vertical, _ => TVGrid.All };
        var setters = banding == 1
            ? new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x33, 0x38, 0xB5, 0xFF) }
            : new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x18, 0x80, 0x80, 0x80) };

        var rows = mode == 1 ? People.OrderBy(p => p.Department).ThenBy(p => p.FirstName).ToList() : People;
        TableViewElement table = mode == 2
            ? TableView(Array.Empty<object>(), TextColumns()) with
            {
                HierarchicalItems = _roots,
                HierarchicalChildrenPath = nameof(LivePerson.Children),
                ExpandFirstLevel = true,
                SelectionMode = TVSel.Single,
                GridLinesVisibility = grid,
                Setters = setters,
            }
            : TableView(rows, TextColumns()) with
            {
                SelectionMode = TVSel.Single,
                GridLinesVisibility = grid,
                Setters = setters,
            };

        var status = mode switch
        {
            1 => $"Grouped · {People.Select(p => p.Department).Distinct().Count():N0} departments · {People.Count:N0} people",
            2 => $"Hierarchical · {_roots.Count:N0} roots · {_roots.Sum(r => r.Children.Count):N0} child rows",
            _ => $"Flat · {People.Count:N0} rows",
        };

        var options = VStack(16,
            TvSample.Section("Grid lines", null,
                RadioButtons(Lines, lines, setLines)),
            TvSample.Section("Mode", null,
                RadioButtons(Modes, mode, setMode),
                mode == 1 ? Caption("GroupedItemsSource is native-only in the consumable wrapper; this mode orders rows by Department while keeping grid-line styling live.") : TextBlock("")),
            TvSample.Section("Row banding", null,
                RadioButtons(Banding, banding, setBanding)),
            TvSample.Section("Status", null,
                TvSample.Readout("GridLinesVisibility", Lines[lines]),
                TvSample.Readout("Mode", Modes[mode]),
                TvSample.Readout("Row banding", Banding[banding]),
                TvSample.Readout("Source", $"{status} · {Lines[lines].ToLowerInvariant()} lines · {Banding[banding].ToLowerInvariant()}")));

        return TvSample.Page("GridLines",
            "Choose which grid lines show — All, Horizontal, Vertical, or None. Switch mode or banding to see the lines stay on-theme.",
            table, options);
    }
}

class TvRowTemplatePage : Component
{
    static readonly string[] Templates =
    {
        "Default — per-column cells",
        "Card — name + role/status",
        "Compact — name + email",
        "Mixed — department + salary"
    };

    public override Element Render()
    {
        var (template, setTemplate) = UseState(0);
        var columns = template switch
        {
            1 => CardColumns(),
            2 => CompactColumns(),
            3 => MixedColumns(),
            _ => TextColumns(),
        };

        var table = TableView(People, columns) with
        {
            SelectionMode = TVSel.Single,
        };

        var options = VStack(16,
            TvSample.NativeNote("TableView.RowTemplate replaces the whole realized row in the native control. The consumable Reactor wrapper exposes column cells, so this sample switches between curated column sets to suggest the same template choices."),
            TvSample.Section("Template picker", "Pick a row template. The TableView's realized rows re-shape immediately — no rebind needed.",
                RadioButtons(Templates, template, setTemplate)),
            TvSample.Section("Status", null,
                TvSample.Readout("Active template", Templates[template])));

        return TvSample.Page("RowTemplate",
            "Switch templates to rebuild every row in place. Selection and keyboard nav keep working; 'None' restores the default cells.",
            table, options);
    }

    static List<TableColumn> CardColumns() => new()
    {
        new("First name", nameof(Person.FirstName), Width: 130),
        new("Role", nameof(Person.Role), Width: 190),
        new("Department", nameof(Person.Department), CellStyle.Pill, Width: 150),
        new("Status", nameof(Person.IsActive), CellStyle.Chip, Width: 100),
    };

    static List<TableColumn> CompactColumns() => new()
    {
        new("First name", nameof(Person.FirstName), Width: 130),
        new("Email", nameof(Person.Email), Width: 260),
    };

    static List<TableColumn> MixedColumns() => new()
    {
        new("First name", nameof(Person.FirstName), Width: 130),
        new("Department", nameof(Person.Department), CellStyle.Pill, Width: 150),
        new("Role", nameof(Person.Role), Width: 190),
        new("Salary", nameof(Person.Salary), CellStyle.Tint, Width: 120),
    };
}

class TvRowDetailsPage : Component
{
    static readonly string[] Modes = { "Collapsed", "Visible", "VisibleWhenSelected" };
    WinTV? _tv;

    public override Element Render()
    {
        var (mode, setMode) = UseState(2);
        var (selectedIndex, setSelectedIndex) = UseState(-1);
        var (changes, setChanges) = UseState(0);

        string selected = selectedIndex >= 0 && selectedIndex < People.Count
            ? $"{People[selectedIndex].FirstName} {People[selectedIndex].LastName}"
            : "(none)";

        var table = TableView(People, TextColumns()) with
        {
            SelectionMode = TVSel.Single,
            OnControlReady = tv => _tv = tv,
            OnSelectionChanged = _ =>
            {
                setChanges(changes + 1);
                setSelectedIndex(_tv?.SelectedIndex ?? -1);
            },
        };

        var options = VStack(16,
            TvSample.NativeNote("RowDetailsTemplate and RowDetailsVisibilityMode are native TableView features not yet exposed by the consumable Reactor wrapper. The picker tracks the intended mode and selection readouts remain live."),
            TvSample.Section("Visibility mode", "Pick how row details are shown. VisibleWhenSelected is the default for this sample.",
                RadioButtons(Modes, mode, setMode)),
            TvSample.Section("Status", null,
                TvSample.Readout("Visibility events", changes.ToString()),
                TvSample.Readout("Last event", selected == "(none)" ? "(none yet)" : $"{selected} — details would be visible")));

        return TvSample.Page("RowDetails",
            "Pick a visibility mode to show each row's details panel — always, never, or only when selected. The readouts track each change.",
            table, options);
    }
}

class TvMixedControlsPage : Component
{
    public override Element Render()
    {
        var first = People[0];
        var table = TableView(People, VibrantColumns()) with
        {
            SelectionMode = TVSel.Single,
        };

        var options = VStack(16,
            TvSample.NativeNote("In-cell DatePicker, TimePicker, ComboBox, and CheckBox require native TableViewTemplateColumn cell templates. The consumable wrapper renders typed text / pill / chip / tint columns, so this page shows the same data with vibrant cells and a live first-row readout."),
            TvSample.Section("Live data readout", "First row",
                TvSample.Readout("Name", $"{first.FirstName} {first.LastName}"),
                TvSample.Readout("Join date", first.JoinDateText),
                TvSample.Readout("Department", first.Department),
                TvSample.Readout("Role", first.Role),
                TvSample.Readout("Active", first.IsActive.ToString())));

        return TvSample.Page("MixedControls",
            "Edit any row's date, time, department, or Active checkbox — changes bind straight back to the row and the readout below updates live.",
            table, options);
    }
}

class TvMarqueePage : Component
{
    static readonly string[] Modes = { "None", "Single", "Multiple", "Extended" };
    WinTV? _tv;

    public override Element Render()
    {
        var (marquee, setMarquee) = UseState(true);
        var (mode, setMode) = UseState(3);
        var (selectedCount, setSelectedCount) = UseState(0);
        var (selectedIndex, setSelectedIndex) = UseState(-1);
        var (selectedIndices, setSelectedIndices) = UseState("(none)");
        var (commitRanges, setCommitRanges) = UseState("(none)");

        var sel = mode switch { 0 => TVSel.None, 1 => TVSel.Single, 2 => TVSel.Multiple, _ => TVSel.Extended };
        var gesture = !marquee
            ? "Gesture disabled — turn CanUserMarqueeSelect on."
            : sel is TVSel.None or TVSel.Single
                ? "Gesture unavailable in None / Single mode — choose Multiple or Extended."
                : $"Ready — drag in the blank area below the last row ({People.Count} rows loaded).";

        var table = TableView(People, TextColumns()) with
        {
            SelectionMode = sel,
            IsSelectionGutterVisible = true,
            OnControlReady = tv => _tv = tv,
            OnSelectionChanged = _ =>
            {
                var indices = SelectedIndices();
                setSelectedCount(indices.Count);
                setSelectedIndex(_tv?.SelectedIndex ?? -1);
                setSelectedIndices(indices.Count == 0 ? "(none)" : string.Join(", ", indices));
                setCommitRanges(indices.Count == 0 ? "(none)" : DescribeRanges(indices));
            },
            Setters = new Action<WinTV>[] { tv => tv.CanUserMarqueeSelect = marquee },
        };

        var options = VStack(16,
            TvSample.Section("Marquee controls", "Ready to try: marquee is ON and the table starts in Extended selection mode. Click and drag with the left mouse button in the blank area below the last row to draw a selection rectangle, then release to commit the selection.",
                TextBlock(gesture),
                ToggleSwitch(marquee, setMarquee, onContent: "On", offContent: "Off", header: "CanUserMarqueeSelect"),
                ComboBox(Modes, mode, setMode),
                Button("Clear selection", () => _tv?.DeselectAll())),
            TvSample.Section("Status", null,
                TvSample.Readout("Gesture state", gesture),
                TvSample.Readout("Rows loaded", People.Count.ToString()),
                TvSample.Readout("Selected count", selectedCount.ToString()),
                TvSample.Readout("SelectedIndex", selectedIndex.ToString()),
                TvSample.Readout("Selected indices / ranges", selectedIndices),
                TvSample.Readout("Commit ranges", commitRanges)));

        return TvSample.Page("Marquee",
            "Click and drag in an empty area of the table to draw a selection rectangle; on release, the rows it touches are selected.",
            table, options);
    }

    List<int> SelectedIndices()
    {
        var selected = _tv?.SelectedItems;
        if (selected == null || selected.Count == 0) return new();
        return selected.OfType<Person>()
            .Select(p => People.IndexOf(p))
            .Where(i => i >= 0)
            .OrderBy(i => i)
            .ToList();
    }

    static string DescribeRanges(List<int> sortedIndices)
    {
        var ranges = new List<string>();
        int start = sortedIndices[0], prev = start;
        for (int i = 1; i < sortedIndices.Count; i++)
        {
            int v = sortedIndices[i];
            if (v == prev + 1) { prev = v; continue; }
            ranges.Add(start == prev ? $"{start}" : $"{start}..{prev}");
            start = prev = v;
        }
        ranges.Add(start == prev ? $"{start}" : $"{start}..{prev}");
        return string.Join(", ", ranges);
    }
}
