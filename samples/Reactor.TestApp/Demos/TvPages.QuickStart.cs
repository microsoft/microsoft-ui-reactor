using System;
using System.Collections.Generic;
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
using TVUnit = Microsoft.UI.Xaml.Controls.TableViewSelectionUnit;
using SortDir = Microsoft.UI.Xaml.Controls.Primitives.SortDirection;
using TVColumn = Microsoft.UI.Xaml.Controls.TableViewColumn;

namespace Reactor.TestApp.TableViewGallery;

// ── QUICK START section (gold-standard template the other section files mirror) ───────────────────
//
// Pattern reference for sibling page files:
//   * Declarative options drive the consumable control's element props via UseState.
//   * Imperative options (Select all, SortByColumn presets, AutoSize, scroll, clipboard) capture the
//     live native control once via `OnControlReady = tv => _tv = tv;` then call its methods in onClick.
//   * Live readouts use OnSelectionChanged (re-attached each render) + UseState counters, surfaced via
//     TvSample.Readout(label, value). TvSample.Section(header, caption, …) titles each options block.

class TvShowcasePage : Component
{
    static readonly string[] Modes = { "Flat", "Grouped", "Hierarchical" };
    static readonly string[] RowCounts = { "10", "100", "500", "1000" };
    static readonly int[] RowCountVals = { 10, 100, 500, 1000 };
    static readonly string[] Banding = { "Default theme row colors", "Custom banding" };
    static readonly string[] Intervals = { "Off", "1000 ms", "500 ms", "200 ms" };
    static readonly int[] IntervalVals = { 0, 1000, 500, 200 };

    private System.Collections.ObjectModel.ObservableCollection<LivePerson>? _flat;
    private List<LivePerson>? _roots;
    private List<LivePerson>? _flatView;
    private int _flatViewN = -1;
    private readonly Random _rng = new(12345);

