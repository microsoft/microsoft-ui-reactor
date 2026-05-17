using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<MasterDetailApp>("Master-Detail Recipe", width: 640, height: 360
#if DEBUG
    , preview: true
#endif
);

class MasterDetailApp : Component
{
    public override Element Render() => Component<NoteBrowser>();
}

// <snippet:data>
record Note(int Id, string Title, string Body);

class NoteBrowser : Component
{
    private static readonly Note[] Notes = new[] {
        new Note(1, "Project plan", "Draft the milestone sequence; ship before Friday."),
        new Note(2, "Grocery list", "Bread, olive oil, lemons, parsley, two limes."),
        new Note(3, "Bug triage",   "Refocus on the persistence regression; defer the WinForms host."),
    };
// </snippet:data>

    public override Element Render()
    {
        // <snippet:selection>
        // Single source of truth for "which note is selected" — the list
        // writes to it via the button click; the detail pane reads from it.
        // Re-renders are scoped to slots that actually changed.
        var (selectedId, setSelectedId) = UseState<int?>(1);
        var selected = Notes.FirstOrDefault(n => n.Id == selectedId);
        // </snippet:selection>

        // <snippet:layout>
        var list = VStack(2,
            Notes.Select(n =>
                Button(n.Title, () => setSelectedId(n.Id))
                    .HAlign(Microsoft.UI.Xaml.HorizontalAlignment.Stretch)
                    .Background(n.Id == selectedId ? "#E5F1FB" : "#FFFFFF")
            ).ToArray()
        ).Width(200).Padding(8);

        Element detail = selected is null
            ? TextBlock("No selection").Opacity(0.6).Padding(20)
            : VStack(8,
                Heading(selected.Title),
                TextBlock(selected.Body).Opacity(0.8)
            ).Padding(20);

        return HStack(0, list, detail);
        // </snippet:layout>
    }
}
