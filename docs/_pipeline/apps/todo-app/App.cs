// <snippet:todo-app>
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<TodoApp>("Todo App", width: 550, height: 600);

class TodoApp : Component
{
    public override Element Render()
    {
        var initialItems = UseMemo(() => new List<TodoItem>
        {
            new("todo-1", "Learn Reactor basics", true),
            new("todo-2", "Build a todo app", false),
            new("todo-3", "Explore hooks", false),
        });
        var (items, updateItems) = UseReducer(initialItems);
        var (newText, setNewText) = UseState("");
        var (nextId, setNextId) = UseState(4);

        var doneCount = items.Count(i => i.Done);

        return VStack(16,
            Heading("Todo List"),
            TextBlock($"{doneCount}/{items.Count} completed").Opacity(0.6),

            // Input row
            HStack(8,
                TextBox(newText, setNewText, placeholderText: "What needs to be done?")
                    .AutomationName("New todo")
                    .Width(300),
                Button("Add", () =>
                {
                    if (!string.IsNullOrWhiteSpace(newText))
                    {
                        var text = newText.Trim();
                        updateItems(list => [.. list, new TodoItem($"todo-{nextId}", text, false)]);
                        setNextId(nextId + 1);
                        setNewText("");
                    }
                }).IsEnabled(!(string.IsNullOrWhiteSpace(newText)))
            ),

            // Item list
            VStack(4,
                items.Select((item, _) =>
                    HStack(8,
                        CheckBox(item.Done, done =>
                            updateItems(list =>
                            {
                                var copy = new List<TodoItem>(list);
                                var itemIndex = copy.FindIndex(i => i.Id == item.Id);
                                if (itemIndex >= 0)
                                    copy[itemIndex] = item with { Done = done };
                                return copy;
                            }),
                            label: item.Text
                        ),
                        Button("Remove", () =>
                            updateItems(list =>
                            {
                                var copy = new List<TodoItem>(list);
                                copy.RemoveAll(i => i.Id == item.Id);
                                return copy;
                            })
                        ).AutomationName($"Remove {item.Text}")
                    ).WithKey(item.Id)
                ).ToArray()
            ),

            // Clear completed button
            When(doneCount > 0, () =>
                Button($"Clear completed ({doneCount})", () =>
                    updateItems(list => list.Where(i => !i.Done).ToList())
                ).AutomationName("Clear completed todos")
            )
        ).Padding(24);
    }
}
// </snippet:todo-app>

// <snippet:todo-record>
record TodoItem(string Id, string Text, bool Done);
// </snippet:todo-record>
