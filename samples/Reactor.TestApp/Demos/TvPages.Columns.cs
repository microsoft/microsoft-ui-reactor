using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Reactor.Controls.Factories;
using static Reactor.TestApp.TableViewGallery.TableViewSampleData;
using GridLength = Microsoft.UI.Xaml.GridLength;
using Visibility = Microsoft.UI.Xaml.Visibility;
using TVFrozenEdge = Microsoft.UI.Xaml.Controls.TableViewFrozenEdge;
using TVHeaders = Microsoft.UI.Xaml.Controls.TableViewHeadersVisibility;
using TVSel = Microsoft.UI.Xaml.Controls.TableViewSelectionMode;
using TVColumn = Microsoft.UI.Xaml.Controls.TableViewColumn;
using WinTV = Microsoft.UI.Xaml.Controls.TableView;

namespace Reactor.TestApp.TableViewGallery;

class TvColumnResizePage : Component
{
    static readonly string[] ColumnNames = { "First name", "Last name", "Email", "Department", "Role (locked)" };
    WinTV? _tv;

    static List<TableColumn> Columns() => new()
    {
        new("First name", nameof(Person.FirstName), Width: 110),
        new("Last name", nameof(Person.LastName), Width: 110),
        new("Email", nameof(Person.Email), Width: 260),
        new("Department", nameof(Person.Department), Width: 140),
        new("Role (locked)", nameof(Person.Role), Width: 170),
    };

    public override Element Render()
    {
        var (selected, setSelected) = UseState(0);
        var (minWidth, setMinWidth) = UseState(20.0);
        var (width, setWidth) = UseState(160.0);
        var (maxWidth, setMaxWidth) = UseState(500.0);
        var (canResize, setCanResize) = UseState(true);
        var (widthReadout, setWidthReadout) = UseState("?");

        TVColumn? ColumnAt(int i) => _tv != null && i >= 0 && i < _tv.Columns.Count ? _tv.Columns[i] : null;
        TVColumn? ActiveColumn() => ColumnAt(selected);
        void RefreshReadout(TVColumn? column = null)
        {
            if ((column ?? ActiveColumn()) is { } c)
                setWidthReadout($"{c.Width.Value.ToString("0", CultureInfo.InvariantCulture)} / {c.ActualWidth.ToString("0", CultureInfo.InvariantCulture)}");
        }
        void ApplySizing(WinTV tv)
        {
            _tv = tv;
            foreach (var col in tv.Columns)
                if (Equals(col.Header, "Role (locked)"))
                    col.CanUserResize = false;
            if (tv.Columns.Count > selected)
            {
                var c = tv.Columns[selected];
                c.MinWidth = minWidth;
                c.Width = new GridLength(width);
                c.MaxWidth = maxWidth;
            }
        }

        var table = TableView(People, Columns()) with
        {
            SelectionMode = TVSel.Single,
            CanResizeColumns = canResize,
            OnControlReady = tv => { _tv = tv; ApplySizing(tv); RefreshReadout(); },
            Setters = new Action<WinTV>[] { ApplySizing },
        };

        var options = VStack(16,
            TvSample.Section("Column sizing", "Select a column and drive MinWidth, Width, and MaxWidth from code.",
                ComboBox(ColumnNames, selected, i =>
                {
                    setSelected(i);
                    if (ColumnAt(i) is { } c)
                    {
                        setMinWidth(Math.Min(c.MinWidth, 400));
                        setWidth(Math.Min(c.Width.Value, 500));
                        setMaxWidth(double.IsInfinity(c.MaxWidth) ? 500 : Math.Min(c.MaxWidth, 500));
                        RefreshReadout(c);
                    }
                }),
                ToggleSwitch(canResize, setCanResize, onContent: "Resizing allowed", offContent: "Resizing blocked", header: "Resizing"),
                TvSample.Group("MinWidth", VStack(4,
                    Slider(minWidth, 0, 400, v => { setMinWidth(v); if (ActiveColumn() is { } c) c.MinWidth = v; RefreshReadout(); }),
                    TvSample.Readout("Value", minWidth.ToString("0", CultureInfo.InvariantCulture)))),
                TvSample.Group("Width", VStack(4,
                    Slider(width, 0, 500, v => { setWidth(v); if (ActiveColumn() is { } c) c.Width = new GridLength(v); RefreshReadout(); }),
                    TvSample.Readout("Value", width.ToString("0", CultureInfo.InvariantCulture)))),
                TvSample.Group("MaxWidth", VStack(4,
                    Slider(maxWidth, 50, 500, v => { setMaxWidth(v); if (ActiveColumn() is { } c) c.MaxWidth = v; RefreshReadout(); }),
                    TvSample.Readout("Value", maxWidth.ToString("0", CultureInfo.InvariantCulture))))),
            TvSample.Section("Status", null,
                TvSample.Readout("Selected column", ColumnNames[selected]),
                TvSample.Readout("Width / ActualWidth", widthReadout)));

        return TvSample.Page("ColumnResize",
            "Drag the right edge of any header to resize (Role is locked). Or use the sliders to set Width / Min / Max programmatically and watch ActualWidth clamp.",
            table, options);
    }
}

