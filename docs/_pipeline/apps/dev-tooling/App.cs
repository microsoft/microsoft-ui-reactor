using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<DevToolingApp>("Dev Tooling Demo", width: 600, height: 450
#if DEBUG
    , preview: true
#endif
);

// <snippet:preview-app>
class DevToolingApp : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (message, setMessage) = UseState("Edit this code and save!");

        return VStack(16,
            Heading("Preview Mode Demo"),
            TextBlock(message).FontSize(16),
            HStack(8,
                Button("Click me", () => setCount(count + 1)),
                TextBlock($"Clicked {count} times").SemiBold()
            ),
            TextBox(message, setMessage, placeholder: "Type something")
                .Width(300)
        ).Padding(24);
    }
}
// </snippet:preview-app>

// <snippet:entry-point>
// Program entry point — this is the entire App.cs file:
// ReactorApp.Run<DevToolingApp>("Dev Tooling Demo",
//     width: 600, height: 450
// #if DEBUG
//     , preview: true
// #endif
// );
//
// The preview flag enables hot reload via dotnet watch.
// In Release builds, preview is omitted entirely.
// </snippet:entry-point>

// <snippet:function-entry>
// Alternative: inline function component, no class needed
// ReactorApp.Run("Quick Test", ctx =>
// {
//     var (n, setN) = ctx.UseState(0);
//     return VStack(12,
//         TextBlock($"Count: {n}").FontSize(20),
//         Button("+1", () => setN(n + 1))
//     ).Padding(24);
// }, width: 400, height: 300);
// </snippet:function-entry>

// <snippet:iteration-demo>
class IterationDemo : Component
{
    public override Element Render()
    {
        var (items, updateItems) = UseReducer(new List<string>());
        var (input, setInput) = UseState("");

        return VStack(12,
            Heading("Iteration Cycle Demo"),
            TextBlock("Add items, then edit this code and save to see hot reload."),
            HStack(8,
                TextBox(input, setInput, placeholder: "New item")
                    .Width(200),
                Button("Add", () =>
                {
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        updateItems(list =>
                        {
                            var next = new List<string>(list) { input };
                            return next;
                        });
                        setInput("");
                    }
                })
            ),
            ForEach(items, item => TextBlock($"  - {item}"))
        ).Padding(24);
    }
}
// </snippet:iteration-demo>
