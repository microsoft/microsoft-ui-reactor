// <snippet:hello-world>
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<GettingStartedApp>("Getting Started", width: 600, height: 400, devtools: true);

class GettingStartedApp : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("World");

        return VStack(16,
            TextBlock($"Hello, {name}!").FontSize(24).Bold(),
            TextBox(name, setName, placeholderText: "Enter your name").Width(250)
        ).Padding(24);
    }
}
// </snippet:hello-world>

// The classes below are alternate roots for the rest of this page. Only one
// ReactorApp.Run<T>() call can run as a top-level statement, so the launch
// lines for these classes are shown as comments — drop one into a fresh
// App.cs to try it.

// <snippet:usestate-counter>
// Launch with:
//   ReactorApp.Run<CounterExample>("Counter", width: 600, height: 400);

class CounterExample : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);

        return VStack(12,
            TextBlock($"Count: {count}").FontSize(20).SemiBold(),
            HStack(8,
                Button("- 1", () => setCount(count - 1)),
                Button("Reset", () => setCount(0)),
                Button("+ 1", () => setCount(count + 1))
            )
        ).Padding(24);
    }
}
// </snippet:usestate-counter>

// <snippet:layout-basics>
// Launch with:
//   ReactorApp.Run<LayoutBasicsExample>("Layout", width: 600, height: 400);

class LayoutBasicsExample : Component
{
    public override Element Render()
    {
        return VStack(16,
            Heading("Layout Demo"),

            SubHeading("Horizontal Stack"),
            HStack(8,
                Button("One"),
                Button("Two"),
                Button("Three")
            ),

            SubHeading("Nested Layout"),
            HStack(16,
                VStack(4,
                    TextBlock("Left Column").Bold(),
                    TextBlock("Item A"),
                    TextBlock("Item B")
                ),
                VStack(4,
                    TextBlock("Right Column").Bold(),
                    TextBlock("Item X"),
                    TextBlock("Item Y")
                )
            )
        ).Padding(24);
    }
}
// </snippet:layout-basics>

// <snippet:multiple-state>
// Launch with:
//   ReactorApp.Run<MultipleStateExample>("Multiple State", width: 600, height: 400);

class MultipleStateExample : Component
{
    public override Element Render()
    {
        var (firstName, setFirstName) = UseState("");
        var (lastName, setLastName) = UseState("");
        var (fontSize, setFontSize) = UseState(16.0);

        var fullName = string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName)
            ? "Anonymous"
            : $"{firstName} {lastName}".Trim();

        return VStack(12,
            TextBlock($"Hello, {fullName}!").FontSize(fontSize).Bold(),
            TextBox(firstName, setFirstName, placeholderText: "First name").Width(200),
            TextBox(lastName, setLastName, placeholderText: "Last name").Width(200),
            HStack(8,
                TextBlock("Font size:"),
                Slider(fontSize, 10, 40, setFontSize).Width(200),
                TextBlock($"{fontSize:F0}px")
            )
        ).Padding(24);
    }
}
// </snippet:multiple-state>
