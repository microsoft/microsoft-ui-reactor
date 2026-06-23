using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Reactor.Controls.Factories;
using static Reactor.TestApp.TableViewGallery.TableViewSampleData;
using WinTV = Microsoft.UI.Xaml.Controls.TableView;
using TVGrid = Microsoft.UI.Xaml.Controls.TableViewGridLinesVisibility;
using TVHeaders = Microsoft.UI.Xaml.Controls.TableViewHeadersVisibility;
using TVSel = Microsoft.UI.Xaml.Controls.TableViewSelectionMode;
using TVUnit = Microsoft.UI.Xaml.Controls.TableViewSelectionUnit;
using TVFrozenEdge = Microsoft.UI.Xaml.Controls.TableViewFrozenEdge;

namespace Reactor.TestApp.TableViewGallery;

// ── Helpers shared by the gallery pages ──────────────────────────────────────────────────────────
static class TvFx
{
    public static Windows.UI.Color Argb(byte a, byte r, byte g, byte b) =>
        new() { A = a, R = r, G = g, B = b };

    public static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(byte a, byte r, byte g, byte b) =>
        new(Argb(a, r, g, b));

    /// <summary>Marks the last <paramref name="count"/> columns frozen to the trailing edge (post-ApplyColumns setter).</summary>
    public static Action<WinTV> FreezeTrailing(int count) => tv =>
    {
        int n = tv.Columns.Count;
        for (int i = 0; i < n; i++)
            tv.Columns[i].FrozenEdge = (i >= n - count && count > 0) ? TVFrozenEdge.Trailing : TVFrozenEdge.None;
    };
}

// ── Display pages: chrome + a live table whose behaviour is built into the native control ─────────
static class TvDisplayPages
{
    static Element Table(IReadOnlyList<TableColumn>? cols = null) =>
        TableView(People, cols ?? TextColumns(), height: 460);

    public static Element Home()
    {
        var (_, desc) = TvMeta.Of("Showcase");
        return ScrollView(VStack(16,
            Heading("TableView"),
            TextBlock("A native WinUI 3 tabular control for data-heavy desktop experiences — typed columns, " +
                "multi-column sort and filter, inline edit, frozen leading and trailing columns, hierarchical and " +
                "grouped rows, and two-axis virtualization with built-in keyboard navigation, accessibility, and " +
                "Excel-style clipboard."),
            InfoBar("First-class Reactor gallery", "Every page on the left is a pure-C# Reactor (MVU) component that " +
                "consumes the consumable TableView(items, columns) control — mirroring the native TableViewSamples gallery."),
            Card(VStack(8,
                SubHeading("Sections"),
                TextBlock("\u2022 Quick start — Showcase, Selection, Cell selection, Multi-column sort, Per-column filtering, Inline editing, Keyboard nav"),
                TextBlock("\u2022 Columns — resize, reorder, dynamic columns, sticky headers, headers visibility, frozen leading / trailing"),
                TextBlock("\u2022 Rows & cells — reorder, groups, hierarchy, banding, grid lines, row templates / details, mixed controls, marquee"),
                TextBlock("\u2022 Styling — conditional row + per-cell styling"),
                TextBlock("\u2022 Power user — advanced filter, clipboard, persisted layout, RTL, virtualization, pagination, data export"))).Padding(16),
            TableView(People, VibrantColumns(), height: 320) with { CanSortColumns = true, FrozenColumnCount = 1 }
        )).Padding(4);
    }

    public static Element Sort() =>
        TvSample.Page("Sort",
            "Click a column header to sort ascending; click it again to reverse. Ctrl-click a second header to layer a secondary sort.",
            TableView(People, VibrantColumns(), height: 460) with { CanSortColumns = true, CanResizeColumns = true, FrozenColumnCount = 1 });

    public static Element Filter() =>
        TvSample.Page("Filter",
            "Open a column header's funnel and choose values to narrow the rows. The funnel marks filtered columns; clear it to reset.",
            TableView(People, TextColumns(), height: 460) with { CanFilterColumns = true, CanSortColumns = true });

    public static Element AdvancedFilter() =>
        TvSample.Page("AdvancedFilter",
            "Click a column's funnel to build a typed predicate (Contains, Equals, IsEmpty, …). Filters compose across columns.",
            TableView(People, TextColumns(), height: 460) with { CanFilterColumns = true });

