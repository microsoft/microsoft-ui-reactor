using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

// <snippet:entry-point>
ReactorApp.Run<NotesApp>("Notes", width: 600, height: 520);
// </snippet:entry-point>

// <snippet:state-reducer>
record Note(string Id, string Text);

record NotesState(IReadOnlyList<Note> Notes, string Draft)
{
    public static readonly NotesState Initial = new([], "");
}

abstract record NotesAction;
record SetDraft(string Text) : NotesAction;
record AddNote : NotesAction;
record DeleteNote(string Id) : NotesAction;

static class NotesReducer
{
    public static NotesState Reduce(NotesState state, NotesAction action) => action switch
    {
        SetDraft d => state with { Draft = d.Text },
        AddNote when !string.IsNullOrWhiteSpace(state.Draft) => state with
        {
            Notes = [.. state.Notes, new(Guid.NewGuid().ToString(), state.Draft.Trim())],
            Draft = "",
        },
        DeleteNote d => state with { Notes = [.. state.Notes.Where(n => n.Id != d.Id)] },
        _ => state,
    };
}
// </snippet:state-reducer>

// <snippet:root-component>
class NotesApp : Component
{
    public override Element Render()
    {
        var (state, dispatch) = UseReducer<NotesState, NotesAction>(
            NotesReducer.Reduce, NotesState.Initial);

        return VStack(12,
            HStack(8,
                TextBox(state.Draft, text => dispatch(new SetDraft(text)),
                        placeholderText: "New note")
                    .AutomationName("New note")
                    .Width(320),
                Button("Add", () => dispatch(new AddNote()))
            ),
            Component<NoteList, NoteListProps>(
                new(state.Notes, id => dispatch(new DeleteNote(id))))
        ).Padding(16);
    }
}
// </snippet:root-component>

// <snippet:child-component>
record NoteListProps(IReadOnlyList<Note> Notes, Action<string> OnDelete);

class NoteList : Component<NoteListProps>
{
    public override Element Render() =>
        Props.Notes.Count == 0
            ? TextBlock("No notes yet")
            : VStack(4, ForEach(Props.Notes, note =>
                HStack(8,
                    TextBlock(note.Text).VAlign(VerticalAlignment.Center),
                    Button("Delete", () => Props.OnDelete(note.Id))
                        .AutomationName($"Delete {note.Text}")
                ).WithKey(note.Id)));
}
// </snippet:child-component>
