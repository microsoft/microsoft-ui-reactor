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

    public override Element Render()
    {
        var (mode, setMode) = UseState(0);
        var (rc, setRc) = UseState(1);
        var (vibrant, setVibrant) = UseState(true);
        var (banding, setBanding) = UseState(0);
        var (gutter, setGutter) = UseState(false);

        var data = mode == 0 ? ManyPeople(RowCountVals[rc]) : (IReadOnlyList<Person>)People;
        var setters = banding == 1
            ? new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x22, 0x66, 0x7E, 0xEA) }
            : new Action<WinTV>[] { tv => tv.AlternatingRowBackground = null! };

        var table = TableView(data, vibrant ? VibrantColumns() : TextColumns(), height: 460) with
        {
            CanSortColumns = true,
            CanResizeColumns = true,
            CanFilterColumns = true,
            FrozenColumnCount = 1,
            IsSelectionGutterVisible = gutter,
            SelectionMode = TVSel.Extended,
            Setters = setters,
        };

        var options = VStack(16,
            TvSample.Section("Mode", "Switch the source between flat, grouped, and hierarchical.",
                RadioButtons(Modes, mode, setMode),
                mode != 0 ? Caption("Grouped / hierarchical shapes are provided by the native control; this gallery shows the flat source.") : TextBlock("")),
            TvSample.Section("Row count", "Flat mode only — grouped and hierarchical use a curated set.",
                ComboBox(RowCounts, rc, setRc)),
            TvSample.Section("Appearance", null,
                ToggleSwitch(vibrant, setVibrant, onContent: "Vibrant cells", offContent: "Plain text"),
                RadioButtons(Banding, banding, setBanding)),
            TvSample.Section("Selection", "Ctrl/Shift/marquee multi-select works either way. On shows a checkbox gutter; off is gutter-free.",
                ToggleSwitch(gutter, setGutter, onContent: "Multi-select gutter", offContent: "Off")),
            TvSample.Section("Status", null,
                TvSample.Readout("Source", mode == 0 ? $"Flat · {RowCountVals[rc]:N0} rows" : Modes[mode])));

        return TvSample.Page("Showcase",
            "Toggle vibrant cells, change the flat row count, switch banding, and show / hide the selection gutter. " +
            "Click a header to sort; click a funnel to filter; drag a header edge to resize.",
            table, options);
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

        var table = TableView(People, TextColumns(), height: 460) with
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
            table, options);
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

        var table = TableView(People, TextColumns(), height: 460) with
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
            table, options);
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

        var table = TableView(People, VibrantColumns(), height: 460) with
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
            table, options);
    }
}

class TvFilterPage : Component
{
    WinTV? _tv;

    public override Element Render()
    {
        var (fires, setFires) = UseState(0);
        var (visible, setVisible) = UseState($"{People.Count} / {People.Count}");

        var table = TableView(People, TextColumns(), height: 460) with
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
            table, options);
    }
}

class TvInlineEditPage : Component
{
    public override Element Render()
    {
        var (readOnly, setReadOnly) = UseState(false);
        var table = TableView(People, TextColumns(), height: 460) with
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
            table, options);
    }
}

class TvKeyboardNavPage : Component
{
    public override Element Render()
    {
        var table = TableView(People, TextColumns(), height: 460) with { SelectionMode = TVSel.Single, IsSelectionGutterVisible = true };

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
            table, options);
    }
}

