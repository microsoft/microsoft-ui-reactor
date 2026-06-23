using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

// FIRST-CLASS Reactor TableView gallery, built to mirror the real TableViewSamples reference gallery:
// the same NavigationView shell, the same grouped sections + page set (Quick start / Columns /
// Rows & cells / Styling / Power user + Performance / About), and the same per-page SamplePresenter
// chrome (heading + description + a "Try it" InfoBar + an interactive Options panel + the live table) —
// all pure-C# Reactor (MVU) consuming the consumable TableView(items, columns) control. No XamlHostElement.
class TableViewFirstClassGallery : Component
{
    public override Element Render()
    {
        var (tag, setTag) = UseState("Showcase");

        var menu = new[]
        {
            NavItem("Home", "Home", "Home"),

            NavItemHeader("Quick start"),
            NavItem("Showcase", "ViewAll", "Showcase"),
            NavItem("Selection", "\uE73A", "Selection"),
            NavItem("Cell selection", "\uF0E2", "CellSelection"),
            NavItem("Multi-column sort", "\uE8CB", "Sort"),
            NavItem("Per-column filtering", "\uE16E", "Filter"),
            NavItem("Inline editing", "\uE70F", "InlineEdit"),
            NavItem("Keyboard nav + a11y", "\uE765", "KeyboardNav"),

            NavItemHeader("Columns"),
            NavItem("Column resize", "\uE740", "ColumnResize"),
            NavItem("Column reorder + autosize", "\uE8AB", "ColumnReorder"),
            NavItem("Column drag-reorder gesture", "\uE8B0", "ColumnReorderGesture"),
            NavItem("Dynamic columns", "\uE8B7", "DynamicColumns"),
            NavItem("Sticky headers", "\uE73E", "StickyHeaders"),
            NavItem("Headers visibility", "\uE8A7", "HeadersVisibility"),
            NavItem("Frozen columns", "\uE840", "FrozenColumns"),
            NavItem("Frozen trailing columns", "\uE840", "FrozenTrailingColumns"),

            NavItemHeader("Rows & cells"),
            NavItem("Row drag-and-drop reorder", "\uE8A6", "RowReorder"),
            NavItem("Grouped rows", "\uE8FD", "Groups"),
            NavItem("N-level hierarchy", "\uE8A2", "Hierarchy"),
            NavItem("Row colors (banding)", "Highlight", "RowColors"),
            NavItem("Grid lines visibility", "\uF0E2", "GridLines"),
            NavItem("Custom row templates", "\uE8B5", "RowTemplate"),
            NavItem("Row details template", "\uE7C3", "RowDetails"),
            NavItem("Mixed controls in cells", "\uE950", "MixedControls"),
            NavItem("Marquee selection", "\uE8FF", "Marquee"),

            NavItemHeader("Styling"),
            NavItem("Conditional row styling", "\uE790", "ConditionalStyling"),
            NavItem("Per-cell conditional styling", "\uE7C8", "CellStyling"),

            NavItemHeader("Power user"),
            NavItem("Advanced filter UI", "\uE71C", "AdvancedFilter"),
            NavItem("Clipboard / Excel-style", "\uE8C8", "Clipboard"),
            NavItem("Persisted layout state", "\uE74E", "Layout"),
            NavItem("Right-to-left layout", "\uE7AF", "RTLPlayground"),
            NavItem("Virtualization", "\uE8C6", "Virtualization"),
            NavItem("Pagination", "\uE8FD", "Pagination"),
            NavItem("Data export", "\uE74E", "DataExport"),

            NavItemHeader("More"),
            NavItem("Performance", "Clock", "Performance"),
            NavItem("About", "Help", "About"),
        };

        return NavigationView(menu, content: Route(tag)) with
        {
            SelectedTag = tag,
            OnSelectedTagChanged = t => { if (t != null) setTag(t); },
            PaneTitle = "TableView",
            IsSettingsVisible = false,
        };
    }

