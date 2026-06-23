using System;
using System.Collections;
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
using TVSel = Microsoft.UI.Xaml.Controls.TableViewSelectionMode;

namespace Reactor.TestApp.TableViewGallery;

class TvConditionalStylingPage : Component
{
    static readonly string[] Selectors =
    {
        "None — implicit theme style",
        "By department — category tints",
        "By salary tier — top earners green, below 60k amber",
        "Highlight inactive — critical-fill background",
    };

    public override Element Render()
    {
        var (selector, setSelector) = UseState(0);

        var setters = selector switch
        {
            1 => new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x1F, 0x00, 0x78, 0xD4) },
            2 => new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x1F, 0x16, 0xA3, 0x4A) },
            3 => new Action<WinTV>[] { tv => tv.AlternatingRowBackground = TvFx.Brush(0x1F, 0xDC, 0x26, 0x26) },
            _ => new Action<WinTV>[] { tv => tv.AlternatingRowBackground = null! },
        };

        var table = TableView(People, selector == 0 ? TextColumns() : VibrantColumns(), height: 460) with
        {
            SelectionMode = TVSel.Single,
            Setters = setters,
        };

        var options = VStack(16,
            TvSample.Section("Row style selector", "Pick a selector. The native sample swaps RowStyleSelector; this consumable page swaps supported row banding and vibrant column visuals.",
                RadioButtons(Selectors, selector, setSelector)),
            TvSample.Section("Live readouts", null,
                TvSample.Readout("Active selector", selector == 0 ? "(none) — implicit theme style" : Selectors[selector]),
                TvSample.Readout("Selector invocations", "native-only")),
            TvSample.NativeNote("RowStyleSelector itself is a native TableView surface not exposed by the Reactor wrapper yet; this page keeps the same option surface and applies the closest supported styling knobs."));

        return TvSample.Page("ConditionalStyling",
            "Pick a rule to re-tint rows by department, salary tier, or active state. Rows pick up the style as they scroll into view; selection still highlights on top.",
            table, options);
    }
}

class TvCellStylingPage : Component
{
    static readonly string[] Configs =
    {
        "None — no cell styles",
        "Salary tier — Salary column only",
        "Department + Active — category and status tints",
        "All three columns — Salary + Department + Active",
        "Vibrant sales dashboard — saturated pills, Salary tint, status chip",
    };

    public override Element Render()
    {
        var (config, setConfig) = UseState(4);
        var (live, setLive) = UseState(true);
        var (interval, setInterval) = UseState(500.0);

        var table = TableView(People, config == 0 ? TextColumns() : VibrantColumns(), height: 460) with
        {
            SelectionMode = TVSel.Single,
        };

        var options = VStack(16,
            TvSample.Section("Configuration", "Pick a styling rule. The vibrant preset uses the consumable control's pill, chip, and tint cell visuals.",
                RadioButtons(Configs, config, setConfig)),
            TvSample.Section("Status", null,
                TvSample.Readout("Active config", Configs[config])),
            TvSample.Section("Live updates", "Keep the same live-update controls as the native sample. A real timer is native binding behavior; this Reactor sample records the settings.",
                ToggleSwitch(live, setLive, onContent: "On", offContent: "Off", header: "Live updates"),
                TvSample.Group("Update interval (ms)", Slider(interval, 25, 2000, setInterval)),
                TvSample.Readout("Update interval (ms)", ((int)interval).ToString(CultureInfo.InvariantCulture)),
                TvSample.NativeNote("Native live tint updates rely on data-bound cell converters and property-change notifications. The consumable wrapper exposes static cell visual presets, so no timer is started here.")));

        return TvSample.Page("CellStyling",
            "Pick a styling rule to tint specific columns; only the targeted columns change. Selecting a row still highlights the whole row on top.",
            table, options);
    }
}

class TvAdvancedFilterPage : Component
{
    WinTV? _tv;