    public static Element InlineEdit() =>
        TvSample.Page("InlineEdit",
            "Double-click a text cell (or select it and press F2) to edit in place; Enter commits, Esc discards.",
            TableView(People, TextColumns(), height: 460) with { SelectionMode = TVSel.Single });

    public static Element KeyboardNav() =>
        TvSample.Page("KeyboardNav",
            "Click a cell, then use the arrow keys, Tab, Home / End, and Page Up / Page Down to move focus across cells.",
            TableView(People, TextColumns(), height: 460) with { SelectionMode = TVSel.Single });

    public static Element ColumnReorder() =>
        TvSample.Page("ColumnReorder",
            "Drag a column header sideways to reorder it; each column keeps its own width as it moves.",
            TableView(People, TextColumns(), height: 460) with { CanReorderColumns = true, CanResizeColumns = true });

    public static Element ColumnReorderGesture() =>
        TvSample.Page("ColumnReorderGesture",
            "Drag any column header sideways; a drop indicator shows the target slot before you release.",
            TableView(People, TextColumns(), height: 460) with { CanReorderColumns = true });

    public static Element StickyHeaders() =>
        TvSample.Page("StickyHeaders",
            "Scroll the table down — the column header row stays pinned at the top while the rows scroll under it.",
            TableView(People, VibrantColumns(), height: 320) with { GridLinesVisibility = TVGrid.Horizontal });

    public static Element RowReorder() =>
        TvSample.Page("RowReorder",
            "Drag a row by its cells to a new position; the RowReordered event reports the (from, to) index pair.",
            TableView(People, TextColumns(), height: 460) with
            {
                SelectionMode = TVSel.Single,
                Setters = new Action<WinTV>[] { tv => tv.CanUserReorderRows = true },
            });

    public static Element Groups() =>
        TvSample.Page("Groups",
            "The directory below is ordered by Department to suggest grouping; the native control adds collapsible group headers.",
            TableView(People.OrderBy(p => p.Department).ToList(), VibrantColumns(), height: 460) with { CanSortColumns = true },
            extraInfo: TvSample.NativeNote("Group headers + bulk expand / collapse are provided by the native control's grouped ItemsSource shape."));

    public static Element Hierarchy() =>
        TvSample.Page("Hierarchy",
            "Tree-grid mode nests child rows under a parent with an expand chevron in the first column.",
            TableView(People, VibrantColumns(), height: 460),
            extraInfo: TvSample.NativeNote("N-level hierarchy is driven by HierarchicalChildrenPropertyName on the native control."));

    public static Element RowTemplate() =>
        TvSample.Page("RowTemplate",
            "A single DataTemplate can render the whole row (card-style / two-line) instead of per-column cells.",
            TableView(People, VibrantColumns(), height: 460),
            extraInfo: TvSample.NativeNote("Custom whole-row templates are set via the native control's row template surface."));

    public static Element RowDetails() =>
        TvSample.Page("RowDetails",
            "Each row can expand a details area below it (RowDetailsTemplate + RowDetailsVisibilityMode).",
            TableView(People, VibrantColumns(), height: 460) with { SelectionMode = TVSel.Single },
            extraInfo: TvSample.NativeNote("The per-row details panel is provided by the native RowDetailsTemplate."));

    public static Element MixedControls() =>
        TvSample.Page("MixedControls",
            "Cells can host DatePicker / ComboBox / CheckBox via template columns, with TwoWay bindings back to the row.",
            TableView(People, VibrantColumns(), height: 460),
            extraInfo: TvSample.NativeNote("In-cell interactive controls use TableViewTemplateColumn cell templates."));

    public static Element Marquee() =>
        TvSample.Page("Marquee",
            "Press and drag in the table's empty space to draw a selection rectangle; every row it touches is selected.",
            TableView(People, TextColumns(), height: 460) with { SelectionMode = TVSel.Multiple, IsSelectionGutterVisible = true });

    public static Element Clipboard() =>
        TvSample.Page("Clipboard",
            "Select one or more rows / cells and press Ctrl+C to copy Excel-compatible TSV; Ctrl+D fills down.",
            TableView(People, TextColumns(), height: 460) with { SelectionMode = TVSel.Extended, IsSelectionGutterVisible = true });