    static Element Route(string tag) => tag switch
    {
        "Home" => TvDisplayPages.Home(),
        "About" => TvDisplayPages.About(),
        "Showcase" => Component<TvShowcasePage>(),
        "Selection" => Component<TvSelectionPage>(),
        "CellSelection" => Component<TvCellSelectionPage>(),
        "Sort" => Component<TvSortPage>(),
        "Filter" => Component<TvFilterPage>(),
        "InlineEdit" => Component<TvInlineEditPage>(),
        "KeyboardNav" => Component<TvKeyboardNavPage>(),
        "ColumnResize" => Component<TvColumnResizePage>(),
        "ColumnReorder" => Component<TvColumnReorderPage>(),
        "ColumnReorderGesture" => Component<TvColumnReorderGesturePage>(),
        "DynamicColumns" => Component<TvDynamicColumnsPage>(),
        "StickyHeaders" => Component<TvStickyHeadersPage>(),
        "HeadersVisibility" => Component<TvHeadersPage>(),
        "FrozenColumns" => Component<TvFrozenLeadingPage>(),
        "FrozenTrailingColumns" => Component<TvFrozenTrailingPage>(),
        "RowReorder" => Component<TvRowReorderPage>(),
        "Groups" => Component<TvGroupsPage>(),
        "Hierarchy" => Component<TvHierarchyPage>(),
        "RowColors" => Component<TvRowColorsPage>(),
        "GridLines" => Component<TvGridLinesPage>(),
        "RowTemplate" => Component<TvRowTemplatePage>(),
        "RowDetails" => Component<TvRowDetailsPage>(),
        "MixedControls" => Component<TvMixedControlsPage>(),
        "Marquee" => Component<TvMarqueePage>(),
        "ConditionalStyling" => Component<TvConditionalStylingPage>(),
        "CellStyling" => Component<TvCellStylingPage>(),
        "AdvancedFilter" => Component<TvAdvancedFilterPage>(),
        "Clipboard" => Component<TvClipboardPage>(),
        "Layout" => Component<TvLayoutPage>(),
        "RTLPlayground" => Component<TvRtlPage>(),
        "Virtualization" => Component<TvVirtualizationPage>(),
        "Pagination" => Component<TvPaginationPage>(),
        "DataExport" => Component<TvDataExportPage>(),
        "Performance" => Component<TvPerformancePage>(),
        _ => TvDisplayPages.Home(),
    };
}