    public override Element Render()
    {
        var (filteredFires, setFilteredFires) = UseState(0);
        var (lastOpening, _) = UseState("(native flyout event not exposed)");
        var (activeFilters, setActiveFilters) = UseState("(none)");
        var (visibleRows, setVisibleRows) = UseState($"{People.Count} / {People.Count}");

        void RefreshFilters()
        {
            if (_tv == null) return;
            var active = _tv.FilteredColumns.Select(c => Convert.ToString(c.Header, CultureInfo.InvariantCulture) ?? "(column)").ToList();
            setActiveFilters(active.Count == 0 ? "(none)" : string.Join(", ", active));
            setVisibleRows($"{(_tv.ItemsSource as ICollection)?.Count ?? People.Count} / {People.Count}");
        }

        var table = TableView(People, TextColumns(), height: 460) with
        {
            SelectionMode = TVSel.Single,
            CanFilterColumns = true,
            CanSortColumns = true,
            OnControlReady = tv =>
            {
                _tv = tv;
                tv.Filtered += (_, __) =>
                {
                    setFilteredFires(filteredFires + 1);
                    RefreshFilters();
                };
            },
        };

        var options = VStack(16,
            TvSample.Section("Filter actions", "Clear every active column filter in one batch.",
                Button("Clear all filters", () =>
                {
                    if (_tv != null)
                    {
                        foreach (var c in _tv.FilteredColumns.ToList()) c.Filter = null!;
                        RefreshFilters();
                    }
                })),
            TvSample.Section("Live readouts", null,
                TvSample.Readout("FilterFlyoutOpening", "native-only"),
                TvSample.Readout("Last opening column", lastOpening),
                TvSample.Readout("Filtered fires", filteredFires.ToString(CultureInfo.InvariantCulture)),
                TvSample.Readout("Active filters", activeFilters),
                TvSample.Readout("Visible rows", visibleRows)));

        return TvSample.Page("AdvancedFilter",
            "Click the funnel on each column header to build filters. Click Clear all filters to reset.",
            table, options);
    }
}

class TvClipboardPage : Component
{
    WinTV? _tv;

    public override Element Render()
    {
        var (canCopy, setCanCopy) = UseState(true);
        var (canPaste, setCanPaste) = UseState(true);
        var (canCut, setCanCut) = UseState(true);
        var (lastAction, setLastAction) = UseState("(none)");
        var (clipboardPreview, setClipboardPreview) = UseState("(empty)");

        void CaptureSelection(string action)
        {
            var selected = _tv?.SelectedItems?.Cast<Person>().ToList() ?? new List<Person>();
            setLastAction($"{action} requested · {selected.Count} selected row(s)");
            setClipboardPreview(selected.Count == 0 ? "(select rows first)" : TvPowerHelpers.ToDelimited(selected.Take(10), "\t"));
        }

        var table = TableView(People, TextColumns(), height: 460) with
        {
            SelectionMode = TVSel.Extended,
            OnControlReady = tv => _tv = tv,
        };

        var options = VStack(16,
            TvSample.Section("Clipboard controls", null,
                ToggleSwitch(canCopy, setCanCopy, onContent: "CanUserCopy", offContent: "CanUserCopy off", header: "CanUserCopy"),
                ToggleSwitch(canPaste, setCanPaste, onContent: "CanUserPaste", offContent: "CanUserPaste off", header: "CanUserPaste"),
                ToggleSwitch(canCut, setCanCut, onContent: "CanUserCut", offContent: "CanUserCut off", header: "CanUserCut"),
                Button("Copy (Ctrl+C)", () => CaptureSelection(canCopy ? "Copy" : "Copy disabled in options")),
                Button("Cut (Ctrl+X)", () => CaptureSelection(canCut ? "Cut" : "Cut disabled in options")),
                Button("Paste (Ctrl+V)", () => setLastAction(canPaste ? "Paste requested" : "Paste disabled in options")),
                Button("Fill down (Ctrl+D)", () => setLastAction("Fill down requested"))),
            TvSample.Section("Status", null,
                TvSample.Readout("Last action", lastAction),
                TvSample.Readout("Copying fires", "native-only"),
                TvSample.Readout("Cutting fires", "native-only"),
                TvSample.Readout("Pasting fires", "native-only"),
                TvSample.Readout("Last clipboard text", clipboardPreview)),
            TvSample.NativeNote("The generated projection does not expose programmatic Copy/Cut/Paste methods or CanUserCopy/Paste/Cut properties through the Reactor wrapper. Use the native Ctrl+C / Ctrl+X / Ctrl+V / Ctrl+D gestures in the table."));

        return TvSample.Page("Clipboard",
            "Click a row and press Ctrl+C to copy it as TSV. Ctrl+V pastes, Ctrl+D fills down. The switches mirror the native sample's option surface.",
            table, options);
    }
}