    public static Element Layout() =>
        TvSample.Page("Layout",
            "Sort + reorder some columns, then imagine capturing that arrangement to a token and restoring it later.",
            TableView(People, TextColumns(), height: 460) with { CanSortColumns = true, CanReorderColumns = true, CanResizeColumns = true },
            extraInfo: TvSample.NativeNote("Serializing / restoring the sort + column order + frozen-edge token is a native control API."));

    public static Element About()
    {
        var (_, desc) = TvMeta.Of("About");
        return ScrollView(VStack(16,
            Heading("About"),
            TextBlock(desc),
            Card(VStack(8,
                SubHeading("Native control"),
                TextBlock("Microsoft.UI.Xaml.Controls.TableView — a separate-binary split control " +
                          "(Microsoft.UI.Xaml.Controls.Advanced.dll), projected via CsWinRT vs public WinAppSDK 2.0.1."),
                TextBlock("The Release sample consumes the optimized (fre) binary; Debug consumes the checked (chk) binary."))).Padding(16)
        )).Padding(4);
    }
}

// ── Interactive pages (Options panel + UseState driving the live table) ──────────────────────────
class TvShowcasePage : Component
{
    public override Element Render()
    {
        var (vibrant, setVibrant) = UseState(true);
        var (grid, setGrid) = UseState(true);

        var table = TableView(People, vibrant ? VibrantColumns() : TextColumns(), height: 460) with
        {
            GridLinesVisibility = grid ? TVGrid.Horizontal : TVGrid.None,
            CanSortColumns = true,
            CanResizeColumns = true,
            FrozenColumnCount = 1,
        };

        var options = VStack(14,
            TvSample.Group("Vibrant cells", ToggleSwitch(vibrant, setVibrant, onContent: "Pills / chips / tints", offContent: "Plain text")),
            TvSample.Group("Grid lines", ToggleSwitch(grid, setGrid, onContent: "Horizontal", offContent: "None")));

        return TvSample.Page("Showcase",
            "Toggle Vibrant cells to switch Department / Status / Salary between colored template cells and plain text. " +
            "Click a header to sort; drag a header edge to resize.",
            table, options);
    }
}

class TvSelectionPage : Component
{
    static readonly string[] Modes = { "None", "Single", "Multiple", "Extended" };

    public override Element Render()
    {
        var (mode, setMode) = UseState(2);
        var (gutter, setGutter) = UseState(true);
        var sel = mode switch { 0 => TVSel.None, 1 => TVSel.Single, 3 => TVSel.Extended, _ => TVSel.Multiple };

        var table = TableView(People, TextColumns(), height: 460) with { SelectionMode = sel, IsSelectionGutterVisible = gutter };
        var options = VStack(14,
            TvSample.Group("Selection mode", RadioButtons(Modes, mode, setMode)),
            TvSample.Group("Selection gutter", ToggleSwitch(gutter, setGutter, onContent: "Visible", offContent: "Hidden")));

        return TvSample.Page("Selection",
            "Pick a selection mode, then click rows (and the header checkbox) to select. Toggle the leading gutter on or off.",
            table, options);
    }
}

class TvCellSelectionPage : Component
{
    static readonly string[] Units = { "Row", "Cell", "CellOrRow" };

    public override Element Render()
    {
        var (idx, setIdx) = UseState(1);
        var unit = idx switch { 0 => TVUnit.Row, 2 => TVUnit.CellOrRow, _ => TVUnit.Cell };

        var table = TableView(People, TextColumns(), height: 460) with
        {
            SelectionMode = TVSel.Extended,
            SelectionUnit = unit,
            IsSelectionGutterVisible = true,
        };
        var options = TvSample.Group("Selection unit", RadioButtons(Units, idx, setIdx));

        return TvSample.Page("CellSelection",
            "Pick a unit. In Cell / CellOrRow, click a cell to select it; Ctrl+click toggles, Shift+click selects a range.",
            table, options);
    }
}

class TvColumnResizePage : Component
{
    public override Element Render()
    {
        var (resize, setResize) = UseState(true);
        var table = TableView(People, TextColumns(), height: 460) with { CanResizeColumns = resize };
        var options = TvSample.Group("Resizing", ToggleSwitch(resize, setResize, onContent: "Drag header edges", offContent: "Locked"));
        return TvSample.Page("ColumnResize",
            "Drag a column's right edge to resize it, or lock resizing off below.", table, options);
    }
}

