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

        var table = TableView(_rows, TextColumns(), height: 460) with
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
    public override Element Render()
    {
        var (grouping, setGrouping) = UseState(true);
        var (shuffle, setShuffle) = UseState(0);
        var (lastAction, setLastAction) = UseState("(none)");

        var source = ShuffledPeople(shuffle);
        var rows = grouping ? source.OrderBy(p => p.Department).ThenBy(p => p.FirstName).ToList() : source;
        var groups = rows.GroupBy(p => p.Department).OrderBy(g => g.Key).ToList();
        var perGroup = string.Join(" · ", groups.Select(g => $"{g.Key}: {g.Count()}"));

        var table = TableView(rows, TextColumns(), height: 460) with
        {
            CanSortColumns = true,
            CanFilterColumns = true,
        };

        var options = VStack(16,
            TvSample.NativeNote("Grouped row headers and ExpandAllGroups / CollapseAllGroups are native TableView features not yet exposed by the consumable Reactor wrapper. This page keeps the same controls, orders rows by Department when grouping is on, and reports real group counts from the bound data."),
            TvSample.Section("Grouping controls", "Toggle grouping, reshuffle departments, or expand/collapse every group using the sample's grouped-row headers.",
                ToggleSwitch(grouping, setGrouping, onContent: "On", offContent: "Off", header: "Group by Department"),
                VStack(8,
                    Button("Shuffle ~25% of people across departments", () => { setShuffle(shuffle + 1); setLastAction("Departments reshuffled"); }),
                    Button("Expand all", () => setLastAction("Expand all requested — native grouping not exposed")),
                    Button("Collapse all", () => setLastAction("Collapse all requested — native grouping not exposed")))),
            TvSample.Section("Status", null,
                TvSample.Readout("IsGrouping", grouping.ToString()),
                TvSample.Readout("Group count", grouping ? groups.Count.ToString() : "0"),
                TvSample.Readout("Total people", rows.Count.ToString()),
                TvSample.Readout("Source shape", grouping ? "Flat rows ordered by Department" : "Flat rows"),
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

    public override Element Render()
    {
        var (selectedIndex, setSelectedIndex) = UseState(-1);
        var (lastAction, setLastAction) = UseState("(none)");

        string selected = selectedIndex >= 0 && selectedIndex < People.Count
            ? $"{selectedIndex}: {People[selectedIndex].FirstName} {People[selectedIndex].LastName}"
            : "(none)";

        var table = TableView(People, TextColumns(), height: 460) with
        {
            SelectionMode = TVSel.Single,
            CanSortColumns = true,
            CanFilterColumns = true,
            OnControlReady = tv => _tv = tv,
            OnSelectionChanged = _ => setSelectedIndex(_tv?.SelectedIndex ?? -1),
        };

        var options = VStack(16,
            TvSample.NativeNote("HierarchicalItemsSource, HierarchicalChildrenPropertyName, chevrons, and ExpandAllItems / CollapseAllItems are native TableView features not yet exposed by the consumable Reactor wrapper. This page renders the flat data and reports the selected row it can observe."),
            TvSample.Section("Hierarchy actions", "Expand, collapse, or toggle the selected node without using chevrons.",
                VStack(8,
                    Button("Expand all", () => setLastAction("Expand all requested — native hierarchy not exposed")),
                    Button("Collapse all", () => setLastAction("Collapse all requested — native hierarchy not exposed")),
                    Button("Toggle selected", () => setLastAction(selectedIndex >= 0 ? $"Toggle requested for {selected}" : "No selected node to toggle")))),
            TvSample.Section("Status", null,
                TvSample.Readout("IsHierarchical", "False"),
                TvSample.Readout("ChildrenPropertyName", "(native-only)"),
                TvSample.Readout("Selected node", selected),
                TvSample.Readout("Sorted columns", "(use headers)"),
                TvSample.Readout("Filtered columns", "(use funnels)"),
                TvSample.Readout("Last action", lastAction)));

        return TvSample.Page("Hierarchy",
            "Click a chevron to expand / collapse a node. Sorting and filtering work per level and keep the tree path visible. Use Expand all / Collapse all too.",
            table, options);
    }
}

class TvRowColorsPage : Component
{
    static readonly string[] Presets = { "None", "Default theme", "Custom colors" };

    public override Element Render()
    {
        var (preset, setPreset) = UseState(1);

        var setters = preset switch
        {
            2 => new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x33, 0x38, 0xB5, 0xFF) },
            1 => new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x18, 0x80, 0x80, 0x80) },
            _ => new Action<WinTV>[] { tv => tv.AlternatingRowBackground = null! },
        };

        var table = TableView(People, TextColumns(), height: 460) with
        {
            SelectionMode = TVSel.Extended,
            Setters = setters,
        };

        var options = VStack(16,
            TvSample.Section("Row banding", "Pick a preset to set alternating-row background colors. Each click instantly re-tints the rows; None clears them.",
                RadioButtons(Presets, preset, setPreset)),
            TvSample.Section("Status", null,
                TvSample.Readout("Active mode", Presets[preset])));

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

    public override Element Render()
    {
        var (lines, setLines) = UseState(3);
        var (mode, setMode) = UseState(0);
        var (banding, setBanding) = UseState(0);

        var grid = lines switch { 0 => TVGrid.None, 1 => TVGrid.Horizontal, 2 => TVGrid.Vertical, _ => TVGrid.All };
        var setters = banding == 1
            ? new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x33, 0x38, 0xB5, 0xFF) }
            : new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x18, 0x80, 0x80, 0x80) };

        var table = TableView(People, TextColumns(), height: 460) with
        {
            SelectionMode = TVSel.Single,
            GridLinesVisibility = grid,
            Setters = setters,
        };

        var options = VStack(16,
            TvSample.Section("Grid lines", null,
                RadioButtons(Lines, lines, setLines)),
            TvSample.Section("Mode", null,
                RadioButtons(Modes, mode, setMode),
                mode == 0 ? TextBlock("") : Caption("Grouped and hierarchical layouts are native-only in the consumable wrapper; the flat table remains visible so you can compare grid-line styling.")),
            TvSample.Section("Row banding", null,
                RadioButtons(Banding, banding, setBanding)),
            TvSample.Section("Status", null,
                TvSample.Readout("GridLinesVisibility", Lines[lines]),
                TvSample.Readout("Mode", Modes[mode]),
                TvSample.Readout("Row banding", Banding[banding])));

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

        var table = TableView(People, columns, height: 460) with
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

        var table = TableView(People, TextColumns(), height: 460) with
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
                TvSample.Readout("Visibility mode", Modes[mode]),
                TvSample.Readout("SelectionChanged fires", changes.ToString()),
                TvSample.Readout("Selected row", selected)));

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
        var table = TableView(People, VibrantColumns(), height: 460) with
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

        var sel = mode switch { 0 => TVSel.None, 1 => TVSel.Single, 2 => TVSel.Multiple, _ => TVSel.Extended };
        var gesture = !marquee
            ? "Gesture disabled — turn CanUserMarqueeSelect on."
            : sel is TVSel.None or TVSel.Single
                ? "Gesture unavailable in None / Single mode — choose Multiple or Extended."
                : $"Ready — drag in the blank area below the last row ({People.Count} rows loaded).";

        var table = TableView(People, TextColumns(), height: 460) with
        {
            SelectionMode = sel,
            IsSelectionGutterVisible = true,
            OnControlReady = tv => _tv = tv,
            OnSelectionChanged = _ =>
            {
                setSelectedCount(_tv?.SelectedItems?.Count ?? 0);
                setSelectedIndex(_tv?.SelectedIndex ?? -1);
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
                TvSample.Readout("SelectedIndex", selectedIndex.ToString())));

        return TvSample.Page("Marquee",
            "Click and drag in an empty area of the table to draw a selection rectangle; on release, the rows it touches are selected.",
            table, options);
    }
}
