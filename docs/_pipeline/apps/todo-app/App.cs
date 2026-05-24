// <snippet:todo-app>
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<TodoApp>("Todo App", width: 550, height: 600, devtools: true);

class TodoApp : Component
{
    public override Element Render()
    {
        var (items, updateItems) = UseReducer(new List<TodoItem>
        {
            new("Learn Reactor basics", true),
            new("Build a todo app", false),
            new("Explore hooks", false),
        });
        var (newText, setNewText) = UseState("");

        var doneCount = items.Count(i => i.Done);

        return VStack(16,
            Heading("Todo List"),
            TextBlock($"{doneCount}/{items.Count} completed").Opacity(0.6),

            // Input row
            HStack(8,
                TextBox(newText, setNewText, placeholderText: "What needs to be done?")
                    .Width(300),
                Button("Add", () =>
                {
                    if (!string.IsNullOrWhiteSpace(newText))
                    {
                        updateItems(list => [.. list, new TodoItem(newText.Trim(), false)]);
                        setNewText("");
                    }
                }).IsEnabled(!(string.IsNullOrWhiteSpace(newText)))
            ),

            // Item list
            VStack(4,
                items.Select((item, index) =>
                    HStack(8,
                        CheckBox(item.Done, done =>
                            updateItems(list =>
                            {
                                var copy = new List<TodoItem>(list);
                                copy[index] = item with { Done = done };
                                return copy;
                            }),
                            label: item.Text
                        ),
                        Button("Remove", () =>
                            updateItems(list =>
                            {
                                var copy = new List<TodoItem>(list);
                                copy.RemoveAt(index);
                                return copy;
                            })
                        )
                    ).WithKey($"todo-{index}")
                ).ToArray()
            ),

            // Clear completed button
            When(doneCount > 0, () =>
                Button($"Clear completed ({doneCount})", () =>
                    updateItems(list => list.Where(i => !i.Done).ToList())
                )
            )
        ).Padding(24);
    }
}
// </snippet:todo-app>

// <snippet:todo-record>
record TodoItem(string Text, bool Done);
// </snippet:todo-record>