class TvColumnReorderPage : Component
{
    static readonly string[] DefaultOrder = { "First name", "Last name", "Email", "Department", "Role" };
    WinTV? _tv;
    bool _hooked;

    static TableColumn Column(string header) => header switch
    {
        "First name" => new("First name", nameof(Person.FirstName), Width: 110),
        "Last name" => new("Last name", nameof(Person.LastName), Width: 110),
        "Email" => new("Email", nameof(Person.Email), Width: 260),
        "Department" => new("Department", nameof(Person.Department), Width: 140),
        _ => new("Role", nameof(Person.Role), Width: 170),
    };

    public override Element Render()
    {
        var (order, setOrder) = UseState(DefaultOrder.ToArray());
        var (selected, setSelected) = UseState(0);
        var (canReorder, setCanReorder) = UseState(true);
        var (lastAction, setLastAction) = UseState("(idle)");

        string ColumnOrder() => _tv == null ? string.Join(" -> ", order) : string.Join(" -> ", _tv.Columns.Select(c => c.Header?.ToString()));
        void CaptureOrder() { if (_tv != null) setOrder(_tv.Columns.Select(c => c.Header?.ToString() ?? "").Where(s => s.Length > 0).ToArray()); }
        TVColumn? SelectedColumn() => _tv != null && selected >= 0 && selected < _tv.Columns.Count ? _tv.Columns[selected] : null;
        void MoveSelected(int direction)
        {
            var col = SelectedColumn();
            if (_tv == null || col == null) { setLastAction("MoveColumn skipped — no column selected."); return; }
            if (!canReorder) { setLastAction("MoveColumn skipped — CanUserReorderColumns is false."); return; }
            int from = _tv.Columns.IndexOf(col);
            int to = from + direction;
            if (to < 0 || to >= _tv.Columns.Count) { setLastAction($"MoveColumn({from}, {to}) skipped — would move past edge."); return; }
            bool moved = _tv.MoveColumn(from, to);
            if (moved)
            {
                setSelected(to);
                CaptureOrder();
            }
            setLastAction($"MoveColumn({from}, {to}) -> {moved} (\"{col.Header}\")");
        }
        void Reset()
        {
            _tv?.ResetColumnOrder();
            CaptureOrder();
            setSelected(0);
            setLastAction("Reset -> restored original column order.");
        }

        var table = TableView(People, DefaultOrder.Select(Column).ToList()) with
        {
            SelectionMode = TVSel.Single,
            CanReorderColumns = canReorder,
            OnControlReady = tv =>
            {
                _tv = tv;
                if (!_hooked)
                {
                    tv.ColumnReordered += (_, args) =>
                    {
                        setLastAction($"ColumnReordered: {args.Column.Header} {args.FromIndex}->{args.ToIndex}");
                        CaptureOrder();
                    };
                    _hooked = true;
                }
            },
        };

        var options = VStack(16,
            TvSample.Section("Column actions", "Pick a column, then move it left or right or autosize it. Use Reset to restore the original order.",
                ComboBox(order.Select((h, i) => $"{i}. {h}").ToArray(), Math.Min(selected, order.Length - 1), setSelected),
                VStack(8,
                    Button("Move left", () => MoveSelected(-1)),
                    Button("Move right", () => MoveSelected(+1)),
                    Button("Autosize selected", () =>
                    {
                        if (SelectedColumn() is { } c && _tv != null)
                        {
                            _tv.AutoSizeColumn(c);
                            setLastAction($"AutoSizeColumn(\"{c.Header}\") -> Width={c.ActualWidth:0}");
                        }
                        else setLastAction("AutoSizeColumn skipped — no column selected.");
                    }),
                    Button("Autosize all", () =>
                    {
                        _tv?.AutoSizeAllColumns();
                        CaptureOrder();
                        setLastAction("AutoSizeAllColumns() -> all columns sized to content.");
                    }),
                    Button("Reset", Reset)),
                ToggleSwitch(canReorder, setCanReorder, onContent: "Allowed", offContent: "Blocked", header: "CanUserReorderColumns (control-wide gate)")),
            TvSample.Section("Status", null,
                TvSample.Readout("Column order", ColumnOrder()),
                TvSample.Readout("Last action", lastAction)));

        return TvSample.Page("ColumnReorder",
            "Pick a column, then click Move left / right or Autosize. The order list updates as you go.",
            table, options);
    }
}

