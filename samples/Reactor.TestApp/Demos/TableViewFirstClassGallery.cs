using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Reactor.Controls.Factories;
using static Reactor.TestApp.TableViewGallery.TableViewSampleData;
using TVGrid = Microsoft.UI.Xaml.Controls.TableViewGridLinesVisibility;
using TVHeaders = Microsoft.UI.Xaml.Controls.TableViewHeadersVisibility;
using TVSel = Microsoft.UI.Xaml.Controls.TableViewSelectionMode;

// FIRST-CLASS Reactor TableView gallery, styled to mimic the native TableViewSamples gallery:
// a NavigationView shell with grouped sections, and per-page "SamplePresenter" chrome — title,
// description, a "Try it" callout, an interactive Options panel, and the table — all pure-C# Reactor
// (MVU) consuming the consumable TableView(items, columns) control (no XamlHostElement interop).
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
            NavItem("Selection", "SelectAll", "Selection"),
            NavItem("Sort + filter", "Sort", "Sort"),
            NavItemHeader("Columns"),
            NavItem("Frozen columns", "Pin", "Frozen"),
            NavItem("Headers", "ViewAll", "Headers"),
            NavItemHeader("Rows & cells"),
            NavItem("Grid lines", "List", "GridLines"),
            NavItemHeader("About"),
            NavItem("About", "Help", "About"),
        };

        return NavigationView(menu, content: Content(tag)) with
        {
            SelectedTag = tag,
            OnSelectedTagChanged = t => { if (t != null) setTag(t); },
            PaneTitle = "TableView",
            IsSettingsVisible = false,
        };
    }

    static Element Content(string tag) => tag switch
    {
        "Home" => Component<TvHomePage>(),
        "Showcase" => Component<TvShowcasePage>(),
        "Selection" => Component<TvSelectionPage>(),
        "Sort" => Component<TvSortPage>(),
        "Frozen" => Component<TvFrozenPage>(),
        "Headers" => Component<TvHeadersPage>(),
        "GridLines" => Component<TvGridLinesPage>(),
        "About" => Component<TvAboutPage>(),
        _ => Component<TvShowcasePage>(),
    };
}

// ── Shared SamplePresenter-style page chrome ─────────────────────────────────────────────────────
static class TvPage
{
    public static Element Render(string title, string description, string tryIt, Element table, Element? options = null) =>
        ScrollView(VStack(16,
            Heading(title),
            TextBlock(description),
            InfoBar("Try it", tryIt),
            options is null
                ? table
                : HStack(16, table.Flex(grow: 1), Card(VStack(10, options)).Width(300).VAlign(Microsoft.UI.Xaml.VerticalAlignment.Top))
        )).Padding(4);

    public static Element OptionGroup(string header, Element control) =>
        VStack(6, SubHeading(header), control);
}

// ── Pages ────────────────────────────────────────────────────────────────────────────────────────
class TvHomePage : Component
{
    public override Element Render() =>
        ScrollView(VStack(14,
            Heading("TableView"),
            TextBlock(
                "A native WinUI 3 tabular control for data-heavy desktop experiences — typed columns, " +
                "multi-column sort and filter, frozen columns, grid lines, selection, and colored cell " +
                "visuals. This gallery is the first-class Reactor (MVU) rebuild: every page consumes the " +
                "consumable TableView(items, columns) control directly in pure C# — no XamlHostElement interop."),
            Card(VStack(8,
                SubHeading("What's shown"),
                TextBlock("\u2022 Showcase — colored Department pills, Status chips, stoplight Salary tints (template columns)"),
                TextBlock("\u2022 Selection — row selection modes + the leading selection gutter"),
                TextBlock("\u2022 Sort + filter — sortable / filterable columns via header funnels"),
                TextBlock("\u2022 Frozen columns — pin leading columns during horizontal scroll"),
                TextBlock("\u2022 Headers + Grid lines — visibility toggles, applied live")
            )).Padding(16)
        )).Padding(4);
}

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
            TvPage.OptionGroup("Vibrant cells",
                ToggleSwitch(vibrant, v => setVibrant(v), onContent: "Pills / chips / tints", offContent: "Plain text")),
            TvPage.OptionGroup("Grid lines",
                ToggleSwitch(grid, v => setGrid(v), onContent: "Horizontal", offContent: "None")));

        return TvPage.Render(
            "Showcase",
            "A people-directory dashboard composing the full feature set: colored Department pills, " +
            "Active / Inactive Status chips, and stoplight Salary tints (template columns), a frozen first " +
            "column, and sortable + resizable columns.",
            "Toggle Vibrant cells to switch the Department / Status / Salary columns between colored template " +
            "cells and plain text. Click a header to sort, drag a header edge to resize.",
            table, options);
    }
}

class TvSelectionPage : Component
{
    static readonly string[] Modes = { "None", "Single", "Multiple", "Extended" };