    public override Element Render()
    {
        var (mode, setMode) = UseState(2);   // Hierarchical default — matches the reference Showcase.
        var (rc, setRc) = UseState(1);
        var (vibrant, setVibrant) = UseState(true);
        var (banding, setBanding) = UseState(0);
        var (gutter, setGutter) = UseState(false);
        var (live, setLive) = UseState(0);

        _flat ??= LivePeople(1000);
        _roots ??= HierarchyRoots();

        // Live updates: a DispatcherTimer mutates a few visible rows' Salary/IsActive in place; because
        // LivePerson raises PropertyChanged, the bound cells + tint converters re-run WITHOUT a re-bind
        // (so scroll + selection are preserved). UseEffect (re)starts the timer when interval/mode change.
        UseEffect(() =>
        {
            if (live == 0) return () => { };
            var src = mode == 2 ? FlattenRoots(_roots!) : (IList<LivePerson>)_flat!;
            var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IntervalVals[live]) };
            timer.Tick += (_, __) =>
            {
                int top = Math.Min(src.Count, 28);
                for (int k = 0; k < 3 && top > 0; k++)
                {
                    var p = src[_rng.Next(top)];
                    p.Salary = 60000 + _rng.Next(0, 180000);
                    if (_rng.Next(6) == 0) p.IsActive = !p.IsActive;
                }
            };
            timer.Start();
            return () => timer.Stop();
        }, live, mode);

        int flatN = RowCountVals[rc];
        if (_flatViewN != flatN) { _flatView = _flat!.Take(flatN).ToList(); _flatViewN = flatN; }

        var setters = banding == 1
            ? new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x22, 0x66, 0x7E, 0xEA) }
            : new Action<WinTV>[] { tv => tv.AlternatingRowBackground = null! };
        var cols = vibrant ? VibrantColumns() : TextColumns();

        TableViewElement table;
        if (mode == 2)
        {
            table = TableView(Array.Empty<object>(), cols) with
            {
                HierarchicalItems = _roots,
                HierarchicalChildrenPath = nameof(LivePerson.Children),
                ExpandFirstLevel = true,
                CanResizeColumns = true,
                Setters = setters,
            };
        }
        else
        {
            var data = mode == 1 ? _flatView!.OrderBy(p => p.Department).ToList() : _flatView!;
            table = TableView(data, cols) with
            {
                CanSortColumns = true,
                CanFilterColumns = true,
                CanResizeColumns = true,
                FrozenColumnCount = 1,
                IsSelectionGutterVisible = gutter,
                SelectionMode = TVSel.Extended,
                Setters = setters,
            };
        }

        string source = mode switch { 1 => $"Grouped · {flatN:N0} rows", 2 => $"Hierarchical · {_roots!.Count} roots", _ => $"Flat · {flatN:N0} rows" };

        var options = VStack(16,
            TvSample.Section("Mode", "Switch the source between flat, grouped, and hierarchical.",
                RadioButtons(Modes, mode, setMode),
                mode == 1 ? Caption("Grouped headers are a native shaping feature; this view orders by Department.") : TextBlock("")),
            TvSample.Section("Row count", "Flat mode only — grouped and hierarchical use a curated set.",
                ComboBox(RowCounts, rc, setRc)),
            TvSample.Section("Live updates", "Refresh a few rows' Salary on a timer; the bound tint re-runs per cell, in place.",
                RadioButtons(Intervals, live, setLive)),
            TvSample.Section("Appearance", null,
                ToggleSwitch(vibrant, setVibrant, onContent: "Vibrant cells", offContent: "Plain text"),
                RadioButtons(Banding, banding, setBanding)),
            TvSample.Section("Selection", "Ctrl/Shift/marquee multi-select works either way. On shows a checkbox gutter; off is gutter-free.",
                ToggleSwitch(gutter, setGutter, onContent: "Multi-select gutter", offContent: "Off")),
            TvSample.Section("Status", null,
                TvSample.Readout("Source", source),
                TvSample.Readout("Live updates", live == 0 ? "Off" : Intervals[live])));

        return TvSample.Page("Showcase",
            "Default is Hierarchical (expand a row's chevron to drill in). Switch modes, change the flat row count, turn on " +
            "Live updates to watch Salary tints recolor in place, toggle banding / the selection gutter. Click a header to sort; a funnel to filter.",
            table, options,
            sourceCode:
@"// Hierarchical (tree-grid) mode — bind roots + the child-collection property, expand the first level:
var roots = HierarchyRoots();                       // List<LivePerson>, each with .Children
TableView(items: Array.Empty<object>(), columns: VibrantColumns()) with
{
    HierarchicalItems        = roots,
    HierarchicalChildrenPath = nameof(LivePerson.Children),
    ExpandFirstLevel         = true,
    CanResizeColumns         = true,
};

// Live updates — a timer mutates Salary in place; LivePerson raises PropertyChanged so the
// bound tint cell re-runs its converter WITHOUT a re-bind (selection + scroll preserved):
UseEffect(() =>
{
    if (intervalMs == 0) return () => { };
    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
    timer.Tick += (_, _) => row.Salary = 60000 + rng.Next(180000);
    timer.Start();
    return () => timer.Stop();
}, intervalMs);");
    }

    static IList<LivePerson> FlattenRoots(List<LivePerson> roots)
    {
        var list = new List<LivePerson>();
        void Walk(LivePerson p) { list.Add(p); foreach (var c in p.Children) Walk(c); }
        foreach (var r in roots) Walk(r);
        return list;
    }
}

class TvSelectionPage : Component
{
    static readonly string[] Modes = { "None", "Single", "Multiple", "Extended" };
    WinTV? _tv;

