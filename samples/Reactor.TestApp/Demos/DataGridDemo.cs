using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

// ═══════════════════════════════════════════════════════════════════════
//  DataGrid Demo — Phase 2A showcase
// ═══════════════════════════════════════════════════════════════════════

record Product(int Id, string Name, string Category, double Price, int Stock, bool InStock)
{
    public override string ToString() => $"{Name} ({Category})";
}

/// <summary>Mutable class for demonstrating in-place editing.</summary>
class MutableEmployee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
    public double Salary { get; set; }
    public bool Active { get; set; }
}

enum DataGridDemoSection
{
    ExplicitColumns,
    AutoColumns,
    SelectionSingle,
    SelectionMulti,
    CustomRenderers,
    LargeDataset,
    InlineEditMutable,
    InlineEditRecord,
    AdvancedFeatures,
}

class DataGridDemo : Component
{
    static readonly string[] SectionLabels =
    [
        "Explicit Columns",
        "Auto-Columns (Reflection)",
        "Single Selection",
        "Multi Selection",
        "Custom Renderers",
        "Large Dataset (10K rows)",
        "Inline Edit (Mutable)",
        "Inline Edit (Record)",
        "Advanced Features (Phase 3-5)",
    ];

    public override Element Render()
    {
        var (section, setSection) = UseState(DataGridDemoSection.ExplicitColumns);

        return FlexColumn(
            Heading("DataGrid Demo").Flex(shrink: 0),
            TextBlock("Phase 2-3: DataGrid with VirtualList, sort, selection, keyboard nav, inline editing").Foreground(SecondaryText).Flex(shrink: 0),

            HStack(8,
                TextBlock("Demo:"),
                ComboBox(SectionLabels, (int)section, i => setSection((DataGridDemoSection)i)).Width(280)
            ).Margin(0, 8, 0, 0).Flex(shrink: 0),

            Border(
                section switch
                {
                    DataGridDemoSection.ExplicitColumns => Component<ExplicitColumnsDemo>(),
                    DataGridDemoSection.AutoColumns => Component<AutoColumnsDemo>(),
                    DataGridDemoSection.SelectionSingle => Component<SingleSelectionDemo>(),
                    DataGridDemoSection.SelectionMulti => Component<MultiSelectionDemo>(),
                    DataGridDemoSection.CustomRenderers => Component<CustomRenderersDemo>(),
                    DataGridDemoSection.LargeDataset => Component<LargeDatasetDemo>(),
                    DataGridDemoSection.InlineEditMutable => Component<InlineEditMutableDemo>(),
                    DataGridDemoSection.InlineEditRecord => Component<InlineEditRecordDemo>(),
                    DataGridDemoSection.AdvancedFeatures => Component<AdvancedFeaturesDemo>(),
                    _ => Empty()
                }
            ).Flex(grow: 1).Margin(0, 8, 0, 0)
        );
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  1. Explicit columns with Column DSL
// ═══════════════════════════════════════════════════════════════════════

class ExplicitColumnsDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() =>
        {
            var products = Enumerable.Range(0, 200).Select(i => new Product(
                Id: i,
                Name: ProductHelpers.GetProductName(i),
                Category: ProductHelpers.GetCategory(i),
                Price: 9.99 + (i * 7.77 % 490),
                Stock: i * 3 % 150,
                InStock: i % 7 != 0
            )).ToArray();
            return new ListDataSource<Product>(products, p => (RowKey)p.Id);
        });

