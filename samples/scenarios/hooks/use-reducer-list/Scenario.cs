// id: use-reducer-list
// intent: manage todo items with immutable add, toggle, and delete reducer actions
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// UseReducer keeps list updates centralized and immutable.
ReactorApp.Run<App>("UseReducerList", width: 400, height: 200);

class App : Component
{
    record Item(string Id, string Text, bool Done);
    abstract record TodoAction;
    record AddItem(string Text) : TodoAction;
    record ToggleItem(string Id) : TodoAction;
    record DeleteItem(string Id) : TodoAction;

    static IReadOnlyList<Item> Reduce(IReadOnlyList<Item> state, TodoAction action) => action switch
    {
        AddItem { Text: var text } when !string.IsNullOrWhiteSpace(text)
            => [.. state, new Item(Guid.NewGuid().ToString("N"), text.Trim(), false)],
        ToggleItem { Id: var id }
            => state.Select(item => item.Id == id ? item with { Done = !item.Done } : item).ToArray(),
        DeleteItem { Id: var id }
            => state.Where(item => item.Id != id).ToArray(),
        _ => state
    };

    public override Element Render()
    {
        var (draft, setDraft) = UseState("");
        var (items, dispatch) = UseReducer<IReadOnlyList<Item>, TodoAction>(Reduce, Array.Empty<Item>());

        return VStack(12,
            Heading("Todo reducer"),
            HStack(8,
                TextBox(draft, setDraft, "Add a todo", header: "Todo"),
                Button("Add", () => { dispatch(new AddItem(draft)); setDraft(""); })),
            ListView(items, item => item.Id, (item, _) => HStack(8,
                CheckBox(item.Done, isChecked => dispatch(new ToggleItem(item.Id)), item.Text),
                Button("Delete", () => dispatch(new DeleteItem(item.Id))))));
    }
}