class TvLayoutPage : Component
{
    WinTV? _tv;

    public override Element Render()
    {
        var (token, setToken) = UseState("");
        var (lastAction, setLastAction) = UseState("(none)");
        var (frozen, setFrozen) = UseState(false);
        var (sortState, setSortState) = UseState("(none)");

        string CurrentColumnToken() => _tv?.Columns == null
            ? "(table not ready)"
            : string.Join(",", _tv.Columns.Select(c => Convert.ToString(c.Header, CultureInfo.InvariantCulture) ?? "(column)"));

        var table = TableView(People, TextColumns(), height: 460) with
        {
            SelectionMode = TVSel.Single,
            CanReorderColumns = true,
            CanResizeColumns = true,
            CanSortColumns = true,
            FrozenColumnCount = frozen ? 1 : 0,
            OnControlReady = tv =>
            {
                _tv = tv;
                tv.Sorted += (_, __) =>
                {
                    var sorted = tv.SortedColumns.OrderBy(c => c.SortIndex).Select(c => $"{c.SortIndex}. {c.Header} {c.SortDirection}");
                    setSortState(sorted.Any() ? string.Join("  ·  ", sorted) : "(none)");
                };
            },
        };

        var options = VStack(16,
            TvSample.Section("Layout actions", "Change the layout, save a simple column-order token, then load or reset it.",
                Button("Save layout", () => { var t = CurrentColumnToken(); setToken(t); setLastAction("Saved current column order"); }),
                Button("Load layout", () => setLastAction(string.IsNullOrWhiteSpace(token) ? "No saved token to load" : $"Loaded token ({token.Length} chars)")),
                Button("Reset to defaults", () => { setFrozen(false); setToken(""); setLastAction("Reset to default columns"); }),
                Button(frozen ? "Unfreeze leading column" : "Freeze leading column", () => { setFrozen(!frozen); setLastAction(!frozen ? "Frozen leading column" : "Unfroze leading column"); })),
            TvSample.Section("Saved token", "The saved token appears here. Edit it before clicking Load to experiment with the schema.",
                TextBox(token, setToken, placeholderText: "Saved layout token", header: "Token")),
            TvSample.Section("Status", null,
                TvSample.Readout("Last action", lastAction),
                TvSample.Readout("Token length", $"{token.Length} chars"),
                TvSample.Readout("Sort priority", sortState)),
            TvSample.NativeNote("The real TableView layout serializer/restore API is native-only here. This page captures the observable column order as a simple token and drives the exposed FrozenColumnCount property."));

        return TvSample.Page("Layout",
            "Reorder columns and sort, then click Save layout to capture the state as a token. Reset to defaults, then Load layout to restore it.",
            table, options);
    }
}

class TvRtlPage : Component
{
    WinTV? _tv;

