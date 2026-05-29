// id: list-add-delete-toggle
// intent: dynamic list with add, delete, and toggle using UseReducer
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using System.Collections.Immutable;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Todo List", width: 500, height: 400);

record TodoItem(string Id, string Text, bool Done);
record TodoState(ImmutableList<TodoItem> Items);
abstract record TodoAction;
record AddAction(string Text) : TodoAction;
record ToggleAction(string Id) : TodoAction;
record DeleteAction(string Id) : TodoAction;

class App : Component
{
    private static TodoState Reduce(TodoState state, TodoAction action) => action switch
    {
        AddAction add when !string.IsNullOrWhiteSpace(add.Text)
            => state with { Items = state.Items.Add(new TodoItem(System.Guid.NewGuid().ToString(), add.Text.Trim(), false)) },
        ToggleAction toggle
            => state with { Items = state.Items.Select(item => item.Id == toggle.Id ? item with { Done = !item.Done } : item).ToImmutableList() },
        DeleteAction delete
            => state with { Items = state.Items.RemoveAll(item => item.Id == delete.Id) },
        _ => state,
    };

    public override Element Render()
    {
        var (state, dispatch) = UseReducer<TodoState, TodoAction>(Reduce,
            new TodoState(ImmutableList.Create(new TodoItem("1", "Write docs", false), new TodoItem("2", "Ship sample", true))));
        var (draft, setDraft) = UseState("");

        return VStack(12,
            Heading($"Todo list ({state.Items.Count(item => item.Done)}/{state.Items.Count})"),
            HStack(8,
                TextField(draft, setDraft, placeholder: "Add an item").Width(300),
                Button("Add", () => { dispatch(new AddAction(draft)); setDraft(""); }).IsEnabled(!string.IsNullOrWhiteSpace(draft))),
            VStack(8,
                ForEach(state.Items, item =>
                    HStack(8,
                        CheckBox(item.Done, _ => dispatch(new ToggleAction(item.Id))),
                        TextBlock(item.Text).Width(240).Opacity(item.Done ? 0.5 : 1.0),
                        Button("Delete", () => dispatch(new DeleteAction(item.Id))))
                    .WithKey(item.Id))))
            .Padding(16);
    }
}