    public override Element Render()
    {
        var (mode, setMode) = UseState(2); // Multiple
        var (gutter, setGutter) = UseState(true);

        var sel = mode switch { 0 => TVSel.None, 1 => TVSel.Single, 3 => TVSel.Extended, _ => TVSel.Multiple };
        var table = TableView(People, TextColumns(), height: 460) with
        {
            SelectionMode = sel,
            IsSelectionGutterVisible = gutter,
        };

        var options = VStack(14,
            TvPage.OptionGroup("Selection mode", RadioButtons(Modes, mode, setMode)),
            TvPage.OptionGroup("Selection gutter",
                ToggleSwitch(gutter, v => setGutter(v), onContent: "Visible", offContent: "Hidden")));

        return TvPage.Render(
            "Selection",
            "Choose how rows are selected — none, single, multiple, or extended (Shift / Ctrl) — and show or " +
            "hide the leading selection gutter (the checkbox column).",
            "Pick a selection mode, then click rows (and the header checkbox) to select. Toggle the gutter " +
            "to show / hide the leading checkbox column.",
            table, options);
    }
}

class TvSortPage : Component
{
    public override Element Render()
    {
        var (sort, setSort) = UseState(true);
        var (filter, setFilter) = UseState(true);

        var table = TableView(People, TextColumns(), height: 460) with
        {
            CanSortColumns = sort,
            CanFilterColumns = filter,
            CanResizeColumns = true,
        };

        var options = VStack(14,
            TvPage.OptionGroup("Sorting",
                ToggleSwitch(sort, v => setSort(v), onContent: "Click headers to sort", offContent: "Off")),
            TvPage.OptionGroup("Filtering",
                ToggleSwitch(filter, v => setFilter(v), onContent: "Header funnels", offContent: "Off")));

        return TvPage.Render(
            "Sort + filter",
            "Sortable and filterable columns. Clicking a header cycles ascending / descending / none; the " +
            "header funnel opens a per-column filter.",
            "Click a column header to sort by it. Click the funnel icon to filter that column. Toggle either " +
            "capability off in the options.",
            table, options);
    }
}

class TvFrozenPage : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(2);

        var table = TableView(People, TextColumns(), height: 460) with
        {
            FrozenColumnCount = count,
            CanResizeColumns = true,
        };

        var options = VStack(10,
            SubHeading($"Frozen columns: {count}"),
            Slider((double)count, 0, 4, v => setCount((int)Math.Round(v))),
            TextBlock("The first N columns stay pinned to the leading edge during horizontal scroll."));

        return TvPage.Render(
            "Frozen columns",
            "Pin the leading columns so they stay visible while the rest of the table scrolls horizontally.",
            "Set the frozen count, then scroll the table horizontally — the first N columns stay put.",
            table, options);
    }
}

class TvHeadersPage : Component
{
    static readonly string[] Vis = { "None", "Column", "Row", "All" };

    public override Element Render()
    {
        var (idx, setIdx) = UseState(1); // Column

        var hv = idx switch { 0 => TVHeaders.None, 2 => TVHeaders.Row, 3 => TVHeaders.All, _ => TVHeaders.Column };
        var table = TableView(People, TextColumns(), height: 460) with
        {
            HeadersVisibility = hv,
            GridLinesVisibility = TVGrid.Horizontal,
        };

        var options = TvPage.OptionGroup("Headers visibility", RadioButtons(Vis, idx, setIdx));

        return TvPage.Render(
            "Headers",
            "Control which headers are shown — column headers, row headers, both, or none.",
            "Switch the headers visibility and watch the column-header band appear / disappear live.",
            table, options);
    }
}

class TvGridLinesPage : Component
{
    static readonly string[] Vis = { "None", "Horizontal", "Vertical", "All" };

    public override Element Render()
    {
        var (idx, setIdx) = UseState(3); // All

        var gl = idx switch { 0 => TVGrid.None, 1 => TVGrid.Horizontal, 2 => TVGrid.Vertical, _ => TVGrid.All };
        var table = TableView(People, TextColumns(), height: 460) with { GridLinesVisibility = gl };

        var options = TvPage.OptionGroup("Grid lines", RadioButtons(Vis, idx, setIdx));

        return TvPage.Render(
            "Grid lines",
            "Show horizontal grid lines, vertical grid lines, both, or none.",
            "Pick a grid-lines option and watch the table redraw live.",
            table, options);
    }
}

class TvAboutPage : Component
{
    public override Element Render() =>
        ScrollView(VStack(14,
            Heading("About"),
            TextBlock(
                "This first-class Reactor TableView gallery is built entirely in C# with Microsoft.UI.Reactor " +
                "(an MVU framework over WinUI 3). Each page is a Reactor component that consumes the consumable " +
                "Reactor.Controls.TableView control via TableView(items, columns) and reconfigures it reactively " +
                "from its Options panel — no XAML files, no XamlHostElement interop."),
            Card(VStack(8,
                SubHeading("Native control"),
                TextBlock("Microsoft.UI.Xaml.Controls.TableView — a separate-binary split control " +
                          "(Microsoft.UI.Xaml.Controls.Advanced.dll), projected via CsWinRT vs public WinAppSDK 2.0.1.")
            )).Padding(16)
        )).Padding(4);
}