    public override Element Render()
    {
        var (rtl, setRtl) = UseState(true);
        var (selectedCount, setSelectedCount) = UseState(0);
        var (lastReorder, setLastReorder) = UseState("(none)");

        var table = TableView(People, TextColumns(), height: 460) with
        {
            SelectionMode = TVSel.Extended,
            CanReorderColumns = true,
            CanResizeColumns = true,
            FrozenColumnCount = 1,
            OnControlReady = tv => _tv = tv,
            OnSelectionChanged = _ => setSelectedCount(_tv?.SelectedItems?.Count ?? 0),
            Setters = new Action<WinTV>[]
            {
                tv => tv.FlowDirection = rtl ? Microsoft.UI.Xaml.FlowDirection.RightToLeft : Microsoft.UI.Xaml.FlowDirection.LeftToRight,
            },
        };

        var options = VStack(16,
            TvSample.Section("Flow direction", "Toggle FlowDirection on the TableView below to flip between left-to-right and right-to-left.",
                ToggleSwitch(rtl, setRtl, onContent: "RightToLeft", offContent: "LeftToRight", header: "FlowDirection"),
                Button("Reset column order", () => setLastReorder("Reset requested; declarative columns remain in default order"))),
            TvSample.Section("Status", null,
                TvSample.Readout("FlowDirection", rtl ? "RightToLeft" : "LeftToRight"),
                TvSample.Readout("Rows loaded", People.Count.ToString(CultureInfo.InvariantCulture)),
                TvSample.Readout("Selected count", selectedCount.ToString(CultureInfo.InvariantCulture)),
                TvSample.Readout("Last reorder", lastReorder)));

        return TvSample.Page("RTLPlayground",
            "Toggle FlowDirection to flip the page right-to-left. Columns, resize grippers, and frozen edges all mirror.",
            table, options);
    }
}

class TvVirtualizationPage : Component
{
    static readonly string[] Modes = { "In-memory", "Incremental (1M virtual)" };
    static readonly string[] Sizes = { "100", "1,000", "10,000", "50,000" };
    static readonly int[] SizeValues = { 100, 1000, 10000, 50000 };

    WinTV? _tv;

    public override Element Render()
    {
        var (mode, setMode) = UseState(0);
        var (size, setSize) = UseState(2);
        var (scroll, setScroll) = UseState("Home");

        var total = mode == 0 ? SizeValues[size] : Math.Min(SizeValues[size], 1000);
        var rows = ManyPeople(total);
        var realized = Math.Min(50, total);
        var ratio = total == 0 ? "0%" : $"~{(realized * 100.0 / total):0.##}%";

        var table = TableView(rows, TextColumns(), height: 460) with
        {
            SelectionMode = TVSel.Single,
            OnControlReady = tv => _tv = tv,
        };

        var options = VStack(16,
            TvSample.Section("Source and viewport", "Pick a source mode and dataset size, then watch the realized-row count stay tiny relative to the data count.",
                ComboBox(Modes, mode, setMode),
                ComboBox(Sizes, size, setSize)),
            TvSample.Section("Scroll actions", null,
                Button("Scroll Home", () => { _tv?.Select(0); setScroll("Home"); }),
                Button("Scroll Middle", () => { _tv?.Select(total / 2); setScroll("Middle"); }),
                Button("Scroll End", () => { _tv?.Select(Math.Max(0, total - 1)); setScroll("End"); })),
            TvSample.Section("Status", null,
                TvSample.Readout("Source mode", Modes[mode]),
                TvSample.Readout("Total rows", total.ToString("N0", CultureInfo.InvariantCulture)),
                TvSample.Readout("Realized rows", $"~{realized} (viewport)"),
                TvSample.Readout("Realized ratio", ratio),
                TvSample.Readout("Body offset", scroll)),
            TvSample.NativeNote("The consumable wrapper does not expose the TableView's internal realized-row count or ScrollViewer. The readout reports a conservative viewport estimate and the buttons select representative rows."));

        return TvSample.Page("Virtualization",
            "Switch the source mode and dataset size. Scroll fast — memory stays flat because only the viewport is realized.",
            table, options);
    }
}

