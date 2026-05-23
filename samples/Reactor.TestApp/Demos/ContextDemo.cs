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

class ContextDemo : Component
{
    static readonly Context<string> AccentContext = new("#0078D4");
    static readonly Context<string> UserNameContext = new("Guest");

    public override Element Render()
    {
        var (accent, setAccent) = UseState("#0078D4");
        var (userName, setUserName) = UseState("Alice");

        return ScrollView(VStack(16,
            Heading("Context System"),
            TextBlock("Context passes values through the tree without prop drilling."),

            // 1. Controls
            SubHeading("1. Provide context values"),
            HStack(8,
                TextBlock("Accent color:"),
                Button("Blue", () => setAccent("#0078D4")).IsEnabled(!(accent == "#0078D4")),
                Button("Red", () => setAccent("#E74C3C")).IsEnabled(!(accent == "#E74C3C")),
                Button("Green", () => setAccent("#50C878")).IsEnabled(!(accent == "#50C878")),
                Border(Empty()).Background(accent).CornerRadius(4).Size(24, 24)
            ),
            HStack(8,
                TextBlock("User name:"),
                TextBox(userName, setUserName, placeholder: "Enter name").Width(200)
            ),

            // 2. Consumers
            SubHeading("2. Consume in descendants"),
            TextBlock("These components call UseContext() — no props passed from parent."),
            VStack(8,
                Component<AccentBadge>(),
                Component<UserGreeting>()
            ).Provide(AccentContext, accent).Provide(UserNameContext, userName),

            // 3. Nested override
            SubHeading("3. Nested provider overrides outer"),
            TextBlock("An inner .Provide() shadows the outer for its subtree only."),
            HStack(16,
                VStack(8,
                    TextBlock("Outer scope").SemiBold(),
                    Component<AccentBadge>()
                ).Provide(AccentContext, accent).Provide(UserNameContext, userName),
                VStack(8,
                    TextBlock("Inner scope (forced purple)").SemiBold(),
                    Component<AccentBadge>()
                ).Provide(AccentContext, "#9B59B6").Provide(UserNameContext, userName)
            ),

            // 4. Default value
            SubHeading("4. Default value (no provider)"),
            TextBlock("Without a .Provide() ancestor, UseContext returns the Context default."),
            Component<AccentBadge>()
        ));
    }

    class AccentBadge : Component
    {
        public override Element Render()
        {
            var accent = UseContext(AccentContext);
            return HStack(8,
                Border(Empty()).Background(accent).CornerRadius(4).Size(24, 24),
                TextBlock($"Accent = {accent}").SemiBold()
            );
        }
    }

    class UserGreeting : Component
    {
        public override Element Render()
        {
            var accent = UseContext(AccentContext);
            var name = UseContext(UserNameContext);
            return Border(
                TextBlock($"Hello, {name}!").Foreground(accent).SemiBold().FontSize(18)
            ).Padding(12).CornerRadius(6).Background(SubtleFill);
        }
    }
}
