// id: use-context-multi
// intent: compose multiple nested contexts and read them from the same child
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Multiple contexts can be layered so consumers read each concern independently.
ReactorApp.Run<App>("UseContextMulti", width: 400, height: 200);

class App : Component
{
    internal static readonly Context<string> ThemeContext = new("Light");
    internal static readonly Context<string> UserContext = new("Guest");

    public override Element Render()
    {
        return VStack(12,
            Component<ProfileCard>())
            .Provide(UserContext, "Ada")
            .Provide(ThemeContext, "Dark");
    }

    class ProfileCard : Component
    {
        public override Element Render()
        {
            var user = UseContext(UserContext);
            var theme = UseContext(ThemeContext);
            return VStack(8,
                Heading($"Hello, {user}"),
                Caption($"Theme from context: {theme}"));
        }
    }
}
