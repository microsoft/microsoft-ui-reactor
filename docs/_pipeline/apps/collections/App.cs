using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<CollectionsApp>("Collections", width: 700, height: 600
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
                     .Background(Theme.CardBackground)
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
                    placeholderText: "Index")
                    .AutomationName("Target index"),
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
            ("Primary", Theme.Accent), ("Secondary", Theme.AccentSecondary),
            ("Tertiary", Theme.AccentTertiary), ("Subtle", Theme.SubtleFill)
        };

        return VStack(12,
            SubHeading("ForEach (non-virtualized)"),
            HStack(8,
                ForEach(colors, ((string Name, ThemeRef Brush) color) =>
                    Border(
                        TextBlock(color.Name)
                            .Padding(horizontal: 8, vertical: 16)
                    )
                        .Background(color.Brush)
                        .CornerRadius(4)
                        .WithKey(color.Name)
                )
            )
        ).Padding(24);
    }
}
// </snippet:foreach>

class ReconcilerKeyExamples
{
    public record Item(string Id, string Title);
    public record Row(string Id, string Title);

    public static Element KeyedForEach(IEnumerable<Item> items) =>
        // <snippet:keyed-foreach>
        ForEach(items, item => Card(item).WithKey(item.Id));
        // </snippet:keyed-foreach>

    public static Element StableRowKeys(IEnumerable<Row> rows) =>
        // <snippet:stable-row-key>
        // Stable: row identity persists across edits
        ForEach(rows, row => Card(row).WithKey(row.Id));
        // </snippet:stable-row-key>

    private static Element Card(Item item) =>
        Microsoft.UI.Reactor.Factories.Card(TextBlock(item.Title));

    private static Element Card(Row row) =>
        Microsoft.UI.Reactor.Factories.Card(TextBlock(row.Title));
}