    public override Element Render()
    {
        var (mode, setMode) = UseState(3);
        var (gutter, setGutter) = UseState(false);
        var (count, setCount) = UseState(0);
        var (index, setIndex) = UseState(-1);
        var (changes, setChanges) = UseState(0);

        var sel = mode switch { 0 => TVSel.None, 1 => TVSel.Single, 2 => TVSel.Multiple, _ => TVSel.Extended };
        string hint = mode switch
        {
            0 => "No rows can be selected.",
            1 => "Click a row to select exactly one.",
            2 => "Click rows to toggle them; multiple stay selected.",
            _ => "Click + Ctrl / Shift to extend a contiguous or disjoint selection.",
        };

        var table = TableView(People, TextColumns()) with
        {
            SelectionMode = sel,
            IsSelectionGutterVisible = gutter,
            OnControlReady = tv => _tv = tv,
            OnSelectionChanged = _ =>
            {
                setChanges(changes + 1);
                if (_tv != null)
                {
                    setCount(_tv.SelectedItems?.Count ?? 0);
                    setIndex(_tv.SelectedIndex);
                }
            },
        };

        var options = VStack(16,
            TvSample.Section("Mode and quick actions", "Pick a selection mode, then use the buttons to change the selection from code.",
                ComboBox(Modes, mode, setMode),
                ToggleSwitch(gutter, setGutter, onContent: "Selection gutter", offContent: "Off"),
                HStack(8, Button("Select first", () => _tv?.Select(0)), Button("Select last", () => _tv?.Select(People.Count - 1))),
                HStack(8, Button("Select all", () => _tv?.SelectAll()), Button("Clear", () => _tv?.DeselectAll())),
                Caption(hint)),
            TvSample.Section("Status", null,
                TvSample.Readout("Selected count", count.ToString()),
                TvSample.Readout("SelectedIndex", index.ToString()),
                TvSample.Readout("SelectionChanged fires", changes.ToString())));

        return TvSample.Page("Selection",
            "Pick a mode, then click rows or use the buttons to drive the selection from code. The Status panel updates live.",
            table, options,
            sourceCode:
@"WinTV? tv = null;
var table = TableView(People, TextColumns()) with
{
    SelectionMode            = TableViewSelectionMode.Extended,
    IsSelectionGutterVisible = gutter,
    OnControlReady           = t => tv = t,                 // capture for imperative APIs
    OnSelectionChanged       = _ =>                          // live readouts
    {
        setChanges(changes + 1);
        setCount(tv!.SelectedItems?.Count ?? 0);
        setIndex(tv!.SelectedIndex);
    },
};
// Quick-action buttons:
Button(""Select all"", () => tv?.SelectAll());
Button(""Select first"", () => tv?.Select(0));
Button(""Clear"", () => tv?.DeselectAll());");
    }
}

class TvCellSelectionPage : Component
{
    static readonly string[] Units = { "Row", "Cell", "CellOrRow" };
    WinTV? _tv;

    public override Element Render()
    {
        var (idx, setIdx) = UseState(1);
        var (changes, setChanges) = UseState(0);
        var unit = idx switch { 0 => TVUnit.Row, 2 => TVUnit.CellOrRow, _ => TVUnit.Cell };
        string desc = idx switch
        {
            0 => "Selecting highlights the whole row.",
            1 => "Selecting highlights an individual cell.",
            _ => "Cell selection in the cell area; row selection via the gutter.",
        };

        var table = TableView(People, TextColumns()) with
        {
            SelectionMode = TVSel.Extended,
            SelectionUnit = unit,
            IsSelectionGutterVisible = true,
            OnControlReady = tv => _tv = tv,
            OnSelectionChanged = _ => setChanges(changes + 1),
        };

        var options = VStack(16,
            TvSample.Section("Selection unit", "Row selects whole rows. Cell selects individual cells. CellOrRow allows both.",
                ComboBox(Units, idx, setIdx),
                Caption(desc),
                Button("Clear selection", () => _tv?.DeselectAll())),
            TvSample.Section("Gestures", null,
                Caption("\u2022 Click a cell to select just that cell.\n\u2022 Ctrl+click toggles a cell.\n\u2022 Shift+click selects a rectangular range from the anchor.")),
            TvSample.Section("Status", null,
                TvSample.Readout("SelectedCellsChanged fires", changes.ToString())));

        return TvSample.Page("CellSelection",
            "Pick a unit. In Cell / CellOrRow, click a cell to select it; Ctrl+click toggles, Shift+click selects a range.",
            table, options,
            sourceCode:
@"// SelectionUnit chooses what a click selects: a whole Row, a single Cell, or CellOrRow.
var table = TableView(People, TextColumns()) with
{
    SelectionMode            = TableViewSelectionMode.Extended,
    SelectionUnit            = unit,        // TableViewSelectionUnit.Row / Cell / CellOrRow
    IsSelectionGutterVisible = true,
    OnControlReady           = t => _tv = t,
    OnSelectionChanged       = _ => setChanges(changes + 1),
};
Button(""Clear selection"", () => _tv?.DeselectAll());");
    }
}

class TvSortPage : Component
{
    WinTV? _tv;

