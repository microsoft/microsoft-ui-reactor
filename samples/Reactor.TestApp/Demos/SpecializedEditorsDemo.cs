using System.Collections.ObjectModel;
using INotifyPropertyChanged = System.ComponentModel.INotifyPropertyChanged;
using PropertyChangedEventArgs = System.ComponentModel.PropertyChangedEventArgs;
using PropertyChangedEventHandler = System.ComponentModel.PropertyChangedEventHandler;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Layout;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;
using WinUIColor = global::Windows.UI.Color;

// ═══════════════════════════════════════════════════════════════════════
//  Specialized Editors Demo — shows every type-specific editor in DataGrid
//  and PropertyGrid. Three sections demonstrate the two discovery paths
//  and how PropertyGrid reads the same metadata.
//
//  Data flow: Gizmo is INotifyPropertyChanged, hosted in an
//  ObservableCollection wrapped by ObservableListDataSource. Edits made in
//  any of the three sections fire PropertyChanged, which the data source
//  forwards as DataChanged, which refreshes every grid bound to it — so
//  editing InStock in the PropertyGrid immediately flips the pill in both
//  DataGrids above it.
// ═══════════════════════════════════════════════════════════════════════

enum GizmoPriority { Low, Medium, High, Critical }

abstract class NotifyBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>
/// Covers one property per specialized editor type. INPC-aware so edits
/// from any surface (DataGrid cells or PropertyGrid fields) propagate back
/// to every other surface through ObservableListDataSource.
/// </summary>
class Gizmo : NotifyBase
{
    // A stable per-instance key decoupled from user-editable fields. Even if
    // a user changed Id or Name, row identity in the DataGrid stays anchored
    // to this Guid — selection and edit state never alias across rows.
    internal readonly global::System.Guid _key = global::System.Guid.NewGuid();

    int _id;
    string _name = "";
    int _quantity;
    decimal _unitPrice;
    global::System.DateTime _orderDate;
    global::System.TimeSpan _duration;
    bool _inStock;
    GizmoPriority _priority;
    string _docsUrl = "";
    global::System.Uri _website = new("https://microsoft.com");
    WinUIColor _accentColor;

    [PropertyReadOnly]
    public int Id { get => _id; set => Set(ref _id, value); }
    public string Name { get => _name; set => Set(ref _name, value); }

    [Range(0, 10_000)]
    public int Quantity { get => _quantity; set => Set(ref _quantity, value); }

    public decimal UnitPrice { get => _unitPrice; set => Set(ref _unitPrice, value); }
    public global::System.DateTime OrderDate { get => _orderDate; set => Set(ref _orderDate, value); }
    public global::System.TimeSpan Duration { get => _duration; set => Set(ref _duration, value); }
    public bool InStock { get => _inStock; set => Set(ref _inStock, value); }
    public GizmoPriority Priority { get => _priority; set => Set(ref _priority, value); }

    [DataType(DataType.Url)]
    public string DocsUrl { get => _docsUrl; set => Set(ref _docsUrl, value); }

    public global::System.Uri Website { get => _website; set => Set(ref _website, value); }
    public WinUIColor AccentColor { get => _accentColor; set => Set(ref _accentColor, value); }
}