// ── Reference metadata (header + description verbatim from the TableViewSamples pages) ───────────────
static class TvMeta
{
    public static readonly Dictionary<string, (string Header, string Desc)> Pages = new()
    {
        ["Showcase"] = ("Showcase", "A rich people-directory dashboard composing the full TableView feature set: vibrant data-bound Department pills and stoplight Salary tints, status chips, plus sort, filter, resize, reorder, a frozen leading column, grid lines, and an optional selection gutter."),
        ["Selection"] = ("Selection", "Switch SelectionMode between None, Single, Multiple, and Extended, then watch the selection update as you select rows. Toggle the leading selection gutter (checkbox column) on or off."),
        ["CellSelection"] = ("Cell selection", "Switch SelectionUnit between Row, Cell, and CellOrRow. In Cell / CellOrRow, clicking selects an individual cell; Ctrl+click toggles a cell, and Shift+click selects a rectangular range."),
        ["Sort"] = ("Multi-column sort", "Click a header to sort; click again to reverse, and Ctrl-click another header to layer a secondary sort. The chevron shows direction and the badge shows priority order (1, 2, 3…) when 2+ columns are sorted."),
        ["Filter"] = ("Per-column filtering", "Per-column filtering. Open a column's header funnel and choose values to narrow the rows; the funnel marks which columns are filtered, and clearing it resets them."),
        ["InlineEdit"] = ("Inline editing", "Edit cell values in place: double-click or F2 opens the editor, Enter commits, Esc discards. Text columns are editable unless marked read-only."),
        ["KeyboardNav"] = ("Keyboard navigation + accessibility", "Arrow keys, Tab, Home / End, and Page Up / Page Down move focus across cells. The grid exposes its structure to UI Automation so Narrator can announce rows and the table's dimensions."),
        ["ColumnResize"] = ("Column resize", "Drag a column's right edge to resize it interactively, or toggle resizing on and off. Each column keeps its own width."),
        ["ColumnReorder"] = ("Column reorder + autosize (programmatic)", "Columns keep their own width as they move, so reordering never disturbs your sizing. Drag a header sideways to reorder, or enable reordering below."),
        ["ColumnReorderGesture"] = ("Column drag-reorder gesture", "Drag any column header sideways to reorder it; a drop indicator shows the target slot. CanUserReorderColumns toggles the gesture globally."),
        ["DynamicColumns"] = ("Dynamic columns", "Show or hide columns at runtime. Toggle a column below and it appears or disappears instantly while the rest keep their place and width."),
        ["StickyHeaders"] = ("Sticky headers", "The column headers stay pinned at the top of the table as you scroll down — the rows scroll under them. A wide employee roster makes the effect visible."),
        ["HeadersVisibility"] = ("Headers visibility", "Show or hide the table's column headers and the row selection gutter with HeadersVisibility. Switch between All, Column, Row, and None to see each surface appear and disappear."),
        ["FrozenColumns"] = ("Frozen leading columns", "Pin one or more columns to the leading edge: as you scroll horizontally, the leading-frozen columns stay visible while the middle columns scroll under them."),
        ["FrozenTrailingColumns"] = ("Frozen trailing columns", "Pin one or more columns to the trailing edge: as the user scrolls horizontally, the trailing-frozen columns stay visible — useful for keeping a 'Total' or 'Status' column always in view."),
        ["RowReorder"] = ("Row drag-and-drop reorder", "Drag any row by its cells to move it to a new position. CanUserReorderRows enables the gesture, and the RowReordered event reports the (from, to) index pair."),
        ["Groups"] = ("Grouped rows", "Grouped data shape, group headers, and bulk expand / collapse — here the directory is grouped by Department."),
        ["Hierarchy"] = ("N-level hierarchy", "Tree-grid mode: each row exposes its child rows through a named children property, and an expand chevron in the first column lets the user drill in. Supports arbitrary nesting depth."),
        ["RowColors"] = ("Row colors (banding)", "Opt in to per-row brushes: RowBackground / AlternatingRowBackground paint row backgrounds. Both default to null, so rows stay unbanded until you set them — set both to enable zebra striping."),
        ["GridLines"] = ("Grid lines visibility", "GridLinesVisibility — None / Horizontal / Vertical / All — controls per-row bottom borders and per-cell right borders for WPF DataGrid parity."),
        ["RowTemplate"] = ("Custom row templates", "Replace the default per-column cells with a single DataTemplate that renders the whole row — useful for card-style rosters or two-line compact lists. Selection, keyboard nav, and the header row keep working."),
        ["RowDetails"] = ("Row details template", "Per-row expansion area shown below the row body. RowDetailsTemplate sets the visual, RowDetailsVisibilityMode picks Collapsed / Visible / VisibleWhenSelected."),
        ["MixedControls"] = ("Mixed controls in cells", "Host non-text controls — DatePicker, ComboBox, CheckBox — directly inside cells via TableViewTemplateColumn. TwoWay bindings push the user's edits straight back into the row's data object."),
        ["Marquee"] = ("Drag-selection rectangle", "Press and drag in the table's empty space to draw a rectangular marquee. On release, every row the rectangle touches is selected, replacing the current selection."),
        ["ConditionalStyling"] = ("Conditional row styling", "Apply a different row Style based on the data — tint by department, by salary tier, or call out inactive employees. Rows re-tint as they scroll into view."),
        ["CellStyling"] = ("Per-cell conditional styling", "Tint individual cells based on the cell's value. The vibrant preset resembles a sales-dashboard look with saturated category pills, stoplight Salary, and a status chip on Active."),
        ["AdvancedFilter"] = ("Advanced filter UI", "The funnel button on each column header opens a flyout where users build typed predicates (Contains, Equals, IsEmpty, …) per column. Narrow the operator vocabulary per column or suppress the funnel entirely."),
        ["Clipboard"] = ("Clipboard / Excel-style operations", "Ctrl+C / Ctrl+X / Ctrl+V over cell ranges using Excel-compatible TSV (so paste into Excel just works), plus Ctrl+D fill-down."),
        ["Layout"] = ("Persisted layout state", "Capture the table's current sort, column order, and frozen-edge configuration into a single string token, then restore it later — handy for saving per-user view preferences across sessions."),
        ["RTLPlayground"] = ("Right-to-left layout", "Set FlowDirection on the TableView and the whole layout mirrors: columns flow right-to-left, the resize gripper moves to the visual-left edge of each header, and frozen-leading columns pin to the visual-right."),
        ["Virtualization"] = ("Row + data virtualization", "Bind tens of thousands of in-memory rows — only the visible viewport is realized, so memory and scroll responsiveness stay flat. Pick a dataset size below."),
        ["Pagination"] = ("Pagination", "Page through 1,000 records, 50 at a time. Demonstrates driving TableView.ItemsSource from a paged window over a large in-memory list."),
        ["DataExport"] = ("Data export", "Serialize the rows currently displayed to CSV, TSV (Excel-compatible), or JSON. The exporter walks the columns so it honors column order."),
        ["Performance"] = ("Performance", "Repeatable timing measurements for common TableView workloads. Click Run to sort / filter a large dataset; the readout shows elapsed wall-clock ms."),
        ["About"] = ("About", "This first-class Reactor TableView gallery mirrors the native TableViewSamples gallery — same NavigationView shell, sections, and pages — built entirely in C# with Microsoft.UI.Reactor (MVU over WinUI 3), consuming the consumable Reactor.Controls.TableView control. No XamlHostElement interop."),
    };

