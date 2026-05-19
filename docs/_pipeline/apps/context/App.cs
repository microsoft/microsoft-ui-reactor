using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<ContextApp>("Context", width: 600, height: 600
#if DEBUG
    , preview: true
#endif
);

// <snippet:create-context>
static class Contexts
{
    public static Context<string> ThemeMode = new("light");
    public static Context<string> UserName = new("Guest");
    public static Context<int> FontScale = new(16);
}
// </snippet:create-context>

// <snippet:provide-consume>
class ProvideConsumeExample : Component
{
    public override Element Render()
    {
        return VStack(12,
            TextBlock("Outside: no provider"),
            VStack(12,
                Component<Greeting>()
            ).Provide(Contexts.UserName, "Alice")
        ).Padding(24);
    }
}

class Greeting : Component
{
    public override Element Render()
    {
        var name = UseContext(Contexts.UserName);
        return TextBlock($"Hello, {name}!").FontSize(20).Bold();
    }
}
// </snippet:provide-consume>

// <snippet:theme-switch>
class ThemeSwitchExample : Component
{
    public override Element Render()
    {
        var (isDark, setIsDark) = UseState(false);
        var mode = isDark ? "dark" : "light";
        return VStack(16,
            ToggleSwitch(isDark, setIsDark, onContent: "Dark", offContent: "Light"),
            VStack(12, Component<ThemePanel>()).Provide(Contexts.ThemeMode, mode)
        ).Padding(24);
    }
}

class ThemePanel : Component
{
    public override Element Render()
    {
        var theme = UseContext(Contexts.ThemeMode);
        var elTheme = theme == "dark" ? ElementTheme.Dark : ElementTheme.Light;
        return Border(
            VStack(8,
                TextBlock($"Current theme: {theme}").Bold(),
                TextBlock("Panel adapts to context.").Foreground(Theme.SecondaryText)
            ).Padding(16)
        ).Background(Theme.CardBackground)
         .CornerRadius(8)
         .Set(b => b.RequestedTheme = elTheme);
    }
}
// </snippet:theme-switch>

// <snippet:nested-override>
class NestedOverrideExample : Component
{
    public override Element Render()
    {
        return HStack(16,
            VStack(8,
                Caption("Parent value"),
                Component<NameDisplay>()
            ).Provide(Contexts.UserName, "Alice"),
            VStack(8,
                Caption("Overridden child"),
                VStack(4, Component<NameDisplay>())
                    .Provide(Contexts.UserName, "Bob")
            ).Provide(Contexts.UserName, "Alice")
        ).Padding(24);
    }
}

class NameDisplay : Component
{
    public override Element Render()
    {
        var name = UseContext(Contexts.UserName);
        return TextBlock(name).FontSize(18).SemiBold().Foreground(Theme.Accent);
    }
}
// </snippet:nested-override>

// <snippet:multiple-contexts>
class MultipleContextsExample : Component
{
    public override Element Render()
    {
        return VStack(8,
            Component<ProfileCard>()
        ).Provide(Contexts.UserName, "Charlie")
         .Provide(Contexts.FontScale, 22)
         .Padding(24);
    }
}

class ProfileCard : Component
{
    public override Element Render()
    {
        var name = UseContext(Contexts.UserName);
        var fontSize = UseContext(Contexts.FontScale);

        return Border(
            VStack(8,
                TextBlock(name).FontSize(fontSize).Bold(),
                TextBlock($"Font scale from context: {fontSize}px")
                    .Foreground(Theme.SecondaryText)
            ).Padding(16)
        ).Background(Theme.CardBackground).CornerRadius(8);
    }
}
// </snippet:multiple-contexts>

// <snippet:user-context>
// A typed user-context record at the app root — every page reads the
// current user via UseContext rather than threading a User prop through
// every component along the way.
record CurrentUser(string Id, string DisplayName, bool IsAdmin);

static class AppContexts
{
    public static Context<CurrentUser> User = new(
        new CurrentUser("guest", "Guest", IsAdmin: false));
}

