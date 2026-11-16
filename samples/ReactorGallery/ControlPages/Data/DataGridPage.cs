using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Data;

class DataGridPage : Component
{
    // A positional record works as an editable row model: the grid composes a new instance
    // through the matching constructor rather than mutating in place.
    record Product(int Id, string Name, string Category, double Price, bool InStock);

    static readonly string[] NamePool = { "Widget", "Gadget", "Gizmo", "Sprocket", "Cog", "Bolt", "Flange", "Washer" };
    static readonly string[] CatPool = { "Hardware", "Tools", "Parts" };

    static Product[] BuildProducts(int count) =>
        Enumerable.Range(0, count).Select(i => new Product(
            Id: i,
            Name: $"{NamePool[i % NamePool.Length]} {i}",
            Category: CatPool[i % CatPool.Length],
            Price: 4.99 + (i * 3.5 % 90),
            InStock: i % 4 != 0)).ToArray();

    public override Element Render()
    {
        var (mode, setMode) = UseState(1);
        var modes = new[] { "None", "Single", "Multiple" };
        var selection = mode switch
        {
            1 => SelectionMode.Single,
            2 => SelectionMode.Multiple,
            _ => SelectionMode.None,
        };
        var (selectedCount, setSelectedCount) = UseState(0);
        var (lastCellEdit, setLastCellEdit) = UseState("(none yet)");
        var (lastRowEdit, setLastRowEdit) = UseState("(none yet)");

        var source = UseMemo(() =>
            new ListDataSource<Product>(BuildProducts(60), p => (RowKey)p.Id));

        var rowEditSource = UseMemo(() =>
            new ListDataSource<Product>(BuildProducts(12), p => (RowKey)p.Id));

        // `editable: true` on the grid turns editing on; each column opts in individually,
        // so Id stays read-only while Name / Category / Price accept input.
        FieldDescriptor[] Columns() =>
        [
            Column<Product>("Id", p => p.Id, width: 60),
            Column<Product>("Name", p => p.Name, editable: true, displayName: "Product", width: 200),
            Column<Product>("Category", p => p.Category, editable: true, width: 140),
            Column<Product>("Price", p => p.Price, editable: true, format: "C2", width: 100),
            Column<Product>("InStock", p => p.InStock, displayName: "In stock", width: 90),
        ];

        return ScrollView(VStack(16,
            PageHeader("DataGrid", "A virtualized data grid with sortable columns, selection, and inline editing."),

            SampleCard("Columns, sorting, selection & cell editing",
                VStack(8,
                    DataGrid(
                        source: source,
                        columns: Columns(),
                        selectionMode: selection,
                        onSelectionChanged: keys => setSelectedCount(keys.Count),
                        editable: true,
                        editMode: EditMode.Cell,
                        onRowChanged: (key, item) =>
                        {
                            // Invoked synchronously on the UI thread: UseMutation calls the
                            // mutator inline, which awaits this callback. (Anything you await
                            // inside may of course resume elsewhere.)
                            setLastCellEdit($"row {key.Value} → {item.Name} / {item.Category} / {item.Price:C2}");
                            return Task.CompletedTask;
                        },
                        rowHeight: 36
                    ).Height(340),
                    TextBlock($"Selected rows: {selectedCount}").Foreground(Theme.SecondaryText),
                    TextBlock($"Last committed edit: {lastCellEdit}").Foreground(Theme.SecondaryText),
                    Caption("Click a Product / Category / Price cell to edit it; Enter or clicking away commits, Escape cancels. Id and In stock are read-only. Multiple mode: Ctrl+click toggles a row, Shift+click selects a range.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
// Memoize the source so the grid isn't remounted on every render
// (DataGrid keys off source.GetHashCode()).
var source = UseMemo(() => new ListDataSource<Product>(BuildProducts(60), p => (RowKey)p.Id));

DataGrid(
    source: source,
    columns: new FieldDescriptor[]
    {
        Column<Product>(""Id"", p => p.Id, width: 60),                   // read-only
        Column<Product>(""Name"", p => p.Name, editable: true, displayName: ""Product"", width: 200),
        Column<Product>(""Category"", p => p.Category, editable: true, width: 140),
        Column<Product>(""Price"", p => p.Price, editable: true, format: ""C2"", width: 100),
        Column<Product>(""InStock"", p => p.InStock, displayName: ""In stock"", width: 90),
    },
    selectionMode: selection,             // driven by the Options combo below
    onSelectionChanged: keys => setSelectedCount(keys.Count),
    editable: true,                       // grid-level opt-in; columns opt in individually
    editMode: EditMode.Cell,
    onRowChanged: (key, item) =>          // called inline on the UI thread by UseMutation
    {
        setLastCellEdit($""row {key.Value} → {item.Name} / {item.Category} / {item.Price:C2}"");
        return Task.CompletedTask;
    },
    rowHeight: 36)
// Click a header to sort. Columns can be reordered and resized by dragging.",
                options: OptionPanel(
                    TextBlock("Selection mode"),
                    ComboBox(modes, mode, setMode))),

            SampleCard("Row edit mode",
                VStack(8,
                    DataGrid(
                        source: rowEditSource,
                        columns: Columns(),
                        editable: true,
                        editMode: EditMode.Row,
                        onRowChanged: (key, item) =>
                        {
                            setLastRowEdit($"row {key.Value} → {item.Name} / {item.Category} / {item.Price:C2}");
                            return Task.CompletedTask;
                        },
                        rowHeight: 36
                    ).Height(260),
                    TextBlock($"Last committed row: {lastRowEdit}").Foreground(Theme.SecondaryText),
                    Caption("EditMode.Row puts every editable cell in the row into edit mode at once, so the whole row commits or cancels together.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (lastRowEdit, setLastRowEdit) = UseState(""(none yet)"");

// `rowEditSource` and `Columns()` are the same list source and five columns
// the card above uses — this card differs only in editMode.
DataGrid(
    source: rowEditSource,
    columns: Columns(),
    editable: true,
    editMode: EditMode.Row,   // whole row edits and commits together
    onRowChanged: (key, item) =>
    {
        setLastRowEdit($""row {key.Value} → {item.Name} / {item.Category} / {item.Price:C2}"");
        return Task.CompletedTask;
    },
    rowHeight: 36)

TextBlock($""Last committed row: {lastRowEdit}"")")
        ).Margin(36, 24, 36, 36));
    }
}