    public static (string Header, string Desc) Of(string tag) =>
        Pages.TryGetValue(tag, out var v) ? v : (tag, "");
}

// ── Shared SamplePresenter-style page chrome ─────────────────────────────────────────────────────
static class TvSample
{
    public static Element Page(string tag, string tryIt, Element table, Element? options = null, Element? extraInfo = null)
    {
        var (header, desc) = TvMeta.Of(tag);
        var head = VStack(16,
            Heading(header),
            TextBlock(desc),
            InfoBar("Try it", tryIt));
        if (extraInfo != null)
            head = VStack(16, head, extraInfo);

        Element body = options is null
            ? table
            : HStack(16, table.Flex(grow: 1), Card(VStack(12, options)).Width(300).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Top));

        return ScrollView(VStack(16, head, body)).Padding(4);
    }

    public static Element Group(string header, Element control) => VStack(6, SubHeading(header), control);

    /// <summary>A live readout row: a secondary label and a bold value (mirrors the reference Status panels).</summary>
    public static Element Readout(string label, string value) =>
        HStack(8, Caption(label).Flex(grow: 1), TextBlock(value));

    /// <summary>A titled options section: a SubHeading, an optional caption, then the controls.</summary>
    public static Element Section(string header, string? caption, params Element[] children)
    {
        var items = new List<Element> { SubHeading(header) };
        if (caption != null) items.Add(Caption(caption));
        items.AddRange(children);
        return VStack(8, items.ToArray());
    }

    // A note shown on pages whose behaviour is provided by the native control's built-in gestures
    // (so there is no extra Options panel to drive) or that need control surface not yet exposed
    // by the consumable wrapper.
    public static Element NativeNote(string message) =>
        InfoBar("Native control feature", message);
}

// ── Shared helpers used across the gallery page section files ────────────────────────────────────
static class TvFx
{
    public static Windows.UI.Color Argb(byte a, byte r, byte g, byte b) => new() { A = a, R = r, G = g, B = b };

    public static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(byte a, byte r, byte g, byte b) => new(Argb(a, r, g, b));

    /// <summary>Marks the last <paramref name="count"/> columns frozen to the trailing edge (post-ApplyColumns setter).</summary>
    public static Action<Microsoft.UI.Xaml.Controls.TableView> FreezeTrailing(int count) => tv =>
    {
        int n = tv.Columns.Count;
        for (int i = 0; i < n; i++)
            tv.Columns[i].FrozenEdge = (i >= n - count && count > 0)
                ? Microsoft.UI.Xaml.Controls.TableViewFrozenEdge.Trailing
                : Microsoft.UI.Xaml.Controls.TableViewFrozenEdge.None;
    };
}

// ── Home + About (no table / options) ─────────────────────────────────────────────────────────────
static class TvDisplayPages
{
    public static Element Home() =>
        ScrollView(VStack(16,
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
