// Reactor.Advanced TableView sample — the WinUI Microsoft.UI.Xaml.Controls.Tabular.TableView
// (Windows App SDK experimental channel), MVU-style: the rows are immutable records held in
// component state, edits come back through callbacks that produce a new snapshot, and the
// table diffs each snapshot into the rows it shows. Also: a Reactor-rendered template column
// with a typed sort, filtering, grouping, density and selection.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Advanced.Tabular;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Tabular;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;

ReactorApp.Run<TableViewDemo>("Reactor TableView", width: 900, height: 640);

record Person(int Id, string Name, string Team, int Score);

class TableViewDemo : Component
{
    private static readonly string[] Densities = ["Compact", "Standard", "Comfortable"];

    private static readonly Person[] InitialPeople =
    [
        new(1, "Ada Lovelace", "Research", 97),
        new(2, "Grace Hopper", "Compilers", 92),
        new(3, "Alan Turing", "Research", 99),
        new(4, "Margaret Hamilton", "Flight", 95),
        new(5, "Linus Torvalds", "Kernel", 88),
        new(6, "Barbara Liskov", "Compilers", 94),
        new(7, "Katherine Johnson", "Flight", 96),
        new(8, "Ken Thompson", "Kernel", 90),
    ];

    public override Element Render()
    {
        var (people, setPeople) = UseState(InitialPeople);
        var (search, setSearch) = UseState("");
        var (grouped, setGrouped) = UseState(false);
        var (density, setDensity) = UseState(1);
        var (selected, setSelected) = UseState<Person?>(null);

        // An edit is just a state update: replace the row with a modified copy.
        void Update(Person person, Func<Person, Person> change) =>
            setPeople([.. people.Select(p => p.Id == person.Id ? change(p) : p)]);

        // FilterRows/GroupRows re-apply whenever the delegate changes, so memoize on their inputs.
        var filter = UseMemo<Func<Person, bool>?>(
            () => search.Length == 0 ? null : p => p.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase),
            search);

        var table = TableView(people,
                TextColumn<Person>("Name", p => p.Name, (p, name) => Update(p, x => x with { Name = name }), width: 220),
                TextColumn<Person>("Team", p => p.Team, (p, team) => Update(p, x => x with { Team = team }), width: 140),
                TemplateColumn<Person>("Score", p => HStack(8,
                        Progress(p.Score).Width(120).VAlign(VerticalAlignment.Center),
                        TextBlock($"{p.Score}").VAlign(VerticalAlignment.Center))
                    .Margin(12, 0))
                    .SortBy<Person>((a, b) => a.Score.CompareTo(b.Score))
                    .Star())
            .KeyRows<Person>(p => p.Id)
            .SelectionMode(TableViewSelectionMode.Single)
            .SelectionChanged((_, item) => setSelected(item as Person))
            .Density((TableViewDensity)density)
            .FilterRows(filter)
            .GroupRows<Person>(grouped ? p => p.Team : null)
            with
        {
            CanUserSortColumns = true,
            EmptyContent = TextBlock("No one matches that search.").Margin(16),
        };

        var status = selected is not null
            ? $"Selected: {selected.Name} — double-click a Name or Team cell to edit it."
            : "Click a row to select it; click a header to sort.";

        return VStack(12,
            Heading("TableView"),
            HStack(16,
                TextBox(search, setSearch, placeholderText: "Filter by name").Width(240),
                ToggleSwitch(grouped, setGrouped, header: "Group by team"),
                ComboBox(Densities, density, setDensity).Width(160)),
            table.Height(420),
            Caption(status)
        ).Padding(24);
    }
}