static class GizmoSeed
{
    public static ObservableCollection<Gizmo> MakeItems() => new([
        new Gizmo
        {
            Id = 1, Name = "Resonator", Quantity = 42, UnitPrice = 19.99m,
            OrderDate = new(2026, 3, 15),
            Duration = global::System.TimeSpan.FromHours(2.5),
            InStock = true, Priority = GizmoPriority.High,
            DocsUrl = "https://example.com/docs/resonator",
            Website = new("https://example.com"),
            AccentColor = global::Microsoft.UI.Colors.SteelBlue,
        },
        new Gizmo
        {
            Id = 2, Name = "Modulator", Quantity = 8, UnitPrice = 125.00m,
            OrderDate = new(2026, 4, 1),
            Duration = global::System.TimeSpan.FromMinutes(45),
            InStock = false, Priority = GizmoPriority.Critical,
            DocsUrl = "https://example.com/docs/modulator",
            Website = new("https://example.org"),
            AccentColor = global::Microsoft.UI.Colors.Crimson,
        },
        new Gizmo
        {
            Id = 3, Name = "Transducer", Quantity = 1000, UnitPrice = 0.79m,
            OrderDate = new(2026, 2, 28),
            Duration = global::System.TimeSpan.FromMinutes(5),
            InStock = true, Priority = GizmoPriority.Low,
            DocsUrl = "https://example.com/docs/transducer",
            Website = new("https://github.com"),
            AccentColor = global::Microsoft.UI.Colors.MediumSeaGreen,
        },
        new Gizmo
        {
            Id = 4, Name = "Flux Capacitor", Quantity = 1, UnitPrice = 1_210_000m,
            OrderDate = new(2026, 1, 1),
            Duration = global::System.TimeSpan.FromHours(24),
            InStock = false, Priority = GizmoPriority.Medium,
            DocsUrl = "https://example.com/docs/flux",
            Website = new("https://bttf.com"),
            AccentColor = global::Microsoft.UI.Colors.MediumPurple,
        },
    ]);
}

class SpecializedEditorsDemo : Component
{
    public override Element Render()
    {
        // Shared data across all three sections. Using ObservableCollection +
        // ObservableListDataSource means INPC mutations on any Gizmo (from any
        // surface) fire DataChanged, and every grid bound to this source
        // refreshes — that's how the PropertyGrid edit propagates to the grid.
        var items = UseMemo(() => GizmoSeed.MakeItems());
        var source = UseMemo(() =>
            new ObservableListDataSource<Gizmo>(items, g => (RowKey)g._key.ToString()));
        var registry = UseMemo(() => new TypeRegistry());
        var (selectedKey, setSelectedKey) = UseState<string?>(items[0]._key.ToString());

        Gizmo? selected = selectedKey is null
            ? null
            : items.FirstOrDefault(g => g._key.ToString() == selectedKey);

        Action<IReadOnlySet<RowKey>> onSelect = keys =>
        {
            if (keys.Count == 0) { setSelectedKey(null); return; }
            setSelectedKey(keys.First().Value);
        };

        return FlexColumn(
            Heading("Specialized Editors").Flex(shrink: 0),
            TextBlock(
                "Same data, three renderings. Compare how auto-discovery picks the " +
                "right editor from each property's type (and attributes), vs how an " +
                "explicit typed column can intentionally pick something different. " +
                "Edits in any surface propagate to the others via INPC."
            ).Foreground(SecondaryText).Flex(shrink: 0),

            ScrollView(
                VStack(16,
                    // ──────────────────────────────────────────────────
                    // Section 1 — read-only display via AutoColumns.
                    // Editable=false keeps this section purely about how
                    // auto-discovery picks renderers; edits happen in the
                    // PropertyGrid and flow back through INPC.
                    // ──────────────────────────────────────────────────
                    SectionCard(
                        "1.  DataGrid — metadata-driven (display)",
                        "AutoColumns resolves each property's type through TypeRegistry. " +
                            "bool → CheckMark. DateTime → short date. TimeSpan → short time. " +
                            "Uri → hyperlink. Color → swatch. enum → plain text. " +
                            "DocsUrl renders as a hyperlink because of [DataType(DataType.Url)]. " +
                            "This section is non-editable — click a row to select, edit below.",
                        DataGridDsl.DataGrid(
                            source: source,
                            registry: registry,
                            selectionMode: SelectionMode.Single,
                            onSelectionChanged: onSelect,
                            editable: false,
                            rowHeight: 40
                        ).Height(260)
                    ),

                    // ──────────────────────────────────────────────────
                    // Section 2 — explicit typed columns, editable.
                    // ──────────────────────────────────────────────────
                    SectionCard(
                        "2.  DataGrid — explicit typed columns (edit in grid)",
                        "Same data, columns set explicitly via NumberColumn / DateColumn / " +
                            "ToggleSwitchColumn / ComboBoxColumn / HyperlinkColumn / ColorColumn. " +
                            "InStock is intentionally rendered as a toggle pill + ToggleSwitch " +
                            "editor here (vs the CheckMark in section 1). Priority uses a " +
                            "ComboBox seeded with only three of the four enum values.",
                        DataGridDsl.DataGrid(
                            source: source,
                            columns: BuildExplicitColumns(),
                            selectionMode: SelectionMode.Single,
                            onSelectionChanged: onSelect,
                            editable: true,
                            rowHeight: 40
                        ).Height(260)
                    ),

                    // ──────────────────────────────────────────────────
                    // Section 3 — PropertyGrid bound to selected row
                    // ──────────────────────────────────────────────────
                    SectionCard(
                        "3.  PropertyGrid — metadata-driven",
                        selected is null
                            ? "Select a row above to edit its properties here."
                            : $"Editing Gizmo #{selected.Id}. PropertyGrid uses the Standard " +
                                "tier, so bool renders as ToggleSwitch (vs CheckMark/pill above). " +
                                "All other editors come from the same TypeRegistry resolution " +
                                "path. Toggling InStock here updates both grids above.",
                        selected is null
                            ? (Element)TextBlock("(nothing selected)").Foreground(SecondaryText).Padding(12)
                            : PropertyGridDsl.PropertyGrid(target: selected, registry: registry)
                    )
                )
            ).Flex(grow: 1)
        ) with { RowGap = 8 };
    }

