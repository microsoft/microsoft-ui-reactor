using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<StylingApp>("Styling and Theming", width: 650, height: 800
#if DEBUG
    , preview: true
#endif
);

// <snippet:theme-tokens>
class ThemeTokensExample : Component
{
    public override Element Render()
    {
        return VStack(12,
            TextBlock("Primary Text").Foreground(Theme.PrimaryText),
            TextBlock("Secondary Text").Foreground(Theme.SecondaryText),
            TextBlock("Accent Text").Foreground(Theme.AccentText).SemiBold(),
            TextBlock("On Accent Background")
                .Foreground("#FFFFFF")
                .Padding(horizontal: 8, vertical: 4)
                .Background(Theme.Accent)
                .CornerRadius(4)
        ).Padding(24);
    }
}
// </snippet:theme-tokens>

// <snippet:card-layout>
class CardLayoutExample : Component
{
    public override Element Render()
    {
        return VStack(16,
            Heading("Dashboard"),
            HStack(12,
                Card("Users", "1,204", Theme.Accent),
                Card("Revenue", "$48.2k", Theme.SystemSuccess),
                Card("Errors", "3", Theme.SystemCritical)
            )
        ).Padding(24);
    }

    static Element Card(string title, string value, ThemeRef accent) =>
        Border(
            VStack(8,
                Caption(title).Foreground(Theme.SecondaryText),
                TextBlock(value).FontSize(28).Bold().Foreground(accent)
            ).Padding(16)
        ).Background(Theme.CardBackground)
         .CornerRadius(8)
         .WithBorder(Theme.CardStroke, 1)
         .Width(160);
}
// </snippet:card-layout>

// <snippet:color-modifiers>
class ColorModifiersExample : Component
{
    public override Element Render()
    {
        return VStack(8,
            TextBlock("Theme token").Background(Theme.SubtleFill).Padding(8),
            TextBlock("Hex string").Background("#E8F5E9").Padding(8),
            TextBlock("Mixed").Foreground(Theme.PrimaryText)
                .Background("#1E1E2E").Padding(8)
        ).Padding(24);
    }
}
// </snippet:color-modifiers>

// <snippet:signal-colors>
class SignalColorsExample : Component
{
    public override Element Render()
    {
        return HStack(12,
            Badge("Info", Theme.SystemAttention),
            Badge("Success", Theme.SystemSuccess),
            Badge("Warning", Theme.SystemCaution),
            Badge("Error", Theme.SystemCritical)
        ).Padding(24);
    }

    static Element Badge(string label, ThemeRef color) =>
        TextBlock(label)
            .FontSize(12).SemiBold()
            .Foreground(color)
            .Padding(horizontal: 8, vertical: 4)
            .Background(Theme.SubtleFill)
            .CornerRadius(4);
}
// </snippet:signal-colors>

// <snippet:dark-light-toggle>
class DarkLightToggleExample : Component
{
    public override Element Render()
    {
        var (isDark, setIsDark) = UseState(false);
        var theme = isDark ? ElementTheme.Dark : ElementTheme.Light;

        return VStack(16,
            ToggleSwitch(isDark, setIsDark, onContent: "Dark", offContent: "Light"),
            Border(
                VStack(12,
                    TextBlock("This panel follows the toggle.").Foreground(Theme.PrimaryText),
                    TextBlock("Background adapts automatically.").Foreground(Theme.SecondaryText)
                ).Padding(16)
            ).Background(Theme.CardBackground)
             .CornerRadius(8)
             .RequestedTheme(theme)
        ).Padding(24);
    }
}
// </snippet:dark-light-toggle>

// <snippet:color-scheme-hook>
class ColorSchemeHookExample : Component
{
    public override Element Render()
    {
        var isDark = UseIsDarkTheme();
        var scheme = UseColorScheme();

        return VStack(12,
            TextBlock($"Color scheme: {scheme}").FontSize(16).SemiBold(),
            TextBlock(isDark ? "Dark mode is active" : "Light mode is active")
                .Foreground(Theme.SecondaryText),
            Border(
                TextBlock(isDark ? "Dark content" : "Light content")
                    .Padding(12)
            ).Background(Theme.CardBackground)
             .CornerRadius(8)
             .WithBorder(Theme.CardStroke, 1)
        ).Padding(24);
    }
}
// </snippet:color-scheme-hook>

