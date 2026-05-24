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

class TodoDemo : Component
{
    record TodoItem(string Text, bool Done);

    public override Element Render()
    {
        var (items, updateItems) = UseReducer(new List<TodoItem>
        {
            new("Build Reactor library", true),
            new("Write test app", true),
            new("Add more features", false),
        });
        var (newText, setNewText) = UseState("");

        var doneCount = items.Count(i => i.Done);
        var totalCount = items.Count;

        return VStack(12,
            Heading("Todo List"),
            TextBlock($"{doneCount}/{totalCount} completed"),

            // Add new item
            HStack(8,
                TextBox(newText, setNewText, placeholderText: "What needs to be done?").Width(300),
                Button("Add", () =>
                {
                    if (!string.IsNullOrWhiteSpace(newText))
                    {
                        updateItems(list => [.. list, new TodoItem(newText.Trim(), false)]);
                        setNewText("");
                    }
                }).IsEnabled(!(string.IsNullOrWhiteSpace(newText)))
            ),

            // List of items
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
                        Button("\u00d7", () =>
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

            // Conditional: show "All done!" when everything is checked
            When(totalCount > 0 && doneCount == totalCount,
                () => TextBlock("All done! \U0001f389").SemiBold())
        );
    }
}
