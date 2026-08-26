using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Xaml;
using System.Collections.ObjectModel;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;

ReactorApp.Run<DataSystemApp>("Data System", width: 900, height: 600
);

record Product(int Id, string Name, string Category, double Price, int Stock);

static class SampleProducts
{
    public static readonly List<Product> Items = new()
    {
        new(1, "Laptop", "Electronics", 999.99, 15),
        new(2, "Desk Chair", "Furniture", 249.50, 30),
        new(3, "Monitor", "Electronics", 449.00, 22),
        new(4, "Keyboard", "Accessories", 79.99, 100),
        new(5, "Mouse", "Accessories", 39.99, 150),
        new(6, "Headphones", "Electronics", 199.00, 45),
        new(7, "Standing Desk", "Furniture", 599.00, 12),
        new(8, "Webcam", "Electronics", 129.99, 60),
        new(9, "USB Hub", "Accessories", 29.99, 200),
        new(10, "Bookshelf", "Furniture", 189.00, 25),
    };
}

// <snippet:data-source>
class DataSourceExample
{
    // Wrap an in-memory list — supports client-side sort, filter, search
    static ListDataSource<Product> CreateSource() =>
        new(SampleProducts.Items, p => (RowKey)p.Id);

    // source.Capabilities → Sort | Filter | Search | Count | Mutate
}
// </snippet:data-source>

// <snippet:explicit-columns>
class ExplicitColumnsDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() => new ListDataSource<Product>(
            SampleProducts.Items, p => (RowKey)p.Id));

        var columns = UseMemo(() => new FieldDescriptor[]
        {
            Column<Product>("Id", p => p.Id, width: 60),
            Column<Product>("Name", p => p.Name, width: 180),
            Column<Product>("Category", p => p.Category, width: 120),
            Column<Product>("Price", p => p.Price, format: "C2", width: 100),
            Column<Product>("Stock", p => p.Stock, width: 80),
        });

        return DataGrid<Product>(source, columns).Height(400);
    }
}
// </snippet:explicit-columns>

// <snippet:auto-columns>
class AutoColumnsDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() => new ListDataSource<Product>(
            SampleProducts.Items, p => (RowKey)p.Id));

        var registry = UseMemo(() => new TypeRegistry());

        return DataGrid<Product>(source, registry).Height(400);
    }
}
// </snippet:auto-columns>

// <snippet:sort-filter>
class SortFilterDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() => new ListDataSource<Product>(
            SampleProducts.Items, p => (RowKey)p.Id));

        var columns = UseMemo(() => new FieldDescriptor[]
        {
            Column<Product>("Name", p => p.Name, width: 180),
            Column<Product>("Category", p => p.Category, width: 120),
            Column<Product>("Price", p => p.Price, format: "C2", width: 100),
            Column<Product>("Stock", p => p.Stock, width: 80).NotSortable(),
        });

        return DataGrid<Product>(source, columns, showSearch: true).Height(400);
    }
}
// </snippet:sort-filter>

// <snippet:selection>
class SelectionDemo : Component
{
    public override Element Render()
    {
        var initialSelection = UseMemo<IReadOnlySet<RowKey>>(() => new HashSet<RowKey>());
        var (selected, setSelected) = UseState(initialSelection);

        var source = UseMemo(() => new ListDataSource<Product>(
            SampleProducts.Items, p => (RowKey)p.Id));

        var columns = UseMemo(() => AutoColumns<Product>());

        return VStack(12,
            TextBlock($"Selected: {selected.Count} items").Opacity(0.6),
            DataGrid<Product>(source, columns,
                selectionMode: SelectionMode.Multiple,
                onSelectionChanged: setSelected).Height(350)
        );
    }
}
// </snippet:selection>

// <snippet:inline-editing>
class InlineEditingDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() => new ListDataSource<Product>(
            SampleProducts.Items, p => (RowKey)p.Id));

        var columns = UseMemo(() => new FieldDescriptor[]
        {
            Column<Product>("Id", p => p.Id, width: 60),
            Column<Product>("Name", p => p.Name, editable: true, width: 180),
            Column<Product>("Price", p => p.Price, editable: true,
                format: "C2", width: 100),
            Column<Product>("Stock", p => p.Stock, editable: true, width: 80),
        });

        return DataGrid<Product>(source, columns,
            editable: true,
            editMode: EditMode.Cell,
            onRowChanged: async (key, product) =>
            {
                // Persist the change — e.g., call an API
            }).Height(400);
    }
}
// </snippet:inline-editing>

