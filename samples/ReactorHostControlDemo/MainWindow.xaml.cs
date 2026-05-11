using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorHostControlDemo;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(900, 600));

        // Mount a Component class into the left panel
        var counterHost = new ReactorHostControl();
        counterHost.Mount(new CounterDemo());
        CounterPanel.Child = counterHost;

        // Mount a function component into the right panel
        var todoHost = new ReactorHostControl();
        todoHost.Mount(ctx =>
        {
            var (items, setItems) = ctx.UseState<List<string>>(["Buy groceries", "Walk the dog"]);
            var (draft, setDraft) = ctx.UseState("");

            return VStack(12,
                TextBlock("Todo List").FontSize(20).Bold().Margin(16, 16, 16, 0),

                HStack(8,
                    TextField(draft, onChanged: setDraft, placeholder: "What needs doing?"),
                    Button("Add", () =>
                    {
                        if (string.IsNullOrWhiteSpace(draft)) return;
                        setItems([.. items, draft.Trim()]);
                        setDraft("");
                    })
                ).Margin(horizontal: 16, vertical: 0),

                VStack(4,
                    items.Select((item, i) =>
                        HStack(8,
                            TextBlock(item).VAlign(VerticalAlignment.Center),
                            Button("x", () => setItems(items.Where((_, j) => j != i).ToList()))
                        ) with { Key = $"todo-{i}-{item}" }
                    ).ToArray()
                ).Margin(16, 8, 16, 16),

                items.Count == 0
                    ? TextBlock("All done!").Foreground("#888").HAlign(HorizontalAlignment.Center).Margin(16)
                    : TextBlock($"{items.Count} item{(items.Count == 1 ? "" : "s")} remaining")
                        .Foreground("#888").HAlign(HorizontalAlignment.Center).Margin(0, 0, 0, 16)
            );
        });
        TodoPanel.Child = todoHost;
    }
}
