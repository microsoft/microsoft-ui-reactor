using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

class DataTemplateDemo : Component
{
    record Animal(int Id, string Name, string Species, string Emoji);

    // Heterogeneous tree model: group rows and animal rows are distinct C#
    // shapes, so the TreeView<T> viewBuilder renders each differently and the
    // childrenSelector reads hierarchy off the shape (see section 4).
    abstract record PetNode : IReactorKeyed { public abstract string Key { get; } }
    sealed record PetGroup(string Label, string Glyph, IReadOnlyList<PetNode> Items) : PetNode
    {
        public override string Key => "group:" + Label;
    }
    sealed record PetLeaf(Animal Animal) : PetNode
    {
        public override string Key => "animal:" + Animal.Id;
    }

    static string EmojiFor(string species) => species switch
    {
        "Cat" => "\U0001F431",
        "Dog" => "\U0001F436",
        "Rabbit" => "\U0001F430",
        "Hamster" => "\U0001F439",
        "Parrot" => "\U0001F99C",
        _ => "\U0001F43E",
    };

    static IReadOnlyList<PetNode> BuildPetTree(IReadOnlyList<Animal> animals)
    {
        var groups = new[] { "Cat", "Dog", "Rabbit", "Hamster", "Parrot" }
            .Where(species => animals.Any(a => a.Species == species))
            .Select(species => (PetNode)new PetGroup(species, EmojiFor(species),
                animals.Where(a => a.Species == species)
                       .Select(a => (PetNode)new PetLeaf(a)).ToList()))
            .ToList();
        return [new PetGroup("All Pets", "\U0001F3E0", groups)];
    }

    static readonly List<Animal> AllAnimals =
    [
        new(1, "Luna", "Cat", "\U0001F431"),
        new(2, "Max", "Dog", "\U0001F436"),
        new(3, "Bella", "Cat", "\U0001F431"),
        new(4, "Charlie", "Dog", "\U0001F436"),
        new(5, "Oliver", "Rabbit", "\U0001F430"),
        new(6, "Lucy", "Cat", "\U0001F431"),
        new(7, "Buddy", "Dog", "\U0001F436"),
        new(8, "Daisy", "Hamster", "\U0001F439"),
        new(9, "Rocky", "Dog", "\U0001F436"),
        new(10, "Coco", "Parrot", "\U0001F99C"),
    ];