        return FlexColumn(
            SubHeading("Explicit Columns with Column DSL").Flex(shrink: 0),
            TextBlock("Columns defined via Column<T> builder. Click column headers to sort.").Foreground(SecondaryText).Flex(shrink: 0),

            DataGrid(
                source: source,
                columns: new FieldDescriptor[]
                {
                    Column<Product>("Id", p => p.Id, width: 60),
                    Column<Product>("Name", p => p.Name, displayName: "Product Name", width: 200),
                    Column<Product>("Category", p => p.Category, width: 140),
                    Column<Product>("Price", p => p.Price, displayName: "Price", format: "C2", width: 100),
                    Column<Product>("Stock", p => p.Stock, displayName: "In Stock", width: 80),
                    Column<Product>("InStock", p => p.InStock, displayName: "Available", width: 80),
                },
                rowHeight: 36
            ).Flex(grow: 1)
        ) with { RowGap = 8 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  2. Auto-columns from reflection
// ═══════════════════════════════════════════════════════════════════════

class AutoColumnsDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() =>
        {
            var products = Enumerable.Range(0, 100).Select(i => new Product(
                Id: i,
                Name: ProductHelpers.GetProductName(i),
                Category: ProductHelpers.GetCategory(i),
                Price: 9.99 + (i * 7.77 % 490),
                Stock: i * 3 % 150,
                InStock: i % 7 != 0
            )).ToArray();
            return new ListDataSource<Product>(products, p => (RowKey)p.Id);
        });

        var registry = UseMemo(() => new TypeRegistry());

        return FlexColumn(
            SubHeading("Auto-Generated Columns (Reflection)").Flex(shrink: 0),
            TextBlock("Columns auto-generated from Product record properties via TypeRegistry + reflection.").Foreground(SecondaryText).Flex(shrink: 0),
            TextBlock("Click column headers to sort. Columns auto-size from FieldDescriptor metadata.").Foreground(SecondaryText).Flex(shrink: 0),

            DataGrid(
                source: source,
                registry: registry,
                rowHeight: 36
            ).Flex(grow: 1)
        ) with { RowGap = 8 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  3. Single selection
// ═══════════════════════════════════════════════════════════════════════

class SingleSelectionDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() =>
        {
            var products = Enumerable.Range(0, 50).Select(i => new Product(
                Id: i,
                Name: ProductHelpers.GetProductName(i),
                Category: ProductHelpers.GetCategory(i),
                Price: 9.99 + (i * 7.77 % 490),
                Stock: i * 3 % 150,
                InStock: i % 7 != 0
            )).ToArray();
            return new ListDataSource<Product>(products, p => (RowKey)p.Id);
        });

        var (selectedKeys, setSelectedKeys) = UseState<IReadOnlySet<RowKey>>(new HashSet<RowKey>());

        var selectedText = selectedKeys.Count > 0
            ? $"Selected: {string.Join(", ", selectedKeys.Select(k => k.Value))}"
            : "No selection";

        return FlexColumn(
            SubHeading("Single Selection").Flex(shrink: 0),
            TextBlock("Click a row to select it. Only one row can be selected at a time.").Foreground(SecondaryText).Flex(shrink: 0),

            DataGrid(
                source: source,
                columns: new FieldDescriptor[]
                {
                    Column<Product>("Id", p => p.Id, width: 60),
                    Column<Product>("Name", p => p.Name, displayName: "Product", width: 200),
                    Column<Product>("Category", p => p.Category, width: 140),
                    Column<Product>("Price", p => p.Price, format: "C2", width: 100),
                },
                selectionMode: SelectionMode.Single,
                onSelectionChanged: keys => setSelectedKeys(keys),
                rowHeight: 36
            ).Flex(grow: 1),

            Border(
                TextBlock(selectedText).Padding(4)
            ).Background(SubtleFill).Padding(8).Flex(shrink: 0)
        ) with { RowGap = 8 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  4. Multi selection
// ═══════════════════════════════════════════════════════════════════════

class MultiSelectionDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() =>
        {
            var products = Enumerable.Range(0, 50).Select(i => new Product(
                Id: i,
                Name: ProductHelpers.GetProductName(i),
                Category: ProductHelpers.GetCategory(i),
                Price: 9.99 + (i * 7.77 % 490),
                Stock: i * 3 % 150,
                InStock: i % 7 != 0
            )).ToArray();
            return new ListDataSource<Product>(products, p => (RowKey)p.Id);
        });