    /// <summary>
    /// Section 2's explicit columns. Uses TypedColumns factories so the editor
    /// and cell renderer are wired at the call site — no registry lookup at
    /// render time. Two rows (InStock, Priority) deliberately pick a different
    /// editor than the auto-discovered one to make the override visible.
    /// </summary>
    static FieldDescriptor[] BuildExplicitColumns() =>
    [
        ColumnDsl.Column<Gizmo>("Id", g => g.Id, width: 40),
        ColumnDsl.Column<Gizmo>("Name", g => g.Name, editable: true, width: 140),
        TypedColumns.NumberColumn<Gizmo>("Quantity", g => g.Quantity,
            min: 0, max: 10_000, width: 100),
        TypedColumns.NumberColumn<Gizmo>("UnitPrice", g => g.UnitPrice,
            displayName: "Unit Price", format: "C2", width: 110),
        TypedColumns.DateColumn<Gizmo>("OrderDate", g => g.OrderDate,
            displayName: "Date", format: "d", width: 130),
        TypedColumns.TimeColumn<Gizmo>("Duration", g => g.Duration, width: 110),
        // Explicit: render bool as a ToggleSwitch pill even in DataGrid's compact tier.
        TypedColumns.ToggleSwitchColumn<Gizmo>("InStock", g => g.InStock,
            displayName: "Stock", width: 100),
        // Explicit: only offer three of the four enum values here.
        TypedColumns.ComboBoxColumn<Gizmo, GizmoPriority>(
            "Priority", g => g.Priority,
            choices: [GizmoPriority.Low, GizmoPriority.Medium, GizmoPriority.High],
            width: 110),
        TypedColumns.HyperlinkColumn<Gizmo>("Website", g => g.Website, width: 200),
        TypedColumns.ColorColumn<Gizmo>("AccentColor", g => g.AccentColor,
            displayName: "Accent", width: 130),
    ];

    static Element SectionCard(string title, string description, Element content) =>
        Border(
            VStack(8,
                SubHeading(title),
                TextBlock(description).Foreground(SecondaryText).TextWrapping(
                    Microsoft.UI.Xaml.TextWrapping.Wrap),
                Border(content).Padding(0)
            )
        ).Padding(16).WithBorder("#30000000", 1).CornerRadius(8);
}
