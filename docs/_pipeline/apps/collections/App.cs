using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<CollectionsApp>("Collections", width: 700, height: 600
#if DEBUG
    , preview: true
#endif
);

// <snippet:sample-data>
record Contact(string Id, string Name, string Email);

static class SampleData
{
    public static readonly List<Contact> Contacts =
        Enumerable.Range(1, 50).Select(i =>
            new Contact($"c{i}", $"Contact {i}",
                $"user{i}@example.com")
        ).ToList();
}
// </snippet:sample-data>

// <snippet:listview>
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
// </snippet:listview>

// <snippet:lazyvstack>
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
// </snippet:lazyvstack>

// <snippet:gridview>
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
// </snippet:gridview>

// <snippet:virtuallist>
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
// </snippet:virtuallist>

// <snippet:virtuallist-ref>
class VirtualListRefDemo : Component
{
    public override Element Render()
    {
        var listRef = UseRef<VirtualListRef?>(null);
        var (targetIndex, setTargetIndex) = UseState("5000");

        return VStack(12,
            SubHeading("VirtualListRef — Imperative Scroll"),
            HStack(8,
                TextBox(targetIndex, setTargetIndex,
                    placeholderText: "Index"),
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
// </snippet:virtuallist-ref>

// <snippet:foreach>
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
// </snippet:foreach>

// <snippet:multi-select>
class MultiSelectDemo : Component
{
    public override Element Render()
    {
        var contacts = SampleData.Contacts.Take(10).ToList();
        var (selectedIds, setSelectedIds) = UseState(new List<string>());

        return VStack(12,
            SubHeading($"{selectedIds.Count} selected"),
            ListView<Contact>(
                contacts,
                c => c.Id,
                (contact, index) =>
                    HStack(12,
                        TextBlock(contact.Name).Bold(),
                        TextBlock(contact.Email).Opacity(0.6)
                    ).Padding(8)
            )
            .Set(lv => lv.SelectionMode =
                Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Multiple)
            .SelectionChanged(selected =>
                setSelectedIds(selected.Select(c => c.Id).ToList()))
            .Height(300)
        ).Padding(24);
    }
}
// </snippet:multi-select>

// <snippet:withkey>
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
                TextBox(newItem, setNewItem, placeholderText: "New item"),
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
// </snippet:withkey>

// <snippet:grouping>
class GroupingDemo : Component
{
    public override Element Render()
    {
        var grouped = SampleData.Contacts
            .Take(24)
            .GroupBy(c => c.Name[0])
            .OrderBy(g => g.Key)
            .ToList();

        // Reactor doesn't ship a built-in grouped-list control; instead,
        // compose a VStack of header + items per group. The render
        // function for each group hands back its own typed collection,
        // so virtualization still applies inside each section if you
        // swap LazyVStack for ListView.
        return VStack(8,
            SubHeading($"Grouped: {grouped.Count} sections"),
            ScrollView(
                VStack(16,
                    ForEach(grouped, group =>
                        VStack(4,
                            TextBlock($"— {group.Key} —").Bold()
                                .Opacity(0.7),
                            ForEach(group.ToArray(), c =>
                                HStack(8,
                                    TextBlock(c.Name).Bold(),
                                    TextBlock(c.Email).Opacity(0.6))
                                    .WithKey(c.Id))
                        ).WithKey($"group-{group.Key}"))
                ).Padding(8)
            ).Height(300)
        ).Padding(24);
    }
}
// </snippet:grouping>

// <snippet:drag-reorder>
class DragReorderDemo : Component
{
    public override Element Render()
    {
        var (items, setItems) = UseState(
            new List<string> { "Alpha", "Bravo", "Charlie",
                "Delta", "Echo", "Foxtrot" });

        // Reactor surfaces drag-reorder through the underlying WinUI
        // ListView's CanReorderItems / AllowDrop / CanDragItems. The
        // .Set passthrough is the supported escape hatch until a
        // first-class fluent ships. The user's reorder is mirrored
        // back into state via the ListView's reorder event.
        return VStack(8,
            SubHeading("Drag to reorder"),
            ListView<string>(
                items,
                s => s,
                (item, _) =>
                    HStack(8,
                        TextBlock("☰").Opacity(0.4),
                        TextBlock(item).Bold()
                    ).Padding(8))
                .Set(lv =>
                {
                    lv.CanReorderItems = true;
                    lv.AllowDrop = true;
                    lv.CanDragItems = true;
                })
                .Height(260)
        ).Padding(24);
    }
}
// </snippet:drag-reorder>

// <snippet:lazy-loading>
class LazyLoadingDemo : Component
{
    public override Element Render()
    {
        // Pretend "loaded" up to a high-water mark; new items fetch
        // when the visible range crosses into unloaded territory.
        var (loadedTo, setLoadedTo) = UseState(50);
        var totalCount = 1_000;

        return VStack(8,
            SubHeading($"Lazy-load — fetched {loadedTo} of {totalCount}"),
            VirtualList(
                itemCount: totalCount,
                renderItem: index =>
                    index < loadedTo
                        ? HStack(8,
                            TextBlock($"{index + 1}.").Width(50),
                            TextBlock($"Row {index + 1}").Bold(),
                            TextBlock($"loaded").Opacity(0.6))
                            .Padding(8)
                        // Skeleton for not-yet-loaded indices.
                        : HStack(8,
                            TextBlock($"{index + 1}.").Width(50),
                            TextBlock("loading…").Opacity(0.4))
                            .Padding(8),
                getItemKey: index => $"lazy-{index}",
                itemHeight: 40,
                // Watcher fires whenever the visible range changes —
                // bump the high-water mark when the bottom passes the
                // current limit.
                onVisibleRangeChanged: (first, last) =>
                {
                    if (last >= loadedTo - 5 && loadedTo < totalCount)
                        setLoadedTo(Math.Min(loadedTo + 50, totalCount));
                }
            ).Height(300)
        ).Padding(24);
    }
}
// </snippet:lazy-loading>

class CollectionsApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Collections"),
                Component<ListViewDemo>(),
                Component<LazyVStackDemo>(),
                Component<GridViewDemo>(),
                Component<ForEachDemo>(),
                Component<MultiSelectDemo>(),
                Component<WithKeyDemo>(),
                Component<GroupingDemo>(),
                Component<DragReorderDemo>(),
                Component<LazyLoadingDemo>()
            ).Padding(24)
        );
    }
}