        var (selectedKeys, setSelectedKeys) = UseState<IReadOnlySet<RowKey>>(new HashSet<RowKey>());

        var selectedText = selectedKeys.Count > 0
            ? $"Selected {selectedKeys.Count} rows: {string.Join(", ", selectedKeys.Take(10).Select(k => k.Value))}{(selectedKeys.Count > 10 ? "..." : "")}"
            : "No selection (click to select, Ctrl+click to toggle, Shift+click for range)";

        return FlexColumn(
            SubHeading("Multiple Selection").Flex(shrink: 0),
            TextBlock("Click rows to select. Ctrl+click to toggle. Shift+click for range selection.").Foreground(SecondaryText).Flex(shrink: 0),

            DataGrid(
                source: source,
                columns: new FieldDescriptor[]
                {
                    Column<Product>("Id", p => p.Id, width: 60),
                    Column<Product>("Name", p => p.Name, displayName: "Product", width: 200),
                    Column<Product>("Category", p => p.Category, width: 140),
                    Column<Product>("Price", p => p.Price, format: "C2", width: 100),
                    Column<Product>("Stock", p => p.Stock, width: 80),
                },
                selectionMode: SelectionMode.Multiple,
                onSelectionChanged: keys => setSelectedKeys(keys),
                rowHeight: 36
            ).Flex(grow: 1),

            Border(
                TextBlock(selectedText).Padding(4)
            ).Background(SubtleFill).Padding(8).Flex(shrink: 0)
        ) with { RowGap = 8 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  5. Custom cell renderers
// ═══════════════════════════════════════════════════════════════════════

class CustomRenderersDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() =>
        {
            var products = Enumerable.Range(0, 100).Select(i => new Product(
                Id: i,
                Name: ProductHelpers.GetProductName(i),
                Category: ProductHelpers.GetCategory(i),
                Price: 9.99 + (i * 7.77 % 490),
                Stock: i * 3 % 150,
                InStock: i % 7 != 0
            )).ToArray();
            return new ListDataSource<Product>(products, p => (RowKey)p.Id);
        });

