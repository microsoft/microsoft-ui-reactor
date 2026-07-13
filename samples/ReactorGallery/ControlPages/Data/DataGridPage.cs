using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Data;

class DataGridPage : Component
{
    record Product(int Id, string Name, string Category, double Price, bool InStock);

    static readonly string[] NamePool = { "Widget", "Gadget", "Gizmo", "Sprocket", "Cog", "Bolt", "Flange", "Washer" };
    static readonly string[] CatPool = { "Hardware", "Tools", "Parts" };

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

        var source = UseMemo(() =>
        {
            var products = Enumerable.Range(0, 60).Select(i => new Product(
                Id: i,
                Name: $"{NamePool[i % NamePool.Length]} {i}",
                Category: CatPool[i % CatPool.Length],
                Price: 4.99 + (i * 3.5 % 90),
                InStock: i % 4 != 0)).ToArray();
            return new ListDataSource<Product>(products, p => (RowKey)p.Id);
        });

        return ScrollView(VStack(16,
            PageHeader("DataGrid", "A virtualized data grid with sortable columns, selection, and inline editing."),

            SampleCard("Columns, sorting & selection",
                VStack(8,
                    DataGrid(
                        source: source,
                        columns: new FieldDescriptor[]
                        {
                            Column<Product>("Id", p => p.Id, width: 60),
                            Column<Product>("Name", p => p.Name, displayName: "Product", width: 200),
                            Column<Product>("Category", p => p.Category, width: 140),
                            Column<Product>("Price", p => p.Price, format: "C2", width: 100),
                            Column<Product>("InStock", p => p.InStock, displayName: "In stock", width: 90),
                        },
                        selectionMode: selection,
                        onSelectionChanged: keys => setSelectedCount(keys.Count),
                        rowHeight: 36
                    ).Height(340),
                    TextBlock($"Selected rows: {selectedCount}").Foreground(Theme.SecondaryText)),
                sourceCode: @"
// Memoize the source so the grid isn't remounted on every render
// (DataGrid keys off source.GetHashCode()).
var source = UseMemo(() => new ListDataSource<Product>(products, p => (RowKey)p.Id));

DataGrid(
    source: source,
    columns: new FieldDescriptor[]
    {
        Column<Product>(""Name"", p => p.Name, displayName: ""Product"", width: 200),
        Column<Product>(""Price"", p => p.Price, format: ""C2"", width: 100),
        Column<Product>(""InStock"", p => p.InStock, displayName: ""In stock"", width: 90),
    },
    selectionMode: SelectionMode.Single,
    onSelectionChanged: keys => setSelectedCount(keys.Count),
    rowHeight: 36)
// Click a header to sort. Columns can be reordered and resized by dragging.",
                options: OptionPanel(
                    TextBlock("Selection mode"),
                    ComboBox(modes, mode, setMode)))
        ).Margin(36, 24, 36, 36));
    }
}