    TVColumn? Column(string header) => _tv?.Columns?.FirstOrDefault(c => Equals(c.Header, header));

    public override Element Render()
    {
        var (fires, setFires) = UseState(0);
        var (priority, setPriority) = UseState("(none)");

        void Refresh()
        {
            if (_tv == null) return;
            var sc = _tv.SortedColumns.OrderBy(c => c.SortIndex)
                .Select(c => $"{c.SortIndex}. {c.Header} {c.SortDirection}");
            setPriority(sc.Any() ? string.Join("  ·  ", sc) : "(none)");
        }

        var table = TableView(People, VibrantColumns()) with
        {
            CanSortColumns = true,
            CanResizeColumns = true,
            FrozenColumnCount = 1,
            OnControlReady = tv => { _tv = tv; tv.Sorted += (_, __) => { setFires(fires + 1); Refresh(); }; },
        };

        var options = VStack(16,
            TvSample.Section("Programmatic sort", "Each button adds or changes a sort level; the header arrows + priority badges update to match.",
                HStack(8, Button("Sort by Salary (desc)", () => { if (Column("Salary") is { } c) _tv!.SortByColumn(c, SortDir.Descending); Refresh(); }),
                           Button("Add Department (asc)", () => { if (Column("Department") is { } c) _tv!.SetSortColumn(c, SortDir.Ascending); Refresh(); })),
                HStack(8, Button("Toggle Salary", () => { if (Column("Salary") is { } c) _tv!.ToggleSortDirection(c, Microsoft.UI.Xaml.Controls.TableViewSortToggleMode.Replace); Refresh(); }),
                           Button("Clear sort", () => { _tv?.ClearSort(); Refresh(); }))),
            TvSample.Section("Status", null,
                TvSample.Readout("Sorted fires", fires.ToString()),
                TvSample.Readout("Sort priority", priority)));

        return TvSample.Page("Sort",
            "Click a column header to sort; click again to reverse; Ctrl-click another header to layer a secondary sort. Or use the buttons for programmatic presets.",
            table, options,
            sourceCode:
@"// The control owns sort STATE + raises Sorted; the consumer re-orders the data. The
// Reactor TableView handler does that re-shape for you — columns just need a SortMemberPath
// (set automatically from each TableColumn's property path). Drive it imperatively too:
var table = TableView(People, VibrantColumns()) with
{
    CanSortColumns   = true,
    FrozenColumnCount = 1,
    OnControlReady   = tv => { _tv = tv; tv.Sorted += (_, _) => { setFires(fires + 1); Refresh(); }; },
};
// Programmatic presets:
_tv.SortByColumn(Column(""Salary""), SortDirection.Descending);    // replace sort
_tv.SetSortColumn(Column(""Department""), SortDirection.Ascending); // add a level
_tv.ToggleSortDirection(Column(""Salary""), TableViewSortToggleMode.Replace);
_tv.ClearSort();");
    }
}

class TvFilterPage : Component
{
    WinTV? _tv;