class UserContextExample : Component
{
    public override Element Render()
    {
        var (user, setUser) = UseState(new CurrentUser("u1", "Alice", IsAdmin: true));

        return VStack(12,
            HStack(8,
                Button("Sign in as Alice (admin)",
                    () => setUser(new CurrentUser("u1", "Alice", IsAdmin: true))),
                Button("Sign in as Bob (user)",
                    () => setUser(new CurrentUser("u2", "Bob", IsAdmin: false)))
            ),
            VStack(8,
                Component<AccountMenu>(),
                Component<AdminPanel>()
            ).Provide(AppContexts.User, user)
        ).Padding(24);
    }
}

class AccountMenu : Component
{
    public override Element Render()
    {
        var user = UseContext(AppContexts.User);
        return TextBlock($"Signed in as: {user.DisplayName}").SemiBold();
    }
}

class AdminPanel : Component
{
    public override Element Render()
    {
        var user = UseContext(AppContexts.User);
        return user.IsAdmin
            ? Border(TextBlock("Admin tools available").Padding(8))
                .Background(Theme.CardBackground).CornerRadius(4)
            : TextBlock("(no admin tools)").Foreground(Theme.SecondaryText);
    }
}
// </snippet:user-context>

// <snippet:memoize-context-value>
// The value identity matters. Wrapping in UseMemo with explicit deps
// stops every consumer from re-rendering on every provider render —
// the inline-literal version below would create a fresh tuple every
// frame even when nothing changed.
record ThemeConfig(string Mode, int FontScale, string Accent);

static class ThemeContexts
{
    public static Context<ThemeConfig> Theme = new(new ThemeConfig("light", 14, "#0078D4"));
}

class MemoizeContextValueExample : Component
{
    public override Element Render()
    {
        var (mode, setMode) = UseState("light");
        var (scale, setScale) = UseState(14);

        // GOOD — identity stable while inputs unchanged. Consumers only
        // re-render when mode or scale actually change.
        var theme = UseMemo(() => new ThemeConfig(mode, scale, "#0078D4"), mode, scale);

        // BAD — every render creates a fresh ThemeConfig.
        // var theme = new ThemeConfig(mode, scale, "#0078D4");

        return VStack(12,
            HStack(8,
                Button("Toggle mode", () => setMode(mode == "light" ? "dark" : "light")),
                Button("Bump scale", () => setScale(scale + 2))
            ),
            VStack(8, Component<ThemedHeading>())
                .Provide(ThemeContexts.Theme, theme)
        ).Padding(24);
    }
}

class ThemedHeading : Component
{
    public override Element Render()
    {
        var t = UseContext(ThemeContexts.Theme);
        return TextBlock($"Headline @ {t.FontScale}px ({t.Mode})")
            .FontSize(t.FontScale).Foreground(t.Accent);
    }
}
// </snippet:memoize-context-value>

// <snippet:mock-provider-test>
// Production root provides the real value; tests render the same
// component tree with a Provide(...) wrapper supplying a stub. The
// consumer code is unchanged — context lets you swap dependencies
// without re-plumbing props.
class CartConsumer : Component
{
    public override Element Render()
    {
        var user = UseContext(AppContexts.User);
        return TextBlock($"Cart for {user.DisplayName} ({user.Id})");
    }
}

class MockProviderExample : Component
{
    public override Element Render()
    {
        // Two trees: one with the "real" production default, one with a
        // test stub supplied via .Provide(). Same CartConsumer in both.
        return HStack(24,
            VStack(8,
                Caption("Production default"),
                Component<CartConsumer>()
            ),
            VStack(8,
                Caption("Test stub"),
                VStack(0, Component<CartConsumer>())
                    .Provide(AppContexts.User, new CurrentUser("test", "Test User", IsAdmin: false))
            )
        ).Padding(24);
    }
}
// </snippet:mock-provider-test>

// Main app
class ContextApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Context"),
                Component<ProvideConsumeExample>(),
                Component<ThemeSwitchExample>(),
                Component<NestedOverrideExample>(),
                Component<MultipleContextsExample>(),
                Component<UserContextExample>(),
                Component<MemoizeContextValueExample>(),
                Component<MockProviderExample>()
            ).Padding(24)
        );
    }
}