// <snippet:multi-select>
class MultiSelectDemo : Component
{
    public override Element Render()
    {
        var contacts = SampleData.Contacts.Take(10).ToList();
        var initialSelectedIds = UseMemo(() => new List<string>());
        var (selectedIds, setSelectedIds) = UseState(initialSelectedIds);

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
    record FruitItem(string Id, string Name);

    public override Element Render()
    {
        var initialItems = UseMemo(() => new List<FruitItem>
        {
            new("fruit-1", "Apple"),
            new("fruit-2", "Banana"),
            new("fruit-3", "Cherry")
        });
        var (items, updateItems) = UseReducer(
            initialItems);
        var (newItem, setNewItem) = UseState("");
        var (nextId, setNextId) = UseState(4);

        return VStack(12,
            SubHeading("Stable Identity with WithKey"),
            HStack(8,
                TextBox(newItem, setNewItem, placeholderText: "New item")
                    .AutomationName("New item"),
                Button("Add", () => {
                    if (!string.IsNullOrWhiteSpace(newItem)) {
                        var name = newItem.Trim();
                        updateItems(l => [.. l, new FruitItem($"fruit-{nextId}", name)]);
                        setNextId(nextId + 1);
                        setNewItem("");
                    }
                })
            ),
            VStack(4, items.Select((item, _) =>
                HStack(8,
                    TextBlock(item.Name),
                    Button("Remove", () => updateItems(
                        l => l.Where(x => x.Id != item.Id).ToList()))
                        .AutomationName($"Remove {item.Name}")
                ).WithKey(item.Id)
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
        var initialItems = UseMemo(() => new List<string> { "Alpha", "Bravo", "Charlie",
            "Delta", "Echo", "Foxtrot" });
        var (items, setItems) = UseState(initialItems);

        // Reactor surfaces drag-reorder through the underlying WinUI
        // ListView's CanReorderItems / AllowDrop / CanDragItems. The
        // .Set passthrough is the supported escape hatch until a
        // first-class fluent ships. The recipe linked below shows
        // how to mirror a drop back into app state.
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

// ── Snippet-only sources for the collections page ──
// These back prose examples that used to be uncompiled `csharp` blocks.
// They are not mounted in CollectionsApp (no screenshot); the point is that
// CI compiles every symbol they name.

// <snippet:reactor-keyed>
record Person(string Id, string Name, string Email) : IReactorKeyed
{
    string IReactorKeyed.Key => Id;
}

static class KeyedUsage
{
    // keySelector is inferred from IReactorKeyed.Key:
    public static Element List(IReadOnlyList<Person> people) =>
        ListView<Person>(people, (person, index) => TextBlock(person.Name));

    public static Element Lazy(IReadOnlyList<Person> people) =>
        LazyVStack<Person>(people, (person, index) => TextBlock(person.Name));

    public static Element Grid(IReadOnlyList<Person> people) =>
        GridView<Person>(people, (person, index) => TextBlock(person.Name));
}
// </snippet:reactor-keyed>

// <snippet:withkey-keyed>
static class HandBuiltKeyedChildren
{
    public static Element Column(IReadOnlyList<Person> people) =>
        FlexColumn(
            people.Select(p =>
                TextBlock(p.Name).WithKey(p)   // identity-on-data
            ).ToArray<Element?>()
        );
}
// </snippet:withkey-keyed>

record Note(string Id, string Title, string Body, int Revision);

class NoteEditor : Component<Note>
{
    public override Element Render()
    {
        var (dirty, setDirty) = UseState(false);
        return HStack(8,
            TextBlock(Props.Title).SemiBold(),
            Button(dirty ? "Dirty" : "Clean", () => setDirty(!dirty))
                .AutomationName($"Mark {Props.Title} {(dirty ? "clean" : "dirty")}")
        );
    }
}

// <snippet:row-state-reset>
static class RowStateReset
{
    // Each row owns edit state. Scrolling row 5 (dirty) onto row 12 must NOT
    // carry the dirty flag — keySelector identity guarantees a fresh mount.
    public static Element Default(IReadOnlyList<Note> notes) =>
        LazyVStack<Note>(notes, n => n.Id, (note, i) =>
            Component<NoteEditor, Note>(note));
}
// </snippet:row-state-reset>

// <snippet:row-state-explicit-key>
static class RowStateExplicitKey
{
    public static Element RemountPerRevision(IReadOnlyList<Note> notes) =>
        LazyVStack<Note>(notes, n => n.Id, (note, i) =>
            Component<NoteEditor, Note>(note)
                .WithKey($"{note.Id}:{note.Revision}")); // remount on every revision
}
// </snippet:row-state-explicit-key>

// <snippet:row-state-constant-key>
static class RowStateConstantKey
{
    // Durable carry-over: a constant key disables the per-item reset, so the
    // recycled control keeps its component state across logical items.
    public static Element Durable(IReadOnlyList<Note> notes) =>
        LazyVStack<Note>(notes, n => n.Id, (note, i) =>
            Component<NoteEditor, Note>(note).WithKey("note-row"));
}
// </snippet:row-state-constant-key>

// <snippet:row-memo>
static class RowMemo
{
    public static Element Rows(IReadOnlyList<Note> notes) =>
        LazyVStack<Note>(notes, n => n.Id, (note, i) =>
            Memo(note.Id, () =>                 // ← key, then the row factory
                Border(
                    VStack(4,
                        TextBlock(note.Title).SemiBold(),
                        Caption(note.Body).Foreground(Theme.SecondaryText)
                    )
                ).Padding(12)));
}
// </snippet:row-memo>

// <snippet:row-memo-tuple>
class RowMemoTupleKey : Component<Note>
{
    Element RowBody(Note note, bool isSelected) =>
        TextBlock(note.Title).SemiBold().Opacity(isSelected ? 1.0 : 0.6);

    public override Element Render()
    {
        var note = Props;
        var (isSelected, _) = UseState(true);
        var isDark = UseIsDarkTheme();

        // Row chrome depends on selection AND theme, so both belong in the key.
        return Memo((note.Id, isSelected, isDark), () => RowBody(note, isSelected));
    }
}
// </snippet:row-memo-tuple>

// <snippet:manual-row-cache>
class ManualRowCache : Component<IReadOnlyList<Note>>
{
    public override Element Render()
    {
        // Held in the parent component via UseRef so it survives re-renders.
        var cache = UseRef(new Dictionary<string, Element>()).Current;

        Element Row(Note note)
        {
            if (!cache.TryGetValue(note.Id, out var el))
                cache[note.Id] = el = Border(TextBlock(note.Title)); // build once per id
            return el;                                              // same instance on reuse
        }

        return LazyVStack<Note>(Props, n => n.Id, (note, i) => Row(note));
    }
}
// </snippet:manual-row-cache>

// <snippet:letter-jump>
class LetterJump : Component<IReadOnlyList<Person>>
{
    static IReadOnlyDictionary<char, int> ComputeStartIndices(
        IReadOnlyList<Person> people) =>
        people
            .Select((p, i) => (Letter: p.Name[0], Index: i))
            .GroupBy(x => x.Letter)
            .ToDictionary(g => g.Key, g => g.First().Index);

    public override Element Render()
    {
        var contacts = Props;
        var listRef = UseRef<VirtualListRef?>(null);
        var groupStarts = UseMemo(() => ComputeStartIndices(contacts), contacts);

        Element RenderRow(int i) => TextBlock(contacts[i].Name).Padding(8);

        return HStack(0,
            VirtualList(contacts.Count, RenderRow,
                getItemKey: i => contacts[i].Id,
                itemHeight: 60,
                @ref: r => listRef.Current = r).Width(360),
            VStack(2,
                ForEach("ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray(), letter =>
                    Button(letter.ToString(), () =>
                    {
                        if (groupStarts.TryGetValue(letter, out var start))
                            listRef.Current?.ScrollToIndex(start);
                    }).AutomationName($"Jump to {letter}")))
        );
    }
}
// </snippet:letter-jump>

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
