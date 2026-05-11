
# Collections

When you need to render a list of data, Reactor provides three typed collection
elements and a simple `ForEach` helper. Each takes your data, a key selector,
and a view builder function that returns an [element](components.md).

## Sample Data

The examples on this page use a shared `Contact` record and sample data
generator:

```csharp
record Contact(string Id, string Name, string Email);

static class SampleData
{
    public static readonly List<Contact> Contacts =
        Enumerable.Range(1, 50).Select(i =>
            new Contact($"c{i}", $"Contact {i}",
                $"user{i}@example.com")
        ).ToList();
}
```

## ListView

`ListView<T>` renders a scrollable vertical list. Pass your data, a function
that returns a unique key for each item, and a builder that turns each item
into an element:

```csharp
class ListViewDemo : Component
{
    public override Element Render()
    {
        var contacts = SampleData.Contacts.Take(10).ToList();

        return VStack(12,
            SubHeading("ListView"),
            ListView<Contact>(
                contacts,
                c => c.Id,
                (contact, index) =>
                    HStack(12,
                        TextBlock(contact.Name).Bold(),
                        TextBlock(contact.Email).Opacity(0.6)
                    ).Padding(8)
            ).Height(300)
        ).Padding(24);
    }
}
```

![ListView with contacts](images/collections/listview.png)

The `keySelector` parameter (`c => c.Id`) tells Reactor how to identify each
item. When your data changes, Reactor uses keys to match old items to new ones
and update only what changed — no full-list rebuild.

## LazyVStack (Virtualized)

`LazyVStack<T>` looks like `ListView<T>` but only creates elements for items
currently visible on screen. Use it for large datasets:

```csharp
class LazyVStackDemo : Component
{
    public override Element Render()
    {
        var contacts = SampleData.Contacts;

        return VStack(12,
            SubHeading($"LazyVStack ({contacts.Count} items)"),
            LazyVStack<Contact>(
                contacts,
                c => c.Id,
                (contact, index) =>
                    HStack(12,
                        TextBlock($"{index + 1}.").Width(30),
                        TextBlock(contact.Name).Bold(),
                        TextBlock(contact.Email).Opacity(0.6)
                    ).Padding(8)
            ).Height(300)
        ).Padding(24);
    }
}
```

![LazyVStack with 50 items](images/collections/lazyvstack.png)

Even with 50 items in the list, `LazyVStack` only materializes the rows
you can see. As you scroll, it creates new rows and recycles old ones. This
keeps memory usage constant regardless of list size.

When to use which:

| Collection | Virtualized | Best for |
|-----------|------------|---------|
| `ListView<T>` | No | Small lists (< 50 items) |
| `LazyVStack<T>` | Yes | Large lists with known items |
| `VirtualList` | Yes | Count-based / async-loaded lists |

## GridView

`GridView<T>` lays items out in a wrapping grid. The framework determines
column count based on item width and available space:

```csharp
class GridViewDemo : Component
{
    public override Element Render()
    {
        var contacts = SampleData.Contacts.Take(12).ToList();

        return VStack(12,
            SubHeading("GridView"),
            GridView<Contact>(
                contacts,
                c => c.Id,
                (contact, index) =>
                    VStack(4,
                        TextBlock(contact.Name).Bold(),
                        TextBlock(contact.Email).FontSize(12).Opacity(0.6)
                    ).Padding(12)
                     .Background("#f5f5f5")
                     .CornerRadius(8)
                     .Width(160).Height(80)
            ).Height(300)
        ).Padding(24);
    }
}
```

![GridView with contact cards](images/collections/gridview.png)

Each item is sized by the element you return from the view builder. The
grid automatically wraps items into rows based on the container width.

## VirtualList (Count-Based)

`VirtualList` provides count-based virtualization — you tell it how many
items exist and it calls your render function only for visible indices.
Use it when items are loaded asynchronously or your data source provides
a count but not all items upfront:

```csharp
class VirtualListDemo : Component
{
    public override Element Render()
    {
        return VStack(12,
            SubHeading("VirtualList (10,000 items)"),
            VirtualList(
                itemCount: 10_000,
                renderItem: index =>
                    HStack(12,
                        TextBlock($"{index + 1}.").Width(50),
                        TextBlock($"Item {index + 1}").Bold(),
                        TextBlock($"data-{index}@example.com").Opacity(0.6)
                    ).Padding(8),
                getItemKey: index => $"item-{index}",
                itemHeight: 40
            ).Height(300)
        ).Padding(24);
    }
}
```

![VirtualList with 10,000 items](images/collections/virtuallist.png)

Unlike `LazyVStack<T>` which takes a full list, `VirtualList` takes an
`itemCount` and a `renderItem(index)` callback. This makes it ideal for
paginated data sources where items load on demand.

`VirtualListRef` provides imperative control over the virtualized list:

```csharp
class VirtualListRefDemo : Component
{
    public override Element Render()
    {
        var listRef = UseRef<VirtualListRef?>(null);
        var (targetIndex, setTargetIndex) = UseState("5000");

        return VStack(12,
            SubHeading("VirtualListRef — Imperative Scroll"),
            HStack(8,
                TextField(targetIndex, setTargetIndex,
                    placeholder: "Index"),
                Button("Scroll To", () =>
                {
                    if (int.TryParse(targetIndex, out var idx))
                        listRef.Current?.ScrollToIndex(idx);
                })
            ),
            VirtualList(
                itemCount: 10_000,
                renderItem: index =>
                    TextBlock($"Row {index + 1}").Padding(8),
                getItemKey: index => $"row-{index}",
                itemHeight: 36,
                @ref: r => listRef.Current = r
            ).Height(250)
        ).Padding(24);
    }
}
```

| Member | Purpose |
|--------|---------|
| `ScrollToIndex(index)` | Jump to a specific item |
| `ScrollOffset` | Current scroll position |
| `RestoreScrollOffset(offset)` | Restore a saved scroll position |
| `Repeater` | Access the underlying WinUI ItemsRepeater |

Set `itemHeight` for a fixed-height fast path (O(1) offset calculation) or
`estimatedItemHeight` for variable-height rows with automatic measurement.
Use `onVisibleRangeChanged` to load data blocks as the user scrolls.

## ForEach

For small, non-virtualized inline lists, use `ForEach`. It maps a collection
to elements without creating a scrollable container:

```csharp
class ForEachDemo : Component
{
    public override Element Render()
    {
        var colors = new[]
        {
            ("Red", "#ff4444"), ("Green", "#44ff44"),
            ("Blue", "#4444ff"), ("Yellow", "#ffff44")
        };

        return VStack(12,
            SubHeading("ForEach (non-virtualized)"),
            HStack(8,
                ForEach(colors, ((string Name, string Hex) color) =>
                    TextBlock(color.Name)
                        .Padding(horizontal: 8, vertical: 16)
                        .Background(color.Hex)
                        .CornerRadius(4)
                        .WithKey(color.Name)
                )
            )
        ).Padding(24);
    }
}
```

![ForEach colored tags](images/collections/foreach.png)

`ForEach` is a convenience for `items.Select(render).ToArray()` that works
directly inside element trees. Use it when you want to inline a small list
of items inside a larger [layout](layout.md).

## Stable Identity with WithKey

When rendering dynamic lists, always give each item a stable key with
`.WithKey()`. Without keys, Reactor matches items by position — adding or
removing an item causes every subsequent item to be rebuilt:

```csharp
class WithKeyDemo : Component
{
    public override Element Render()
    {
        var (items, updateItems) = UseReducer(
            new List<string> { "Apple", "Banana", "Cherry" });
        var (newItem, setNewItem) = UseState("");

        return VStack(12,
            SubHeading("Stable Identity with WithKey"),
            HStack(8,
                TextField(newItem, setNewItem, placeholder: "New item"),
                Button("Add", () => {
                    if (!string.IsNullOrWhiteSpace(newItem)) {
                        updateItems(l => [.. l, newItem.Trim()]);
                        setNewItem("");
                    }
                })
            ),
            VStack(4, items.Select((item, i) =>
                HStack(8,
                    TextBlock(item),
                    Button("Remove", () => updateItems(
                        l => l.Where((_, idx) => idx != i).ToList()))
                ).WithKey($"item-{item}-{i}")
            ).ToArray())
        ).Padding(24);
    }
}
```

![WithKey demo](images/collections/withkey.png)

The typed collections (`ListView<T>`, `LazyVStack<T>`, `GridView<T>`) handle
keying automatically through their `keySelector` parameter. You only need
`.WithKey()` manually when using `ForEach`, `Select().ToArray()`, or other
manual list rendering.

Rules for good keys:

- **Use a stable identifier** from your data (database ID, unique name).
  Avoid using the array index as a key — it defeats the purpose.
- **Keys must be unique** within their sibling list. Duplicates cause
  undefined reconciliation behavior.
- **Keys should be strings.** The `WithKey` modifier accepts a string.

## Tips

**Use `keySelector` wisely.** The key must uniquely identify each item across
re-renders. A database ID or GUID is ideal. Avoid index-based keys like
`i.ToString()` — they break when items are reordered or removed.

**Prefer `LazyVStack<T>` for anything beyond a handful of items.** The
virtualization overhead is negligible, but the memory savings with large lists
are significant.

**Keep view builders simple.** The function you pass to `ListView<T>` runs
for every visible item on every render. Extract complex item layouts into
their own [`Component<TProps>`](components.md) to get automatic memoization.

**Use `ForEach` for inline lists, typed collections for scrollable lists.**
`ForEach` does not create a scroll container — it just maps data to elements.
For scrollable content, use `ListView<T>` or `LazyVStack<T>`.

**Remember the `index` parameter.** All view builders receive `(T item, int index)`.
Use the index for display (row numbers) but not for keys.

## Next Steps

- **[Forms and Input](forms.md)** — controlled input controls and validation patterns
- **[Navigation](navigation.md)** — stack-based routing, NavigationView, and tabs
- **[Data System](data-system.md)** — DataGrid with sort, filter, search, and inline editing
- **[Flex Layout](flex-layout.md)** — wrapping grids and proportional sizing for collection items
- **[Components](components.md)** — extract item templates into reusable memoized components