        return FlexColumn(
            SubHeading("Custom Cell Renderers").Flex(shrink: 0),
            TextBlock("Custom cell template: price is color-coded, stock shows a progress bar, availability uses an icon.").Foreground(SecondaryText).Flex(shrink: 0),

            DataGrid(
                source: source,
                columns: new FieldDescriptor[]
                {
                    Column<Product>("Id", p => p.Id, width: 60),
                    Column<Product>("Name", p => p.Name, displayName: "Product", width: 200),
                    Column<Product>("Category", p => p.Category, width: 140),
                    (Column<Product>("Price", p => p.Price, width: 120)
                        .CellRenderer(val =>
                        {
                            var price = (double)val;
                            return price > 200
                                ? TextBlock($"${price:N2}").Foreground(Theme.Ref("SystemFillColorCriticalBrush"))
                                : price > 100
                                    ? TextBlock($"${price:N2}").Foreground(Theme.Ref("SystemFillColorCautionBrush"))
                                    : TextBlock($"${price:N2}").Foreground(Theme.Ref("SystemFillColorSuccessBrush"));
                        })).Build(),
                    (Column<Product>("Stock", p => p.Stock, displayName: "Stock Level", width: 160)
                        .CellRenderer(val =>
                        {
                            var stock = (int)val;
                            var pct = Math.Min(100, stock * 100.0 / 150);
                            var color = stock > 100
                                ? Theme.Ref("SystemFillColorSuccessBrush")
                                : stock > 30
                                    ? Theme.Ref("SystemFillColorCautionBrush")
                                    : Theme.Ref("SystemFillColorCriticalBrush");
                            return FlexRow(
                                TextBlock($"{stock}").Width(40),
                                Border(
                                    Border(Empty()).Background(color).Width(pct).Height(12).CornerRadius(4)
                                ).Background(SubtleFill).Width(100).Height(12).CornerRadius(4)
                            ) with { AlignItems = FlexAlign.Center, ColumnGap = 8 };
                        })).Build(),
                    (Column<Product>("InStock", p => p.InStock, displayName: "Available", width: 80)
                        .CellRenderer(val =>
                        {
                            var inStock = (bool)val;
                            return TextBlock(inStock ? "\u2705" : "\u274C");
                        })).Build(),
                },
                rowHeight: 36
            ).Flex(grow: 1)
        ) with { RowGap = 8 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  6. Large dataset
// ═══════════════════════════════════════════════════════════════════════

class LargeDatasetDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(10000);

        var source = UseMemo(() =>
        {
            var products = Enumerable.Range(0, count).Select(i => new Product(
                Id: i,
                Name: ProductHelpers.GetProductName(i),
                Category: ProductHelpers.GetCategory(i),
                Price: 9.99 + (i * 7.77 % 490),
                Stock: i * 3 % 150,
                InStock: i % 7 != 0
            )).ToArray();
            return new ListDataSource<Product>(products, p => (RowKey)p.Id);
        }, count);

        return FlexColumn(
            SubHeading($"Large Dataset ({count:N0} rows)").Flex(shrink: 0),
            TextBlock("Virtualized via VirtualList composition. Fixed-height rows for O(1) scroll offset.").Foreground(SecondaryText).Flex(shrink: 0),

            HStack(8,
                TextBlock("Row count:"),
                Button("1K", () => setCount(1000)),
                Button("10K", () => setCount(10000)),
                Button("50K", () => setCount(50000)),
                Button("100K", () => setCount(100000))
            ).Flex(shrink: 0),

            DataGrid(
                source: source,
                columns: new FieldDescriptor[]
                {
                    Column<Product>("Id", p => p.Id, width: 80),
                    Column<Product>("Name", p => p.Name, displayName: "Product Name", width: 220),
                    Column<Product>("Category", p => p.Category, width: 140),
                    Column<Product>("Price", p => p.Price, format: "C2", width: 100),
                    Column<Product>("Stock", p => p.Stock, width: 80),
                    Column<Product>("InStock", p => p.InStock, displayName: "Available", width: 80),
                },
                selectionMode: SelectionMode.Single,
                rowHeight: 32
            ).Flex(grow: 1)
        ) with { RowGap = 8 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  7. Inline editing — mutable class
// ═══════════════════════════════════════════════════════════════════════

class InlineEditMutableDemo : Component
{
    static readonly string[] Departments = ["Engineering", "Sales", "Marketing", "Finance", "HR", "Operations"];

    public override Element Render()
    {
        var source = UseMemo(() =>
        {
            var employees = Enumerable.Range(0, 30).Select(i => new MutableEmployee
            {
                Id = i,
                Name = $"Employee {i}",
                Department = Departments[i % Departments.Length],
                Salary = 50000 + (i * 2500),
                Active = i % 5 != 0,
            }).ToList();
            return new ListDataSource<MutableEmployee>(employees, e => (RowKey)e.Id);
        });

        var (lastEdit, setLastEdit) = UseState("");

        return FlexColumn(
            SubHeading("Inline Editing — Mutable Class").Flex(shrink: 0),
            TextBlock("Click a cell to edit. Arrow keys navigate. Enter/F2 starts editing. Escape cancels. Tab commits and moves.").Foreground(SecondaryText).Flex(shrink: 0),
            TextBlock("Editable columns: Name, Department, Salary, Active. Id is read-only.").Foreground(SecondaryText).Flex(shrink: 0),

            DataGrid(
                source: source,
                columns: new FieldDescriptor[]
                {
                    Column<MutableEmployee>("Id", e => e.Id, width: 60),
                    Column<MutableEmployee>("Name", e => e.Name, editable: true, width: 180),
                    Column<MutableEmployee>("Department", e => e.Department, editable: true, width: 140),
                    Column<MutableEmployee>("Salary", e => e.Salary, editable: true, displayName: "Salary", format: "C0", width: 120),
                    Column<MutableEmployee>("Active", e => e.Active, editable: true, width: 80),
                },
                selectionMode: SelectionMode.Single,
                editable: true,
                editMode: EditMode.Cell,
                onRowChanged: (key, item) =>
                {
                    setLastEdit($"Committed row {key.Value}: {item.Name}, {item.Department}, {item.Salary:C0}, Active={item.Active}");
                    return Task.CompletedTask;
                },
                rowHeight: 36
            ).Flex(grow: 1),

            Border(
                TextBlock(lastEdit.Length > 0 ? lastEdit : "No edits yet").Padding(4)
            ).Background(SubtleFill).Padding(8).Flex(shrink: 0)
        ) with { RowGap = 8 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  8. Inline editing — immutable record (return-new-owner)
// ═══════════════════════════════════════════════════════════════════════

class InlineEditRecordDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() =>
        {
            var products = Enumerable.Range(0, 20).Select(i => new Product(
                Id: i,
                Name: ProductHelpers.GetProductName(i),
                Category: ProductHelpers.GetCategory(i),
                Price: 9.99 + (i * 7.77 % 490),
                Stock: i * 3 % 150,
                InStock: i % 7 != 0
            )).ToArray();
            return new ListDataSource<Product>(products, p => (RowKey)p.Id);
        });

        var (lastEdit, setLastEdit) = UseState("");

        return FlexColumn(
            SubHeading("Inline Editing — Immutable Record").Flex(shrink: 0),
            TextBlock("Editing immutable records: SetValue creates a new object (return-new-owner pattern).").Foreground(SecondaryText).Flex(shrink: 0),
            TextBlock("Click a cell to edit. Name, Category, Price are editable. Id, Stock, InStock are read-only.").Foreground(SecondaryText).Flex(shrink: 0),

            DataGrid(
                source: source,
                columns: new FieldDescriptor[]
                {
                    Column<Product>("Id", p => p.Id, width: 60),
                    Column<Product>("Name", p => p.Name, editable: true, displayName: "Product Name", width: 200),
                    Column<Product>("Category", p => p.Category, editable: true, width: 140),
                    Column<Product>("Price", p => p.Price, editable: true, format: "C2", width: 100),
                    Column<Product>("Stock", p => p.Stock, width: 80),
                    Column<Product>("InStock", p => p.InStock, displayName: "Available", width: 80),
                },
                selectionMode: SelectionMode.Single,
                editable: true,
                editMode: EditMode.Cell,
                onRowChanged: (key, item) =>
                {
                    setLastEdit($"Committed row {key.Value}: {item.Name}, {item.Category}, {item.Price:C2}");
                    return Task.CompletedTask;
                },
                rowHeight: 36
            ).Flex(grow: 1),

            Border(
                TextBlock(lastEdit.Length > 0 ? lastEdit : "No edits yet — click a cell or press Enter/F2 on a focused cell").Padding(4)
            ).Background(SubtleFill).Padding(8).Flex(shrink: 0)
        ) with { RowGap = 8 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  9. Advanced Features — Phase 3-5 showcase
// ═══════════════════════════════════════════════════════════════════════

class AdvancedFeaturesDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() =>
        {
            var products = Enumerable.Range(0, 100).Select(i => new Product(
                Id: i,
                Name: ProductHelpers.GetProductName(i),
                Category: ProductHelpers.GetCategory(i),
                Price: 9.99 + (i * 7.77 % 490),
                Stock: i * 3 % 150,
                InStock: i % 7 != 0
            )).ToArray();
            return new ListDataSource<Product>(products, p => (RowKey)p.Id);
        });

        var (lastEdit, setLastEdit) = UseState("");

        return FlexColumn(
            SubHeading("Advanced Features — Phase 3-5").Flex(shrink: 0),
            TextBlock("Column reorder: drag headers to reorder columns").Foreground(SecondaryText).Flex(shrink: 0),
            TextBlock("Column resize: drag header right edge to resize").Foreground(SecondaryText).Flex(shrink: 0),
            TextBlock("Row editing: click Edit button, edit multiple cells, Save/Cancel").Foreground(SecondaryText).Flex(shrink: 0),
            TextBlock("Search: type in the search box to filter + highlight matched cells").Foreground(SecondaryText).Flex(shrink: 0),
            TextBlock("Row details: click the expand arrow to see product details").Foreground(SecondaryText).Flex(shrink: 0),
            TextBlock("Validation: Name is required (min 2 chars), Price must be 0-10000").Foreground(SecondaryText).Flex(shrink: 0),
            TextBlock("Pinned columns: Id is pinned left, InStock is pinned right").Foreground(SecondaryText).Flex(shrink: 0),

            DataGrid(
                source: source,
                columns: new FieldDescriptor[]
                {
                    Column<Product>("Id", p => p.Id, width: 60, pin: PinPosition.Left),
                    (Column<Product>("Name", p => p.Name, editable: true, displayName: "Product Name", width: 200)
                        .Validate(
                            Microsoft.UI.Reactor.Controls.Validation.Validate.Required(),
                            Microsoft.UI.Reactor.Controls.Validation.Validate.MinLength(2)
                        )).Build(),
                    Column<Product>("Category", p => p.Category, editable: true, width: 140),
                    (Column<Product>("Price", p => p.Price, editable: true, format: "C2", width: 100)
                        .Validate(Microsoft.UI.Reactor.Controls.Validation.Validate.Range(0, 10000))).Build(),
                    Column<Product>("Stock", p => p.Stock, displayName: "In Stock", width: 80),
                    Column<Product>("InStock", p => p.InStock, displayName: "Available", width: 80, pin: PinPosition.Right),
                },
                selectionMode: SelectionMode.Multiple,
                editable: true,
                editMode: EditMode.Row,
                onRowChanged: (key, item) =>
                {
                    setLastEdit($"Saved row {key.Value}: {item.Name}, {item.Category}, {item.Price:C2}");
                    return Task.CompletedTask;
                },
                rowHeight: 36,
                showSearch: true,
                rowDetailTemplate: (product, key) =>
                    FlexColumn(
                        TextBlock($"Product #{product.Id}: {product.Name}").SemiBold(),
                        TextBlock($"Category: {product.Category}"),
                        TextBlock($"Price: {product.Price:C2}  |  Stock: {product.Stock}  |  Available: {(product.InStock ? "Yes" : "No")}"),
                        Caption($"Row Key: {key.Value}").Foreground(TertiaryText)
                    ) with { RowGap = 4 }
            ),

            Border(
                TextBlock(lastEdit.Length > 0 ? lastEdit : "No edits yet").Padding(4)
            ).Background(SubtleFill).Padding(8).Flex(shrink: 0)
        ) with { RowGap = 4 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  Helpers
// ═══════════════════════════════════════════════════════════════════════

static class ProductHelpers
{
    public static string GetProductName(int i) => (i % 12) switch
    {
        0 => "Widget Alpha",
        1 => "Gadget Pro",
        2 => "Sprocket X",
        3 => "Bolt Master",
        4 => "Gear Plus",
        5 => "Valve Elite",
        6 => "Pipe Deluxe",
        7 => "Flange Ultra",
        8 => "Nut Premium",
        9 => "Washer Max",
        10 => "Screw Turbo",
        _ => "Bracket Prime",
    } + $" #{i}";

    public static string GetCategory(int i) => (i % 6) switch
    {
        0 => "Electronics",
        1 => "Hardware",
        2 => "Tools",
        3 => "Plumbing",
        4 => "Electrical",
        _ => "General",
    };
}
