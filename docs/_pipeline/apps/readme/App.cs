using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<ReadmeShowcase>("Reactor Showcase", width: 600, height: 500
#if DEBUG
    , preview: true
#endif
);

// <snippet:hello-world>
class HelloWorld : Component
{
    public override Element Render()
    {
        return VStack(12,
            TextBlock("Hello from Reactor!").FontSize(24).Bold(),
            TextBlock("No XAML. No data binding. Just C#.")
        ).Padding(24);
    }
}
// </snippet:hello-world>

// <snippet:counter>
class QuickCounter : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return HStack(8,
            Button("- 1", () => setCount(count - 1)),
            TextBlock($"{count}").FontSize(20).SemiBold().Width(40)
                .HAlign(HorizontalAlignment.Center),
            Button("+ 1", () => setCount(count + 1))
        ).Padding(24);
    }
}
// </snippet:counter>

// <snippet:styled-text>
class StyledText : Component
{
    public override Element Render()
    {
        return VStack(8,
            Heading("Heading element"),
            SubHeading("SubHeading element"),
            TextBlock("Regular text with modifiers")
                .FontSize(14).Foreground("#0078D4"),
            Caption("Caption for fine print")
        ).Padding(24);
    }
}
// </snippet:styled-text>

// <snippet:showcase>
class ReadmeShowcase : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                TextBlock("Reactor Framework").FontSize(28).Bold(),
                TextBlock("Declarative UI for native Windows apps"),
                Func(ctx =>
                {
                    var (name, setName) = ctx.UseState("World");
                    return VStack(8,
                        SubHeading("Interactive greeting"),
                        TextBlock($"Hello, {name}!").FontSize(18),
                        TextBox(name, setName, placeholder: "Your name")
                            .Width(250)
                    );
                }),
                Func(ctx =>
                {
                    var (n, setN) = ctx.UseState(0);
                    return HStack(8,
                        SubHeading("Counter:"),
                        Button("-", () => setN(n - 1)),
                        TextBlock($"{n}").FontSize(18).SemiBold(),
                        Button("+", () => setN(n + 1))
                    );
                })
            ).Padding(32)
        );
    }
}
// </snippet:showcase>