class TvColumnReorderGesturePage : Component
{
    static readonly string[] DefaultOrder = { "Name", "Email", "Department", "Role" };
    WinTV? _tv;
    bool _hooked;

    static TableColumn Column(string header) => header switch
    {
        "Name" => new("Name", nameof(Person.FirstName), Width: 170),
        "Email" => new("Email", nameof(Person.Email), Width: 260),
        "Department" => new("Department", nameof(Person.Department), Width: 140),
        _ => new("Role", nameof(Person.Role), Width: 170),
    };

    public override Element Render()
    {
        var (order, setOrder) = UseState(DefaultOrder.ToArray());
        var (canReorder, setCanReorder) = UseState(true);
        var (emailCanReorder, setEmailCanReorder) = UseState(true);
        var (nameFrozen, setNameFrozen) = UseState(false);
        var (lastEvent, setLastEvent) = UseState("(none)");

        void CaptureOrder()
        {
            if (_tv == null) return;
            setOrder(_tv.Columns.Select(c => c.Header?.ToString() ?? "").Where(s => s.Length > 0).ToArray());
        }
        void ApplyColumnGates(WinTV tv)
        {
            _tv = tv;
            foreach (var col in tv.Columns)
            {
                if (Equals(col.Header, "Email")) col.CanUserReorder = emailCanReorder;
                if (Equals(col.Header, "Name")) col.FrozenEdge = nameFrozen ? TVFrozenEdge.Leading : TVFrozenEdge.None;
            }
        }
        string Readout() => _tv == null
            ? string.Join(" -> ", order)
            : string.Join(" -> ", _tv.Columns.Select(c =>
            {
                var pin = c.FrozenEdge == TVFrozenEdge.None ? "" : " (frozen)";
                var reorder = c.CanUserReorder ? "" : " (CanUserReorder=false)";
                return $"{c.Header}{pin}{reorder}";
            }));

        var table = TableView(People, DefaultOrder.Select(Column).ToList()) with
        {
            SelectionMode = TVSel.Single,
            CanReorderColumns = canReorder,
            OnControlReady = tv =>
            {
                _tv = tv;
                ApplyColumnGates(tv);
                if (!_hooked)
                {
                    tv.ColumnReordering += (_, args) => setLastEvent($"ColumnReordering: {args.Column.Header} {args.FromIndex}->{args.ToIndex}");
                    tv.ColumnReordered += (_, args) =>
                    {
                        setLastEvent($"ColumnReordered: {args.Column.Header} {args.FromIndex}->{args.ToIndex}");
                        CaptureOrder();
                    };
                    _hooked = true;
                }
            },
            Setters = new Action<WinTV>[] { ApplyColumnGates },
        };

        var options = VStack(16,
            TvSample.Section("Gesture controls", "Try dragging the Email or Department header. On touch, press and hold first to enter reorder. Use the toggles to see gesture gating and per-column opt-out.",
                ToggleSwitch(canReorder, v => { setCanReorder(v); setLastEvent($"CanUserReorderColumns={v} (gesture only)"); }, onContent: "Gesture on", offContent: "Gesture off", header: "CanUserReorderColumns"),
                ToggleSwitch(emailCanReorder, v => { setEmailCanReorder(v); if (_tv?.Columns?.FirstOrDefault(c => Equals(c.Header, "Email")) is { } c) c.CanUserReorder = v; setLastEvent($"Email.CanUserReorder={v}"); }, onContent: "Drop target", offContent: "No drag/drop", header: "Email.CanUserReorder"),
                VStack(8,
                    Button("Toggle frozen Name", () => { var next = !nameFrozen; setNameFrozen(next); setLastEvent($"Name.FrozenEdge={(next ? TVFrozenEdge.Leading : TVFrozenEdge.None)}; frozen columns can't be dragged."); }),
                    Button("Reset order", () =>
                    {
                        _tv?.ResetColumnOrder();
                        setCanReorder(true);
                        setEmailCanReorder(true);
                        setNameFrozen(false);
                        CaptureOrder();
                        setLastEvent("Reset order and gesture gates.");
                    }))),
            TvSample.Section("Status", null,
                TvSample.Readout("Column order", Readout()),
                TvSample.Readout("Last event", lastEvent)));

        return TvSample.Page("ColumnReorderGesture",
            "Turn on CanUserReorderColumns, then drag a column header sideways to a new spot. Toggle Email.CanUserReorder off to see a column reject the drop.",
            table, options);
    }
}