class TvPaginationPage : Component
{
    static readonly string[] PageSizeLabels = { "25", "50", "100" };
    static readonly int[] PageSizes = { 25, 50, 100 };
    static readonly IReadOnlyList<Person> AllRows = ManyPeople(1000);

    public override Element Render()
    {
        var (page, setPage) = UseState(0);
        var (pageSizeIndex, setPageSizeIndex) = UseState(1);
        var (pageText, setPageText) = UseState("1");

        var pageSize = PageSizes[pageSizeIndex];
        var pageCount = (int)Math.Ceiling(AllRows.Count / (double)pageSize);
        var currentPage = Math.Min(page, pageCount - 1);
        var start = currentPage * pageSize;
        var rows = AllRows.Skip(start).Take(pageSize).ToList();
        var from = start + 1;
        var to = start + rows.Count;

        void Go(int p)
        {
            var clamped = Math.Max(0, Math.Min(pageCount - 1, p));
            setPage(clamped);
            setPageText((clamped + 1).ToString(CultureInfo.InvariantCulture));
        }

        void ApplyPageText(string text)
        {
            setPageText(text);
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var oneBased)) Go(oneBased - 1);
        }

        var table = TableView(rows, TextColumns(), height: 460) with
        {
            SelectionMode = TVSel.Single,
        };

        var options = VStack(16,
            TvSample.Section("Page controls", "Pick a page size, jump to a page, or step through the current page window.",
                ComboBox(PageSizeLabels, pageSizeIndex, i => { setPageSizeIndex(i); Go(0); }),
                HStack(8, Button("« First", () => Go(0)), Button("‹ Prev", () => Go(currentPage - 1))),
                HStack(8, TextBlock("Page"), TextBox(pageText, ApplyPageText, placeholderText: "1").Width(56), TextBlock($"of {pageCount}")),
                HStack(8, Button("Next ›", () => Go(currentPage + 1)), Button("Last »", () => Go(pageCount - 1)))),
            TvSample.Section("Status", null,
                TvSample.Readout("Range", $"Rows {from:N0}–{to:N0} of {AllRows.Count:N0}"),
                TvSample.Readout("Page", $"{currentPage + 1:N0} of {pageCount:N0}")),
            TvSample.NativeNote("The TableView only receives the current page window; selection clears per page by design. Persist cross-page selection in app state keyed by row identity."));

        return TvSample.Page("Pagination",
            "Use First / Prev / Next / Last or type a page number. Change the page size to rebuild the current page.",
            table, options);
    }
}

class TvDataExportPage : Component
{
    static readonly string[] Formats = { "CSV", "TSV", "JSON" };

    public override Element Render()
    {
        var (format, setFormat) = UseState(0);
        var (preview, setPreview) = UseState("");
        var (status, setStatus) = UseState("No export yet. Choose a format and click Export.");

        var rows = People.Take(10).ToList();

        void Export()
        {
            var text = format switch
            {
                1 => TvPowerHelpers.ToDelimited(rows, "\t"),
                2 => TvPowerHelpers.ToJson(rows),
                _ => TvPowerHelpers.ToDelimited(rows, ","),
            };
            setPreview(text);
            setStatus($"Exported {rows.Count} rows as {Formats[format]}");
        }

        var table = TableView(People, TextColumns(), height: 460) with
        {
            SelectionMode = TVSel.Single,
            CanReorderColumns = true,
        };

        var options = VStack(16,
            TvSample.Section("Export options", "Choose a format, export the visible rows, then inspect the generated text.",
                RadioButtons(Formats, format, setFormat),
                Button("Export", Export)),
            TvSample.Section("Status", null,
                TvSample.Readout("Result", status)),
            TvSample.Section("Preview", null,
                TextBox(preview, setPreview, placeholderText: "Preview of the exported text appears here.", header: "Export preview")),
            TvSample.NativeNote("The native sample can call TableView.GetDataAsText to honor the live column order. The consumable wrapper does not expose that formatter, so this page serializes the sample rows from the shared column model."));

        return TvSample.Page("DataExport",
            "Pick a format, click Export, and check the preview.",
            table, options);
    }
}

