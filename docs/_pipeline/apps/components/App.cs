using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<ComponentsApp>("Components Demo", width: 650, height: 550
#if DEBUG
    , preview: true
#endif
);

// <snippet:basic-component>
class Greeting : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("World");

        return VStack(12,
            TextBlock($"Hello, {name}!").FontSize(20).Bold(),
            TextBox(name, setName, placeholderText: "Your name")
                .Width(200)
        ).Padding(16);
    }
}
// </snippet:basic-component>

// <snippet:props-record>
record AlertProps(string Title, string Message, string Severity = "info");
// </snippet:props-record>

// <snippet:props-component>
class Alert : Component<AlertProps>
{
    public override Element Render()
    {
        var bg = Props.Severity switch
        {
            "error" => "#FDE7E9",
            "warning" => "#FFF4CE",
            _ => "#DFF6DD"
        };

        return Border(
            VStack(4,
                TextBlock(Props.Title).Bold(),
                TextBlock(Props.Message)
            ).Padding(12)
        ).Background(bg).CornerRadius(4);
    }
}
// </snippet:props-component>

// <snippet:should-update>
record ExpensiveProps(string Label, int Value);

class ExpensiveDisplay : Component<ExpensiveProps>
{
    protected override bool ShouldUpdate(
        ExpensiveProps? oldProps, ExpensiveProps? newProps)
    {
        // Only re-render when the Value changes, ignore Label
        return oldProps?.Value != newProps?.Value;
    }

    public override Element Render()
    {
        return TextBlock($"Value: {Props.Value}").FontSize(18).Bold();
    }
}
// </snippet:should-update>

// <snippet:function-component>
class FunctionComponentDemo : Component
{
    public override Element Render()
    {
        return VStack(12,
            SubHeading("Function components"),
            // Memo: render once + own state changes (the common case).
            Memo(ctx =>
            {
                var (on, setOn) = ctx.UseState(false);
                return HStack(8,
                    ToggleSwitch(on, setOn),
                    TextBlock(on ? "Active" : "Inactive")
                );
            }),
            // Memo with a dep: skip re-render when deps haven't changed.
            Memo(ctx =>
            {
                return TextBlock("I only re-render when deps change")
                    .Opacity(0.6);
            }, "stable-dep")
        ).Padding(16);
    }
}
// </snippet:function-component>

// <snippet:factory-helpers>
static class Components
{
    public static ComponentElement Alert(string title, string message,
        string severity = "info") =>
        Component<global::Alert, AlertProps>(new(title, message, severity));
}
// </snippet:factory-helpers>

// <snippet:composition>
class ComponentsApp : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return ScrollView(
            VStack(16,
                Heading("Component Patterns"),
                Component<Greeting>(),
                Component<Alert, AlertProps>(new("Success", "It works!")),
                Component<Alert, AlertProps>(new("Oops", "Something broke",
                    "error")),
                HStack(8,
                    Button("+1", () => setCount(count + 1)),
                    Component<ExpensiveDisplay, ExpensiveProps>(
                        new("Counter", count))
                ),
                Component<FunctionComponentDemo>()
            ).Padding(24)
        );
    }
}
// </snippet:composition>