class TvDynamicColumnsPage : Component
{
    static readonly (string Label, TableColumn Col)[] All =
    {
        ("First name", new TableColumn("First name", nameof(Person.FirstName), Width: 110)),
        ("Department", new TableColumn("Department", nameof(Person.Department), CellStyle.Pill, Width: 150)),
        ("Status", new TableColumn("Status", nameof(Person.IsActive), CellStyle.Chip, Width: 100)),
        ("Salary", new TableColumn("Salary", nameof(Person.Salary), CellStyle.Tint, Width: 120)),
        ("Join date", new TableColumn("Join date", nameof(Person.JoinDateText), Width: 110)),
        ("Role", new TableColumn("Role", nameof(Person.Role), Width: 170)),
        ("Email", new TableColumn("Email", nameof(Person.Email), Width: 220)),
    };

    public override Element Render()
    {
        var (mask, setMask) = UseState(0b1111111);
        var cols = new List<TableColumn>();
        for (int i = 0; i < All.Length; i++)
            if ((mask & (1 << i)) != 0) cols.Add(All[i].Col);
        if (cols.Count == 0) cols.Add(All[0].Col);

        var table = TableView(People, cols, height: 460) with { CanSortColumns = true };

        var toggles = new List<Element>();
        for (int i = 0; i < All.Length; i++)
        {
            int bit = 1 << i;
            bool on = (mask & bit) != 0;
            toggles.Add(ToggleSwitch(on, v => setMask(v ? (mask | bit) : (mask & ~bit)),
                onContent: All[i].Label, offContent: All[i].Label));
        }

        return TvSample.Page("DynamicColumns",
            "Toggle a column below to show or hide it at runtime; the remaining columns keep their place and width.",
            table, VStack(8, toggles.ToArray()));
    }
}

class TvHeadersPage : Component
{
    static readonly string[] Vis = { "None", "Column", "Row", "All" };

    public override Element Render()
    {
        var (idx, setIdx) = UseState(1);
        var hv = idx switch { 0 => TVHeaders.None, 2 => TVHeaders.Row, 3 => TVHeaders.All, _ => TVHeaders.Column };
        var table = TableView(People, TextColumns(), height: 460) with { HeadersVisibility = hv, GridLinesVisibility = TVGrid.Horizontal, IsSelectionGutterVisible = true };
        return TvSample.Page("HeadersVisibility", "Switch headers visibility and watch the column-header band + row gutter appear / disappear.",
            table, TvSample.Group("Headers visibility", RadioButtons(Vis, idx, setIdx)));
    }
}

class TvFrozenLeadingPage : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(2);
        var table = TableView(People, TextColumns(), height: 460) with { FrozenColumnCount = count, CanResizeColumns = true };
        var options = VStack(10, SubHeading($"Frozen leading: {count}"),
            Slider((double)count, 0, 4, v => setCount((int)Math.Round(v))),
            TextBlock("The first N columns pin to the leading edge during horizontal scroll."));
        return TvSample.Page("FrozenColumns", "Set the frozen count, then scroll the table horizontally — the first N columns stay pinned.", table, options);
    }
}

class TvFrozenTrailingPage : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(1);
        var table = TableView(People, TextColumns(), height: 460) with
        {
            CanResizeColumns = true,
            Setters = new[] { TvFx.FreezeTrailing(count) },
        };
        var options = VStack(10, SubHeading($"Frozen trailing: {count}"),
            Slider((double)count, 0, 4, v => setCount((int)Math.Round(v))),
            TextBlock("The last N columns pin to the trailing edge during horizontal scroll."));
        return TvSample.Page("FrozenTrailingColumns", "Set the trailing-frozen count, then scroll horizontally — the last N columns stay pinned to the right.", table, options);
    }
}