class TvPerformancePage : Component
{
    public override Element Render()
    {
        var (rows, setRows) = UseState<IReadOnlyList<Person>>(ManyPeople(10000));
        var (last, setLast) = UseState("—");
        var (sortMs, setSortMs) = UseState("—");
        var (filterMs, setFilterMs) = UseState("—");

        void RunSort()
        {
            var source = ManyPeople(50000).OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ToList();
            var sw = Stopwatch.StartNew();
            setRows(source);
            sw.Stop();
            setSortMs($"{sw.Elapsed.TotalMilliseconds:0.##} ms · {source.Count:N0} rows");
            setLast("Run sort");
        }

        void RunFilter()
        {
            var source = ManyPeople(50000).Where(p => p.Salary >= 100000).ToList();
            var sw = Stopwatch.StartNew();
            setRows(source);
            sw.Stop();
            setFilterMs($"{sw.Elapsed.TotalMilliseconds:0.##} ms · {source.Count:N0} rows");
            setLast("Run filter");
        }

        var table = TableView(rows, TextColumns(), height: 460) with
        {
            SelectionMode = TVSel.Single,
        };

        var options = VStack(16,
            TvSample.Section("Performance actions", "Run repeatable sort and filter operations over 50,000 generated rows; elapsed time measures the MVU state update request.",
                Button("Run sort", RunSort),
                Button("Run filter", RunFilter)),
            TvSample.Section("Status", null,
                TvSample.Readout("Last action", last),
                TvSample.Readout("Sort elapsed", sortMs),
                TvSample.Readout("Filter elapsed", filterMs),
                TvSample.Readout("Rows displayed", rows.Count.ToString("N0", CultureInfo.InvariantCulture))));

        return TvSample.Page("Performance",
            "Click Run sort or Run filter to time common large-dataset updates. Run twice; the first can include JIT warmup.",
            table, options);
    }
}

static class TvPowerHelpers
{
    static readonly (string Header, Func<Person, object?> Value)[] ExportColumns =
    {
        ("First name", p => p.FirstName),
        ("Last name", p => p.LastName),
        ("Email", p => p.Email),
        ("Department", p => p.Department),
        ("Role", p => p.Role),
        ("Join date", p => p.JoinDateText),
        ("Salary", p => p.Salary),
        ("Active", p => p.IsActive),
    };

    public static string ToDelimited(IEnumerable<Person> rows, string delimiter)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(delimiter, ExportColumns.Select(c => EscapeDelimited(c.Header, delimiter))));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(delimiter, ExportColumns.Select(c => EscapeDelimited(Convert.ToString(c.Value(row), CultureInfo.InvariantCulture) ?? string.Empty, delimiter))));
        }
        return sb.ToString();
    }

    public static string ToJson(IEnumerable<Person> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[");
        var list = rows.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var row = list[i];
            sb.Append("  {");
            sb.Append(string.Join(", ", ExportColumns.Select(c => $"\"{EscapeJson(c.Header)}\": \"{EscapeJson(Convert.ToString(c.Value(row), CultureInfo.InvariantCulture) ?? string.Empty)}\"")));
            sb.Append(i == list.Count - 1 ? "}" : "},");
            sb.AppendLine();
        }
        sb.Append("]");
        return sb.ToString();
    }

    static string EscapeDelimited(string value, string delimiter)
    {
        if (value.Contains('"') || value.Contains('\r') || value.Contains('\n') || value.Contains(delimiter, StringComparison.Ordinal))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    static string EscapeJson(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}
