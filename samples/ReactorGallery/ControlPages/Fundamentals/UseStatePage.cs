using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Fundamentals;

class UseStatePage : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (name, setName) = UseState("");
        var (showDetails, setShowDetails) = UseState(false);

        return ScrollView(VStack(16,
            PageHeader("UseState",
                "The primary state hook. UseState returns the current value and a setter; calling the setter with a different value schedules a re-render."),

            SampleCard("A value and its setter",
                VStack(8,
                    HStack(8,
                        Button("Increment", () => setCount(count + 1)),
                        Button("Reset", () => setCount(0))),
                    TextBlock($"Count: {count}").Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (count, setCount) = UseState(0);

VStack(8,
    HStack(8,
        Button(""Increment"", () => setCount(count + 1)),
        Button(""Reset"", () => setCount(0))),
    TextBlock($""Count: {count}""))
"),

            SampleCard("Controlled text input",
                VStack(8,
                    TextBox(name, setName, placeholderText: "Type your name")
                        .AutomationName("Name")
                        .Width(280),
                    TextBlock(name.Length == 0 ? "Nothing typed yet." : $"Hello, {name}!")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (name, setName) = UseState("""");

VStack(8,
    TextBox(name, setName, placeholderText: ""Type your name"").AutomationName(""Name""),
    TextBlock(name.Length == 0 ? ""Nothing typed yet."" : $""Hello, {name}!""))
"),

            SampleCard("State decides what renders",
                VStack(8,
                    ToggleSwitch(showDetails, setShowDetails, header: "Show details"),
                    showDetails
                        ? TextBlock("A conditional child may be null — null children are filtered out.")
                            .Foreground(Theme.SecondaryText)
                        : Empty()),
                sourceCode: @"
var (showDetails, setShowDetails) = UseState(false);

VStack(8,
    ToggleSwitch(showDetails, setShowDetails, header: ""Show details""),
    showDetails ? TextBlock(""Details are visible."") : Empty())
"),

            SampleCard("A reference type needs a new instance",
                VStack(8,
                    Caption("Hooks run in the same order every render, so they are never called inside an if or a loop.")
                        .Foreground(Theme.SecondaryText),
                    Caption("Mutating a list in place leaves the reference unchanged, so the setter sees no difference and nothing re-renders. Collections belong to UseReducer.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
// Does NOT re-render — the same List reference before and after.
var (todos, setTodos) = UseState(new List<string>());
todos.Add(""wrong"");
setTodos(todos);

// Re-renders — a new list every time.
var (items, updateItems) = UseReducer(new List<string>());
updateItems(list => [.. list, ""right""]);
")
        ).Margin(36, 24, 36, 36));
    }
}
