using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static Reactor.Controls.Factories; // the consumable first-class control library

// Demonstrates the CONSUMABLE first-class Reactor TableView control
// (Reactor.Controls.TableView) — i.e. exactly what a Reactor consumer writes in
// their own app after referencing the library:
//
//     using static Reactor.Controls.Factories;
//     TableView(items, columns, height: 420)
//
// This tab drives the native control through a real Reactor element + handler
// (typed columns, reactive ItemsSource, pooled control instance) — rather than the
// raw XamlHostElement escape hatch.
class TableViewControlDemo : Component
{
    sealed record DirectoryPerson(string Name, int Age, string City, string Role);

    static readonly List<DirectoryPerson> People = new()
    {
        new("Ada Lovelace", 36, "London", "Mathematician"),
        new("Alan Turing", 41, "Maida Vale", "Computer Scientist"),
        new("Grace Hopper", 85, "New York", "Rear Admiral"),
        new("Katherine Johnson", 101, "Hampton", "Mathematician"),
        new("Margaret Hamilton", 88, "Paoli", "Software Engineer"),
        new("Dennis Ritchie", 70, "Bronxville", "Computer Scientist"),
        new("Barbara Liskov", 85, "Los Angeles", "Computer Scientist"),
        new("Donald Knuth", 86, "Milwaukee", "Author"),
        new("Tim Berners-Lee", 69, "London", "Inventor"),
        new("Linus Torvalds", 54, "Helsinki", "Engineer"),
        new("Margaret Heafield", 63, "Indianapolis", "Director"),
        new("Radia Perlman", 73, "Portsmouth", "Engineer"),
    };

    static readonly List<Reactor.Controls.TableColumn> Columns = new()
    {
        new("Name", nameof(DirectoryPerson.Name)),
        new("Age", nameof(DirectoryPerson.Age)),
        new("City", nameof(DirectoryPerson.City)),
        new("Role", nameof(DirectoryPerson.Role)),
    };

    public override Element Render() =>
        VStack(12,
            Heading("TableView — first-class Reactor control"),
            TextBlock(
                "This is the consumable control exposed by the Reactor.Controls.TableView " +
                "library. A Reactor consumer references the library and writes the typed " +
                "TableView(...) factory directly — no XamlHostElement, no raw interop:"),
            TextBlock("using static Reactor.Controls.Factories;\nTableView(people, columns, height: 420)"),
            TableView(People, Columns, height: 420)
        );
}