// <snippet:lightweight-styling>
class LightweightStylingExample : Component
{
    public override Element Render()
    {
        return VStack(12,
            Button("Default Button", () => { }),
            Button("Accent Button", () => { })
                .Resources(r => r
                    .Set("ButtonBackground", Theme.Accent)
                    .Set("ButtonBackgroundPointerOver", Theme.AccentSecondary)
                    .Set("ButtonBackgroundPressed", Theme.AccentTertiary)
                    .Set("ButtonForeground", "#FFFFFF")
                    .Set("ButtonForegroundPointerOver", "#FFFFFF")
                    .Set("ButtonForegroundPressed", "#FFFFFF")),
            Button("Danger Button", () => { })
                .Resources(r => r
                    .Set("ButtonBackground", Theme.SystemCritical)
                    .Set("ButtonBackgroundPointerOver", "#C42B1C")
                    .Set("ButtonForeground", "#FFFFFF")
                    .Set("ButtonForegroundPointerOver", "#FFFFFF"))
        ).Padding(24);
    }
}
// </snippet:lightweight-styling>

// <snippet:custom-resource>
class CustomResourceExample : Component
{
    public override Element Render()
    {
        return VStack(12,
            TextBlock("Using a named WinUI resource:")
                .Foreground(Theme.PrimaryText),
            TextBlock("NavigationViewItemForeground")
                .Foreground(Theme.Ref("NavigationViewItemForeground"))
        ).Padding(24);
    }
}
// </snippet:custom-resource>

// <snippet:named-styles>
class NamedStylesExample : Component
{
    public override Element Render()
    {
        return VStack(12,
            HStack(8,
                Button("Save", () => { }).AccentButton(),
                Button("Cancel", () => { }).SubtleButton(),
                HyperlinkButton("Learn more", new Uri("https://example.com"))
                    .TextLink()
            ),
            InfoBar(title: "Heads up", message: "Backups run nightly.")
                .Informational(),
            InfoBar(title: "Saved", message: "All changes are persisted.")
                .Success(),
            InfoBar(title: "Almost full", message: "75% of quota used.")
                .Warning(),
            InfoBar(title: "Sync failed", message: "Check your network.")
                .Error()
        ).Padding(24);
    }
}
// </snippet:named-styles>

// <snippet:brand-override>
// Brand color override at app root — every descendant that resolves
// AccentFillColorDefaultBrush picks up the brand color in both themes.
class BrandOverrideExample : Component
{
    public override Element Render()
    {
        return VStack(12,
            SubHeading("Brand color cascades through descendants"),
            Button("Save", () => { }).AccentButton(),
            TextBlock("Accented text").Foreground(Theme.AccentText).SemiBold()
        ).Padding(24)
         // One Resources override at the root re-skins every descendant.
         // Cross-theme: set in both ThemeDictionaries if light vs. dark
         // should pick different brand hues.
         .Resources(r => r
            .Set("AccentFillColorDefaultBrush", "#7B61FF")
            .Set("AccentTextFillColorPrimaryBrush", "#7B61FF"));
    }
}
// </snippet:brand-override>

// <snippet:scoped-theme-override>
// Per-element theme override scope — a single panel forced to Dark
// inside an otherwise Light app, without app-wide RequestedTheme.
class ScopedThemeOverrideExample : Component
{
    public override Element Render()
    {
        return VStack(16,
            SubHeading("Default scheme"),
            Border(VStack(8,
                TextBlock("Default scheme — follows the app theme.")
                    .Foreground(Theme.PrimaryText),
                TextBlock("Card stroke and background also follow.")
                    .Foreground(Theme.SecondaryText)
            ).Padding(16)).Background(Theme.CardBackground)
             .WithBorder(Theme.CardStroke, 1).CornerRadius(8),

            SubHeading("Dark scope — bound to a region root"),
            Border(VStack(8,
                TextBlock("This subtree is always dark.")
                    .Foreground(Theme.PrimaryText),
                TextBlock("ThemeRef descendants resolve against the override.")
                    .Foreground(Theme.SecondaryText)
            ).Padding(16)).Background(Theme.CardBackground)
             .WithBorder(Theme.CardStroke, 1).CornerRadius(8)
             // Region root carries the override — leaf-level overrides
             // are the anti-pattern called out in Common Mistakes.
             .RequestedTheme(ElementTheme.Dark)
        ).Padding(24);
    }
}
// </snippet:scoped-theme-override>

// Main app showing all examples
class StylingApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Styling and Theming"),
                Component<ThemeTokensExample>(),
                Component<CardLayoutExample>(),
                Component<SignalColorsExample>(),
                Component<DarkLightToggleExample>(),
                Component<ColorSchemeHookExample>(),
                Component<NamedStylesExample>(),
                Component<LightweightStylingExample>(),
                Component<BrandOverrideExample>(),
                Component<ScopedThemeOverrideExample>()
            ).Padding(24)
        );
    }
}