    public override Element Render()
    {
        var (fires, setFires) = UseState(0);
        var (visible, setVisible) = UseState($"{People.Count} / {People.Count}");

        var table = TableView(People, TextColumns()) with
        {
            CanFilterColumns = true,
            CanSortColumns = true,
            OnControlReady = tv => { _tv = tv; tv.Filtered += (_, __) => { setFires(fires + 1); if (_tv != null) setVisible($"{(_tv.ItemsSource as System.Collections.ICollection)?.Count ?? People.Count} / {People.Count}"); }; },
        };

        var options = VStack(16,
            TvSample.Section("Filter", "Open a column header's funnel and choose values; the funnel marks filtered columns.",
                Button("Clear all filters", () => { if (_tv != null) foreach (var c in _tv.FilteredColumns.ToList()) c.Filter = null!; })),
            TvSample.Section("Status", null,
                TvSample.Readout("Filtered fires", fires.ToString()),
                TvSample.Readout("Visible rows", visible)));

        return TvSample.Page("Filter",
            "Open a column header's funnel and choose values to narrow the rows; clear a column's filter to restore it.",
            table, options,
            sourceCode:
@"// Funnels appear when CanFilterColumns = true. The control raises Filtered; the Reactor
// handler re-shapes the bound rows. Read the visible count from the live ItemsSource:
var table = TableView(People, TextColumns()) with
{
    CanFilterColumns = true,
    CanSortColumns   = true,
    OnControlReady   = tv =>
    {
        _tv = tv;
        tv.Filtered += (_, _) =>
        {
            setFires(fires + 1);
            int visible = (tv.ItemsSource as System.Collections.ICollection)?.Count ?? People.Count;
            setVisible($""{visible} / {People.Count}"");
        };
    },
};
// Clear every column filter:
foreach (var c in _tv.FilteredColumns.ToList()) c.Filter = null!;");
    }
}

class TvInlineEditPage : Component
{
    public override Element Render()
    {
        var (readOnly, setReadOnly) = UseState(false);
        var table = TableView(People, TextColumns()) with
        {
            SelectionMode = TVSel.Single,
            Setters = new Action<WinTV>[] { tv => tv.IsReadOnly = readOnly },
        };

        var options = VStack(16,
            TvSample.Section("Edit controls", "Double-click a cell (or select it and press F2) to edit; Enter commits, Esc discards.",
                ToggleSwitch(readOnly, setReadOnly, onContent: "Table IsReadOnly (no editing)", offContent: "Editing allowed")),
            TvSample.Section("Cell edit status", null,
                TvSample.Readout("IsReadOnly", readOnly.ToString())));

        return TvSample.Page("InlineEdit",
            "With editing allowed, double-click a text cell (or F2) to edit in place; Enter commits, Esc discards. Toggle the table read-only to lock it.",
            table, options,
            sourceCode:
@"// Text columns are editable unless the control is read-only. Toggle IsReadOnly via a Setter:
var table = TableView(People, TextColumns()) with
{
    SelectionMode = TableViewSelectionMode.Single,
    Setters       = new Action<WinTV>[] { tv => tv.IsReadOnly = readOnly },
};
// Double-click / F2 a cell to edit; Enter commits, Esc discards.");
    }
}

class TvKeyboardNavPage : Component
{
    public override Element Render()
    {
        var table = TableView(People, TextColumns()) with { SelectionMode = TVSel.Single, IsSelectionGutterVisible = true };

        var options = VStack(16,
            TvSample.Section("Keyboard shortcuts", "Click any row to take focus, then use the keyboard.",
                TvSample.Readout("Up / Down", "prev / next row"),
                TvSample.Readout("Home / End", "first / last row"),
                TvSample.Readout("PageUp / PageDown", "-1 / +1 viewport"),
                TvSample.Readout("Tab", "next cell")),
            TvSample.Section("Accessibility", null,
                Caption("The grid exposes its structure to UI Automation, so Narrator can announce rows and report the table's RowCount / ColumnCount.")));

        return TvSample.Page("KeyboardNav",
            "Click a cell, then use the arrow keys, Tab, Home / End, and Page Up / Page Down to move focus across cells.",
            table, options,
            sourceCode:
@"// Keyboard navigation + UI Automation are built in — just render the table:
var table = TableView(People, TextColumns()) with
{
    SelectionMode            = TableViewSelectionMode.Single,
    IsSelectionGutterVisible = true,
};
// Arrow keys / Tab / Home / End / PageUp / PageDown move focus across cells;
// the grid exposes RowCount / ColumnCount to Narrator automatically.");
    }
}