class TvRowColorsPage : Component
{
    public override Element Render()
    {
        var (banded, setBanded) = UseState(true);
        var setters = banded
            ? new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x18, 0x80, 0x80, 0x80) }
            : new Action<WinTV>[] { tv => tv.AlternatingRowBackground = null! };
        var table = TableView(People, VibrantColumns(), height: 460) with { Setters = setters };
        return TvSample.Page("RowColors", "Toggle zebra striping — AlternatingRowBackground paints every other row.",
            table, TvSample.Group("Banding", ToggleSwitch(banded, setBanded, onContent: "Zebra striping", offContent: "None")));
    }
}

class TvGridLinesPage : Component
{
    static readonly string[] Vis = { "None", "Horizontal", "Vertical", "All" };

    public override Element Render()
    {
        var (idx, setIdx) = UseState(3);
        var gl = idx switch { 0 => TVGrid.None, 1 => TVGrid.Horizontal, 2 => TVGrid.Vertical, _ => TVGrid.All };
        var table = TableView(People, TextColumns(), height: 460) with { GridLinesVisibility = gl };
        return TvSample.Page("GridLines", "Pick a grid-lines option and watch the per-row + per-cell borders redraw live.",
            table, TvSample.Group("Grid lines", RadioButtons(Vis, idx, setIdx)));
    }
}

class TvConditionalStylingPage : Component
{
    static readonly string[] Presets = { "None", "Zebra striping", "Highlight high salary" };

    public override Element Render()
    {
        var (idx, setIdx) = UseState(1);
        Action<WinTV>[] setters = idx switch
        {
            1 => new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x18, 0x80, 0x80, 0x80) },
            2 => new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x22, 0x16, 0xA3, 0x4A) },
            _ => new Action<WinTV>[] { tv => tv.AlternatingRowBackground = null! },
        };
        var cols = idx == 2 ? VibrantColumns() : TextColumns();
        var table = TableView(People, cols, height: 460) with { Setters = setters, CanSortColumns = true };
        return TvSample.Page("ConditionalStyling", "Pick a preset — rows re-tint based on the data (zebra, or a salary-tier highlight).",
            table, TvSample.Group("Row styling preset", RadioButtons(Presets, idx, setIdx)));
    }
}

class TvCellStylingPage : Component
{
    public override Element Render()
    {
        var (vibrant, setVibrant) = UseState(true);
        var table = TableView(People, vibrant ? VibrantColumns() : TextColumns(), height: 460) with { CanSortColumns = true };
        return TvSample.Page("CellStyling", "Toggle the vibrant preset — saturated Department pills, stoplight Salary tints, and a status chip on Active.",
            table, TvSample.Group("Vibrant cells", ToggleSwitch(vibrant, setVibrant, onContent: "On", offContent: "Off")));
    }
}

class TvRtlPage : Component
{
    public override Element Render()
    {
        var (rtl, setRtl) = UseState(true);
        var setters = new Action<WinTV>[] { tv => tv.FlowDirection = rtl ? Microsoft.UI.Xaml.FlowDirection.RightToLeft : Microsoft.UI.Xaml.FlowDirection.LeftToRight };
        var table = TableView(People, TextColumns(), height: 460) with { CanSortColumns = true, CanReorderColumns = true, Setters = setters };
        return TvSample.Page("RTLPlayground", "Toggle RightToLeft — columns flow right-to-left and the whole layout mirrors.",
            table, TvSample.Group("Flow direction", ToggleSwitch(rtl, setRtl, onContent: "RightToLeft", offContent: "LeftToRight")));
    }
}

class TvVirtualizationPage : Component
{
    static readonly string[] Sizes = { "1,000", "10,000", "50,000" };
    static readonly int[] Counts = { 1000, 10000, 50000 };

    public override Element Render()
    {
        var (idx, setIdx) = UseState(1);
        var n = Counts[idx];
        var table = TableView(ManyPeople(n), TextColumns(), height: 460) with { CanSortColumns = true };
        var options = VStack(12,
            TvSample.Group("Dataset size", RadioButtons(Sizes, idx, setIdx)),
            TextBlock($"Bound rows: {n:N0}. Only the visible viewport is realized — scroll stays responsive."));
        return TvSample.Page("Virtualization", "Pick a dataset size; the native control realizes only the visible viewport.", table, options);
    }
}

class TvPaginationPage : Component
{
    const int PageSize = 50;
    const int Total = 1000;

