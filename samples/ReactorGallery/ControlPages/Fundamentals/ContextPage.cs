using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Fundamentals;

/// <summary>
/// A context is declared once, as a static, and shared by everyone who provides or reads it.
/// The value passed to the constructor is what a consumer sees when no ancestor provides one.
/// </summary>
static class GreetingContext
{
    public static readonly Context<string> Current = new("Hello from the default value", "Greeting");
}

/// <summary>Reads the ambient greeting. It takes no parameters — that is the point.</summary>
class GreetingLabel : Component
{
    public override Element Render() =>
        TextBlock(UseContext(GreetingContext.Current)).Foreground(Theme.SecondaryText);
}

class ContextPage : Component
{
    public override Element Render()
    {
        var (greeting, setGreeting) = UseState("Hello from the provider");

        return ScrollView(VStack(16,
            PageHeader("Context",
                "Tree-scoped ambient state. Provide a value on an ancestor and any descendant reads it with UseContext — no prop drilling through the components in between."),

            SampleCard("Provide on an ancestor, read anywhere below",
                VStack(8,
                    TextBox(greeting, setGreeting, placeholderText: "Change the provided value")
                        .AutomationName("Greeting")
                        .Width(320),
                    VStack(4,
                        Caption("Nested three deep — none of these components takes a parameter.")
                            .Foreground(Theme.SecondaryText),
                        VStack(4, VStack(4, Component<GreetingLabel>())))
                        .Provide(GreetingContext.Current, greeting)),
                sourceCode: @"
public static readonly Context<string> Current = new(""Hello from the default value"", ""Greeting"");

// Consume, at any depth below the provider.
class GreetingLabel : Component
{
    public override Element Render() => TextBlock(UseContext(GreetingContext.Current));
}

// Provide — every descendant sees this value.
VStack(4, VStack(4, Component<GreetingLabel>())).Provide(GreetingContext.Current, greeting)
"),

            SampleCard("No provider means the default value",
                VStack(8,
                    Component<GreetingLabel>(),
                    Caption("The same component, outside any Provide. UseContext falls back to the value the Context was constructed with, so it never returns an unexpected null.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
// Declared with a default, so a consumer with no provider above it still reads something.
public static readonly Context<string> Current = new(""Hello from the default value"", ""Greeting"");

Component<GreetingLabel>()   // no .Provide above it — reads the default
")
        ).Margin(36, 24, 36, 36));
    }
}
