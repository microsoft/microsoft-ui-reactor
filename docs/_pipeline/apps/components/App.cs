using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<ComponentsApp>("Components Demo", width: 650, height: 550
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
                .AutomationName("Name")
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
            "error" => Theme.SystemCriticalBackground,
            "warning" => Theme.SystemCautionBackground,
            _ => Theme.SystemSuccessBackground
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

// <snippet:callback-props-naive>
record StepModel(int Id, string Name);

// Re-renders on every parent render — OnChanged is a fresh delegate each time.
record StepPropsNaive(StepModel Step, Action<string> OnChanged);
// </snippet:callback-props-naive>

// <snippet:callback-props-callbacks>
record StepCallbacks(Action<string> OnChanged, Action OnRun);

// Only Step drives re-render now; the callbacks slot is ignored by memo.
record StepProps(StepModel Step, Callbacks<StepCallbacks> Cb);

class StepRow : Component<StepProps>
{
    public override Element Render() =>
        HStack(8,
            TextBlock(Props.Step.Name),
            // Read the callback off Props at event time — never capture it
            // into a local at render time.
            Button("Run", () => Props.Cb.Value.OnRun()));
}

static class StepPropsFactory
{
    public static StepProps Create(StepModel step, Action<string> onChanged, Action onRun) =>
        // Construct it — the payload converts implicitly:
        new StepProps(step, new StepCallbacks(onChanged, onRun));
}
// </snippet:callback-props-callbacks>

// <snippet:composition-children>
record CardProps(string Title, Element Body);

class Card : Component<CardProps>
{
    public override Element Render() =>
        Border(
            VStack(8,
                TextBlock(Props.Title).Bold(),
                Props.Body                 // any Element, including child components
            ).Padding(12)
        ).CornerRadius(8).WithBorder(Theme.CardStroke);
}
// </snippet:composition-children>

// <snippet:render-props>
record ItemsListProps<T>(
    IReadOnlyList<T> Items,
    Func<T, Element> Render,
    Func<T, string> Key);

class ItemsList<T> : Component<ItemsListProps<T>>
{
    public override Element Render() =>
        VStack(4, ForEach(Props.Items,
            item => Props.Render(item).WithKey(Props.Key(item))));
}
// </snippet:render-props>

// <snippet:error-boundary-composition>
class RiskyView : Component
{
    public override Element Render() => TextBlock("Risky content");
}

class ErrorBoundaryComposition : Component
{
    public override Element Render() =>
        ErrorBoundary(
            fallback: ex => TextBlock($"Crash: {ex.Message}")
                .Foreground(Theme.SystemCritical),
            child: Component<RiskyView>()
        );
}
// </snippet:error-boundary-composition>

// <snippet:side-effects-in-render>
static class Globals { public static int RenderCount; }

class SideEffectsInRenderDont : Component
{
    public override Element Render()
    {
        // Don't — Render() may be called any number of times per user action.
        File.AppendAllText("log.txt", "rendered\n");  // I/O during render
        Globals.RenderCount++;                        // mutation during render
        return TextBlock("hi");
    }
}
// </snippet:side-effects-in-render>

// <snippet:mutating-props>
record ItemsProps(List<string> Items);

class MutatingPropsDont : Component<ItemsProps>
{
    public override Element Render()
    {
        // Don't — this mutates the parent's collection, so the parent never
        // sees the change and doesn't re-render.
        Props.Items.Add("new item");
        return VStack(4, ForEach(Props.Items, i => TextBlock(i)));
    }
}
// </snippet:mutating-props>

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