    public override Element Render()
    {
        var (page, setPage) = UseState(0);
        var all = ManyPeople(Total);
        int pages = (Total + PageSize - 1) / PageSize;
        var window = all.Skip(page * PageSize).Take(PageSize).ToList();

        var table = TableView(window, TextColumns(), height: 460) with { CanSortColumns = true };
        var options = VStack(12,
            SubHeading($"Page {page + 1} / {pages}"),
            TextBlock($"Rows {page * PageSize + 1:N0}–{Math.Min((page + 1) * PageSize, Total):N0} of {Total:N0}"),
            HStack(8,
                Button("\u2190 Prev", () => setPage(Math.Max(0, page - 1))),
                Button("Next \u2192", () => setPage(Math.Min(pages - 1, page + 1)))));
        return TvSample.Page("Pagination", "Use Prev / Next to page a 50-row window over a 1,000-row source.", table, options);
    }
}

class TvDataExportPage : Component
{
    static readonly string[] Formats = { "CSV", "TSV", "JSON" };

    public override Element Render()
    {
        var (fmt, setFmt) = UseState(0);
        var (preview, setPreview) = UseState("");

        var table = TableView(People, TextColumns(), height: 360) with { CanSortColumns = true };
        var options = VStack(12,
            TvSample.Group("Format", RadioButtons(Formats, fmt, setFmt)),
            Button("Export", () => setPreview(Export(fmt))),
            preview.Length == 0 ? TextBlock("Click Export to serialize the rows.") : Card(TextBlock(preview)).Padding(8));
        return TvSample.Page("DataExport", "Pick a format and click Export to serialize the rows (CSV / TSV / JSON), honoring column order.", table, options);
    }

    static string Export(int fmt)
    {
        var cols = TextColumns();
        string Cell(Person p, TableColumn c) => c.PropertyPath switch
        {
            nameof(Person.FirstName) => p.FirstName,
            nameof(Person.Department) => p.Department,
            nameof(Person.Status) => p.Status,
            nameof(Person.Salary) => p.Salary.ToString(CultureInfo.InvariantCulture),
            nameof(Person.JoinDateText) => p.JoinDateText,
            nameof(Person.Role) => p.Role,
            nameof(Person.Email) => p.Email,
            _ => "",
        };
        var rows = People.Take(6).ToList();
        if (fmt == 2)
        {
            var sb = new StringBuilder("[\n");
            foreach (var p in rows)
                sb.Append("  { ").Append(string.Join(", ", cols.Select(c => $"\"{c.Header}\": \"{Cell(p, c)}\""))).Append(" },\n");
            sb.Append(']');
            return sb.ToString();
        }
        char sep = fmt == 1 ? '\t' : ',';
        var lines = new List<string> { string.Join(sep, cols.Select(c => c.Header)) };
        lines.AddRange(rows.Select(p => string.Join(sep, cols.Select(c => Cell(p, c)))));
        return string.Join("\n", lines) + "\n… (first 6 rows)";
    }
}

class TvPerformancePage : Component
{
    public override Element Render()
    {
        var (result, setResult) = UseState("Click a Run button to measure.");
        var table = TableView(People, VibrantColumns(), height: 300) with { CanSortColumns = true, FrozenColumnCount = 1 };

        string TimeSort()
        {
            var data = ManyPeople(50000);
            var sw = Stopwatch.StartNew();
            var sorted = data.OrderByDescending(p => p.Salary).ThenBy(p => p.Department).ToList();
            sw.Stop();
            return $"Sort 50,000 × 7: {sw.Elapsed.TotalMilliseconds:N1} ms ({sorted.Count:N0} rows)";
        }
        string TimeFilter()
        {
            var data = ManyPeople(50000);
            var sw = Stopwatch.StartNew();
            var filtered = data.Where(p => p.IsActive && p.Salary > 120000).ToList();
            sw.Stop();
            return $"Filter 50,000: {sw.Elapsed.TotalMilliseconds:N1} ms ({filtered.Count:N0} matched)";
        }

        var options = VStack(12,
            SubHeading("Workloads"),
            HStack(8, Button("Run sort", () => setResult(TimeSort())), Button("Run filter", () => setResult(TimeFilter()))),
            Card(TextBlock(result)).Padding(8));
        return TvSample.Page("Performance", "Click Run to time a sort / filter over a 50,000-row dataset; the readout shows elapsed ms.", table, options);
    }
}