// <snippet:column-features>
class ColumnFeaturesDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() => new ListDataSource<Product>(
            SampleProducts.Items, p => (RowKey)p.Id));

        var columns = UseMemo(() => new FieldDescriptor[]
        {
            Column<Product>("Id", p => p.Id, width: 60,
                pin: PinPosition.Left),
            Column<Product>("Name", p => p.Name, width: 200),
            Column<Product>("Category", p => p.Category, width: 140),
            Column<Product>("Price", p => p.Price, format: "C2", width: 120),
            Column<Product>("Stock", p => p.Stock, width: 100),
        });

        return DataGrid<Product>(source, columns).Height(400);
    }
}
// </snippet:column-features>

// <snippet:paging>
class PagingDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() =>
        {
            var products = Enumerable.Range(1, 10_000)
                .Select(i => new Product(i, $"Product {i}",
                    i % 3 == 0 ? "Electronics" : i % 3 == 1 ? "Furniture" : "Accessories",
                    Math.Round(10 + i * 0.99, 2), i % 200))
                .ToList();
            return new ListDataSource<Product>(products, p => (RowKey)p.Id);
        });

        var columns = UseMemo(() => AutoColumns<Product>());

        // DataPageCache loads 50-row blocks on demand, keeps 20 in LRU cache
        return DataGrid<Product>(source, columns).Height(400);
    }
}
// </snippet:paging>

// <snippet:row-details>
class RowDetailsDemo : Component
{
    public override Element Render()
    {
        var source = UseMemo(() => new ListDataSource<Product>(
            SampleProducts.Items, p => (RowKey)p.Id));

        var columns = UseMemo(() => AutoColumns<Product>());

        return DataGrid<Product>(source, columns,
            rowDetailTemplate: (product, key) =>
                VStack(8,
                    TextBlock($"Product ID: {product.Id}").Bold(),
                    TextBlock($"Full details for {product.Name}"),
                    TextBlock($"Category: {product.Category}"),
                    TextBlock($"Unit price: {product.Price:C2}, Stock: {product.Stock}")
                ).Padding(16).Background(Theme.CardBackground)
        ).Height(400);
    }
}
// </snippet:row-details>

// <snippet:master-detail>
class MasterDetailDemo : Component
{
    public override Element Render()
    {
        // Selection lives in the parent, so it survives sort, filter, and refresh.
        var (selected, setSelected) = UseState<RowKey?>(null);

        var source = UseMemo(() => new ListDataSource<Product>(
            SampleProducts.Items, p => (RowKey)p.Id));

        var columns = UseMemo(() => new FieldDescriptor[]
        {
            Column<Product>("Name", p => p.Name, width: 180),
            Column<Product>("Price", p => p.Price, format: "C2", width: 100),
        });

        var detail = SampleProducts.Items.FirstOrDefault(
            p => selected is { } key && (RowKey)p.Id == key);

        return HStack(0,
            DataGrid<Product>(source, columns,
                selectionMode: SelectionMode.Single,
                // The callback hands you the full selection snapshot, not a delta.
                onSelectionChanged: keys => setSelected(
                    keys.Count == 0 ? null : (RowKey?)keys.First())
            ).Width(480).Height(350),
            detail is null
                ? Border(Caption("Select a product")).Padding(24)
                : VStack(4,
                    SubHeading(detail.Name),
                    Caption($"{detail.Category} — {detail.Price:C2}")
                  ).Padding(24).Width(360));
    }
}
// </snippet:master-detail>

// <snippet:observable-source>
class ObservableSourceDemo : Component
{
    public override Element Render()
    {
        // One stable collection instance. UseMemo takes a factory, so the
        // collection is allocated once; UseRef would re-evaluate its argument
        // on every render and throw the copy away.
        var collection = UseMemo(() => new ObservableCollection<Product>(SampleProducts.Items));

        var source = UseMemo(() => new ObservableListDataSource<Product>(
            collection, p => (RowKey)p.Id), collection);

        // The source subscribes to CollectionChanged and is IDisposable, so
        // memoizing it is not enough — dispose it on unmount or the collection
        // keeps the source (and its per-item subscriptions) alive.
        UseEffect(() => () => source.Dispose(), source);

        var columns = UseMemo(() => new FieldDescriptor[]
        {
            Column<Product>("Name", p => p.Name, width: 180),
            Column<Product>("Stock", p => p.Stock, width: 80),
        });

        return VStack(12,
            // Mutations raise CollectionChanged -> DataChanged -> the grid re-fetches.
            Button("Add product", () => collection.Add(new Product(
                collection.Count + 1, "New item", "Accessories", 9.99, 1))),
            DataGrid<Product>(source, columns).Height(320));
    }
}
// </snippet:observable-source>

class DataSystemApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Data System"),
                Component<ExplicitColumnsDemo>(),
                Component<SortFilterDemo>(),
                Component<SelectionDemo>(),
                Component<MasterDetailDemo>(),
                Component<ObservableSourceDemo>()
            ).Padding(24)
        );
    }
}