    public override Element Render()
    {
        var (animals, updateAnimals) = UseReducer(AllAnimals);
        var (selectedListIndex, setSelectedListIndex) = UseState(-1);
        var (selectedGridIndex, setSelectedGridIndex) = UseState(-1);
        var (flipIndex, setFlipIndex) = UseState(0);
        var (filter, setFilter) = UseState("");

        var filtered = string.IsNullOrWhiteSpace(filter)
            ? animals
            : animals.Where(a => a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                 || a.Species.Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .ToList();

        return ScrollView(VStack(16,
            Heading("DataTemplate Demo"),
            TextBlock("Typed ListView<T>, GridView<T>, FlipView<T>, and TreeView<T> — all data-driven via a viewBuilder."),

            // Filter + add/remove controls
            HStack(12,
                TextBox(filter, setFilter, placeholderText: "Filter animals...").Width(200),
                Button("Add Random", () => updateAnimals(list =>
                {
                    var id = list.Count + 1;
                    var species = new[] { "Cat", "Dog", "Rabbit", "Hamster", "Parrot" };
                    var emojis = new[] { "\U0001F431", "\U0001F436", "\U0001F430", "\U0001F439", "\U0001F99C" };
                    var rng = new Random();
                    var si = rng.Next(species.Length);
                    return [.. list, new Animal(id, $"Pet #{id}", species[si], emojis[si])];
                })),
                Button("Remove Last", () => updateAnimals(list =>
                    list.Count > 0 ? list.Take(list.Count - 1).ToList() : list
                )).IsEnabled(!(animals.Count == 0))
            ),

            TextBlock($"{filtered.Count} animals shown").Foreground(SecondaryText),

            // 1. Typed ListView<T>
            SubHeading("1. Typed ListView<T>"),
            TextBlock("Each row uses pattern matching on Species for a DataTemplateSelector-equivalent."),
            Border(
                ListView<Animal>(
                    filtered,
                    a => a.Id.ToString(),
                    (animal, i) => HStack(12,
                        TextBlock(animal.Emoji).FontSize(24),
                        VStack(2,
                            TextBlock(animal.Name).SemiBold(),
                            animal.Species switch
                            {
                                "Cat" => Caption($"Feline - {animal.Species}").Foreground(SecondaryText),
                                "Dog" => Caption($"Canine - {animal.Species}").Foreground(SecondaryText),
                                _ => Caption(animal.Species).Foreground(TertiaryText),
                            }
                        ),
                        Caption($"#{animal.Id}").Foreground(TertiaryText)
                    ).Margin(4)
                ) with
                {
                    SelectedIndex = selectedListIndex,
                    OnSelectedIndexChanged = setSelectedListIndex,
                    OnItemClick = a => setSelectedListIndex(filtered.IndexOf(a)),
                    Header = "Animals"
                }
            ).CornerRadius(8).Height(250),

            When(selectedListIndex >= 0 && selectedListIndex < filtered.Count,
                () => TextBlock($"Selected: {filtered[Math.Min(selectedListIndex, filtered.Count - 1)].Name}").SemiBold()),

            // 2. Typed GridView<T>
            SubHeading("2. Typed GridView<T>"),
            TextBlock("Card layout with per-species colors."),
            Border(
                GridView<Animal>(
                    filtered,
                    a => a.Id.ToString(),
                    (animal, i) =>
                    {
                        var bg = animal.Species switch
                        {
                            "Cat" => "#fff3e0",
                            "Dog" => "#e3f2fd",
                            "Rabbit" => "#f3e5f5",
                            "Hamster" => "#fff9c4",
                            "Parrot" => "#e8f5e9",
                            _ => "#f5f5f5"
                        };
                        return Border(
                            VStack(4,
                                TextBlock(animal.Emoji).FontSize(32).HAlign(HorizontalAlignment.Center),
                                TextBlock(animal.Name).SemiBold().HAlign(HorizontalAlignment.Center),
                                Caption(animal.Species).Foreground(SecondaryText).HAlign(HorizontalAlignment.Center)
                            )
                        ).CornerRadius(8).Background(bg).Padding(12).Width(120).Height(120);
                    }
                ) with
                {
                    SelectedIndex = selectedGridIndex,
                    OnSelectedIndexChanged = setSelectedGridIndex,
                    Header = "Gallery"
                }
            ).CornerRadius(8).Height(300),

            // 3. Typed FlipView<T>
            SubHeading("3. Typed FlipView<T>"),
            TextBlock("Swipe through animal cards."),
            Border(
                FlipView<Animal>(
                    filtered,
                    a => a.Id.ToString(),
                    (animal, i) => Border(
                        VStack(12,
                            TextBlock(animal.Emoji).FontSize(64).HAlign(HorizontalAlignment.Center),
                            TextBlock(animal.Name).FontSize(24).SemiBold().HAlign(HorizontalAlignment.Center),
                            TextBlock($"{animal.Species} (#{animal.Id})").Foreground(SecondaryText).HAlign(HorizontalAlignment.Center)
                        ).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)
                    ).Background(SubtleFill).Padding(32)
                ) with
                {
                    SelectedIndex = flipIndex,
                    OnSelectedIndexChanged = setFlipIndex,
                }
            ).CornerRadius(8).Height(250).Width(400),

            TextBlock($"Showing {flipIndex + 1} of {filtered.Count}").Foreground(SecondaryText),

            // 4. TreeView<T> — heterogeneous nodes from the C# data shape
            SubHeading("4. TreeView<T> (heterogeneous nodes)"),
            TextBlock("Hierarchy (childrenSelector) and per-node template (viewBuilder) both come from the data shape — group rows and animal rows render differently."),
            Border(
                TreeView<PetNode>(
                    items: BuildPetTree(filtered),
                    childrenSelector: n => n is PetGroup g ? g.Items : null,
                    viewBuilder: n => n switch
                    {
                        PetGroup g => HStack(8,
                            TextBlock(g.Glyph).FontSize(16),
                            TextBlock(g.Label).SemiBold(),
                            Caption($"({g.Items.Count})").Foreground(TertiaryText)),
                        PetLeaf l => HStack(8,
                            TextBlock(l.Animal.Emoji),
                            TextBlock(l.Animal.Name),
                            Caption($"#{l.Animal.Id}").Foreground(TertiaryText)),
                        _ => TextBlock(n.Key),
                    }) with { IsExpanded = _ => true }
            ).CornerRadius(8).Height(300).Margin(5)
        ));
    }
}