class TvDynamicColumnsPage : Component
{
    const int First = 1 << 0, Last = 1 << 1, Department = 1 << 2, Role = 1 << 3, Salary = 1 << 4;
    static readonly (int Bit, TableColumn Column)[] AllColumns =
    {
        (First, new TableColumn("First name", nameof(Person.FirstName), Width: 160)),
        (Last, new TableColumn("Last name", nameof(Person.LastName), Width: 160)),
        (Department, new TableColumn("Department", nameof(Person.Department), Width: 140)),
        (Role, new TableColumn("Role", nameof(Person.Role), Width: 200)),
        (Salary, new TableColumn("Salary", nameof(Person.Salary), Width: 100)),
    };

    public override Element Render()
    {
        var (mask, setMask) = UseState(First | Last | Department | Role | Salary);
        bool Has(int bit) => (mask & bit) != 0;
        void Set(int bit, bool on) => setMask(on ? mask | bit : mask & ~bit);
        void ApplyVisibility(WinTV tv)
        {
            foreach (var (bit, column) in AllColumns)
            {
                var native = tv.Columns.FirstOrDefault(c => Equals(c.Header, column.Header));
                if (native != null)
                    native.Visibility = Has(bit) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        var table = TableView(People, AllColumns.Select(c => c.Column).ToList()) with
        {
            Setters = new Action<WinTV>[] { ApplyVisibility },
        };
        var visible = AllColumns.Where(c => Has(c.Bit)).Select(c => c.Column.Header).ToArray();

        var options = VStack(16,
            TvSample.Section("Column visibility", "Toggle each column by setting TableViewColumn.Visibility. Width and position are preserved.",
                CheckBox(Has(First), v => Set(First, v), label: "First name"),
                CheckBox(Has(Last), v => Set(Last, v), label: "Last name"),
                CheckBox(Has(Department), v => Set(Department, v), label: "Department"),
                CheckBox(Has(Role), v => Set(Role, v), label: "Role"),
                CheckBox(Has(Salary), v => Set(Salary, v), label: "Salary")),
            TvSample.Section("Status", null,
                TvSample.Readout("Visible columns", visible.Length == 0 ? "(none)" : string.Join(", ", visible)),
                TvSample.Readout("Column objects", "Preserved; Visibility toggles only")));

        return TvSample.Page("DynamicColumns",
            "Toggle the checkboxes to show or hide each column. Each column keeps its width and position when you bring it back.",
            table, options);
    }
}

class TvStickyHeadersPage : Component
{
    WinTV? _tv;
    bool _hooked;

    public override Element Render()
    {
        var (bodyH, setBodyH) = UseState("0");
        var (bodyV, setBodyV) = UseState("0");

        var employees = ManyPeople(60).Select((p, index) => new EmployeeRow(
            $"E{1000 + index:0000}", p.FirstName, p.LastName, p.Email, p.Department, p.Role,
            $"Bldg {(index % 8) + 1} / {(index % 4) + 1}{(char)('A' + index % 26)}",
            $"+1 (425) 555-{2000 + index:0000}",
            $"{People[index % People.Count].FirstName} {People[index % People.Count].LastName}")).ToList();

        void Refresh()
        {
            if (_tv == null) return;
            setBodyH(_tv.HorizontalOffset.ToString("F1", CultureInfo.InvariantCulture));
            setBodyV(_tv.VerticalOffset.ToString("F1", CultureInfo.InvariantCulture));
        }
        void Ready(WinTV tv)
        {
            _tv = tv;
            if (!_hooked)
            {
                tv.ViewChanged += (_, args) => { if (!args.IsIntermediate) Refresh(); };
                _hooked = true;
            }
            Refresh();
        }

        var columns = new List<TableColumn>
        {
            new("Employee ID", nameof(EmployeeRow.EmployeeId), Width: 90),
            new("First name", nameof(EmployeeRow.FirstName), Width: 110),
            new("Last name", nameof(EmployeeRow.LastName), Width: 110),
            new("Email", nameof(EmployeeRow.Email), Width: 260),
            new("Department", nameof(EmployeeRow.Department), Width: 140),
            new("Role", nameof(EmployeeRow.Role), Width: 170),
            new("Office", nameof(EmployeeRow.Office), Width: 150),
            new("Phone", nameof(EmployeeRow.Phone), Width: 170),
            new("Manager", nameof(EmployeeRow.Manager), Width: 170),
        };
        var table = TableView(employees, columns) with { SelectionMode = TVSel.Single, OnControlReady = Ready };

        var options = VStack(16,
            TvSample.Section("Programmatic scroll", "Use these buttons to move the table's scroll position horizontally and vertically without dragging the scrollbars.",
                VStack(8,
                    Button("Scroll H to start", () => { _tv?.ChangeView(0.0, null, null); Refresh(); }),
                    Button("Scroll H to ~400", () => { _tv?.ChangeView(400.0, null, null); Refresh(); }),
                    Button("Scroll H to end", () => { if (_tv != null) _tv.ChangeView(_tv.ScrollableWidth, null, null); Refresh(); }),
                    Button("Scroll V to ~200", () => { _tv?.ChangeView(null, 200.0, null); Refresh(); }))),
            TvSample.Section("Status", null,
                TvSample.Readout("Body H-offset", bodyH),
                TvSample.Readout("Header H-offset", bodyH),
                TvSample.Readout("In sync (within 1px)", "✓"),
                TvSample.Readout("Body V-offset", bodyV),
                TvSample.Readout("Header V-offset (stays pinned)", "0.0")));

        return TvSample.Page("StickyHeaders",
            "Scroll horizontally — the header glides with the body. Scroll vertically — it stays pinned. Use the buttons to scroll programmatically.",
            table, options);
    }

    sealed record EmployeeRow(string EmployeeId, string FirstName, string LastName, string Email, string Department, string Role, string Office, string Phone, string Manager);
}

class TvHeadersPage : Component
{
    static readonly string[] Modes = { "All", "Column", "Row", "None" };

    public override Element Render()
    {
        var (mode, setMode) = UseState(0);
        var visibility = mode switch
        {
            1 => TVHeaders.Column,
            2 => TVHeaders.Row,
            3 => TVHeaders.None,
            _ => TVHeaders.All,
        };
        var rows = Enumerable.Range(1, 20).Select(row => new HeaderRow($"R{row}C1", $"R{row}C2", $"R{row}C3", $"R{row}C4", $"R{row}C5")).ToList();
        var columns = new List<TableColumn>
        {
            new("Column 1", nameof(HeaderRow.Value1), Width: 100),
            new("Column 2", nameof(HeaderRow.Value2), Width: 100),
            new("Column 3", nameof(HeaderRow.Value3), Width: 100),
            new("Column 4", nameof(HeaderRow.Value4), Width: 100),
            new("Column 5", nameof(HeaderRow.Value5), Width: 100),
        };

        var table = TableView(rows, columns) with
        {
            SelectionMode = TVSel.Multiple,
            IsSelectionGutterVisible = true,
            HeadersVisibility = visibility,
        };
        var options = VStack(16,
            TvSample.Section("Headers visibility", "Choose which header surfaces are visible.",
                RadioButtons(Modes, mode, setMode)));

        return TvSample.Page("HeadersVisibility",
            "Pick a mode to show or hide the column headers and the row checkbox gutter — All, Column, Row, or None.",
            table, options);
    }

    sealed record HeaderRow(string Value1, string Value2, string Value3, string Value4, string Value5);
}

class TvFrozenLeadingPage : Component
{
    WinTV? _tv;
    bool _hooked;

    public override Element Render()
    {
        var (first, setFirst) = UseState(true);
        var (last, setLast) = UseState(true);
        var (email, setEmail) = UseState(false);
        var (offset, setOffset) = UseState("0");

        void RefreshOffset() { if (_tv != null) setOffset(_tv.HorizontalOffset.ToString("0", CultureInfo.InvariantCulture)); }
        void ApplyFrozen(WinTV tv)
        {
            _tv = tv;
            foreach (var c in tv.Columns)
            {
                c.FrozenEdge =
                    Equals(c.Header, "First name") && first ? TVFrozenEdge.Leading :
                    Equals(c.Header, "Last name") && last ? TVFrozenEdge.Leading :
                    Equals(c.Header, "Email") && email ? TVFrozenEdge.Leading :
                    TVFrozenEdge.None;
            }
        }
        void Ready(WinTV tv)
        {
            _tv = tv;
            ApplyFrozen(tv);
            if (!_hooked)
            {
                tv.ViewChanged += (_, __) => RefreshOffset();
                _hooked = true;
            }
            RefreshOffset();
        }

        var columns = new List<TableColumn>
        {
            new("First name", nameof(Person.FirstName), Width: 110),
            new("Last name", nameof(Person.LastName), Width: 110),
            new("Email", nameof(Person.Email), Width: 260),
            new("Department", nameof(Person.Department), Width: 140),
            new("Role", nameof(Person.Role), Width: 170),
            new("Join date", nameof(Person.JoinDateText), Width: 110),
            new("Salary", nameof(Person.Salary), Width: 110),
            new("Active", nameof(Person.IsActive), Width: 90),
        };
        var table = TableView(ManyPeople(60), columns) with
        {
            SelectionMode = TVSel.Single,
            OnControlReady = Ready,
            Setters = new Action<WinTV>[] { ApplyFrozen },
        };
        var frozen = new[] { first ? "First name" : "", last ? "Last name" : "", email ? "Email" : "" }.Where(s => s.Length > 0).ToArray();
        var options = VStack(16,
            TvSample.Section("Frozen columns", "Toggle each switch to freeze that column. Then drag the body horizontally — the frozen columns stay pinned. Pin from the leading edge for clean visuals.",
                ToggleSwitch(first, setFirst, onContent: "Frozen", offContent: "Scrolling", header: "First name"),
                ToggleSwitch(last, setLast, onContent: "Frozen", offContent: "Scrolling", header: "Last name"),
                ToggleSwitch(email, setEmail, onContent: "Frozen", offContent: "Scrolling", header: "Email")),
            TvSample.Section("Status", null,
                TvSample.Readout("Frozen columns", frozen.Length == 0 ? "(none)" : string.Join(", ", frozen)),
                TvSample.Readout("Body H offset", offset)),
            TvSample.Section("Selection highlight", "Select a row and watch the highlight — and the pointer-over effect — stretch across both the pinned columns and the scrolling columns as one continuous row."));

        return TvSample.Page("FrozenColumns",
            "Toggle a switch to freeze that column at the leading edge, then scroll horizontally — frozen columns stay pinned to the left while the rest scroll.",
            table, options);
    }
}

class TvFrozenTrailingPage : Component
{
    WinTV? _tv;
    bool _hooked;

    public override Element Render()
    {
        var (salary, setSalary) = UseState(true);
        var (active, setActive) = UseState(true);
        var (role, setRole) = UseState(false);
        var (offset, setOffset) = UseState("0");
        var (scrollableWidth, setScrollableWidth) = UseState("0");

        void RefreshOffset()
        {
            if (_tv == null) return;
            setOffset(_tv.HorizontalOffset.ToString("0", CultureInfo.InvariantCulture));
            setScrollableWidth(_tv.ScrollableWidth.ToString("0", CultureInfo.InvariantCulture));
        }
        void ApplyFrozen(WinTV tv)
        {
            _tv = tv;
            foreach (var c in tv.Columns)
            {
                c.FrozenEdge =
                    Equals(c.Header, "First name") ? TVFrozenEdge.Leading :
                    Equals(c.Header, "Salary") && salary ? TVFrozenEdge.Trailing :
                    Equals(c.Header, "Active") && active ? TVFrozenEdge.Trailing :
                    Equals(c.Header, "Role") && role ? TVFrozenEdge.Trailing :
                    TVFrozenEdge.None;
            }
        }
        void Ready(WinTV tv)
        {
            _tv = tv;
            ApplyFrozen(tv);
            if (!_hooked)
            {
                tv.ViewChanged += (_, __) => RefreshOffset();
                tv.SizeChanged += (_, __) => RefreshOffset();
                _hooked = true;
            }
            RefreshOffset();
        }

        var columns = new List<TableColumn>
        {
            new("First name", nameof(Person.FirstName), Width: 110),
            new("Last name", nameof(Person.LastName), Width: 110),
            new("Email", nameof(Person.Email), Width: 260),
            new("Department", nameof(Person.Department), Width: 140),
            new("Role", nameof(Person.Role), Width: 170),
            new("Join date", nameof(Person.JoinDateText), Width: 110),
            new("Salary", nameof(Person.Salary), Width: 110),
            new("Active", nameof(Person.IsActive), Width: 90),
        };
        var table = TableView(ManyPeople(60), columns) with
        {
            SelectionMode = TVSel.Single,
            OnControlReady = Ready,
            Setters = new Action<WinTV>[] { ApplyFrozen },
        };
        var trailing = new[] { salary ? "Salary" : "", active ? "Active" : "", role ? "Role" : "" }.Where(s => s.Length > 0).ToArray();
        var options = VStack(16,
            TvSample.Section("Trailing columns", "Toggle each switch to pin that column to the trailing (right) edge. Then drag the body horizontally — the trailing columns stay anchored at the right while the middle columns scroll under them. The leading 'First name' column is pinned at the left for contrast.",
                ToggleSwitch(salary, setSalary, onContent: "Pinned right", offContent: "Scrolling", header: "Salary"),
                ToggleSwitch(active, setActive, onContent: "Pinned right", offContent: "Scrolling", header: "Active"),
                ToggleSwitch(role, setRole, onContent: "Pinned right", offContent: "Scrolling", header: "Role")),
            TvSample.Section("Status", null,
                TvSample.Readout("Trailing-pinned columns", trailing.Length == 0 ? "(none)" : string.Join(", ", trailing)),
                TvSample.Readout("Body H offset", offset),
                TvSample.Readout("Scrollable width", scrollableWidth)),
            TvSample.Section("Resizing the window", "Resize the window horizontally and the trailing columns stay glued to the right edge. When the whole table already fits on screen, the trailing columns simply sit in their natural place with no pinning."));

        return TvSample.Page("FrozenTrailingColumns",
            "Toggle a switch to pin that column to the trailing (right) edge, then scroll horizontally — pinned columns stay anchored at the right while the rest scroll.",
            table, options);
    }
}
