using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

// Doc app for `theming-tokens.md` — renders a swatch grid for every named
// `Theme.*` token defined in src/Reactor/Core/Theme.cs. The doc-app harness
// captures `light` and `dark` variants for the page's lead screenshot pair.
ReactorApp.Run<ThemingTokensApp>("Theming Tokens", width: 760, height: 720
#if DEBUG
    , preview: true
#endif
);

class ThemingTokensApp : Component
{
    public override Element Render() => Component<SwatchGrid>();
}

// <snippet:swatch-grid>
class SwatchGrid : Component
{
    public override Element Render() => ScrollView(
        VStack(16,
            Heading("Theme tokens"),
            SwatchSection("Accent", new[] {
                ("Accent", Theme.Accent),
                ("AccentSecondary", Theme.AccentSecondary),
                ("AccentTertiary", Theme.AccentTertiary),
                ("AccentDisabled", Theme.AccentDisabled),
            }),
            SwatchSection("Text", new[] {
                ("PrimaryText", Theme.PrimaryText),
                ("SecondaryText", Theme.SecondaryText),
                ("TertiaryText", Theme.TertiaryText),
                ("DisabledText", Theme.DisabledText),
                ("AccentText", Theme.AccentText),
            }),
            SwatchSection("Surfaces", new[] {
                ("SolidBackground", Theme.SolidBackground),
                ("CardBackground", Theme.CardBackground),
                ("SmokeFill", Theme.SmokeFill),
                ("SubtleFill", Theme.SubtleFill),
                ("LayerFill", Theme.LayerFill),
            }),
            SwatchSection("Control fill", new[] {
                ("ControlFill", Theme.ControlFill),
                ("ControlFillSecondary", Theme.ControlFillSecondary),
                ("ControlFillTertiary", Theme.ControlFillTertiary),
                ("ControlFillDisabled", Theme.ControlFillDisabled),
                ("ControlFillInputActive", Theme.ControlFillInputActive),
            }),
            SwatchSection("Stroke", new[] {
                ("CardStroke", Theme.CardStroke),
                ("SurfaceStroke", Theme.SurfaceStroke),
                ("DividerStroke", Theme.DividerStroke),
                ("ControlStroke", Theme.ControlStroke),
                ("ControlStrokeSecondary", Theme.ControlStrokeSecondary),
            }),
            SwatchSection("Signal", new[] {
                ("SystemAttention", Theme.SystemAttention),
                ("SystemSuccess", Theme.SystemSuccess),
                ("SystemCaution", Theme.SystemCaution),
                ("SystemCritical", Theme.SystemCritical),
                ("SystemNeutral", Theme.SystemNeutral),
                ("SystemSolidNeutral", Theme.SystemSolidNeutral),
            })
        ).Padding(20)
    );

    private static Element SwatchSection(string title, (string Name, ThemeRef Ref)[] tokens) =>
        VStack(8,
            SubHeading(title),
            VStack(4, tokens.Select(t => Row(t.Name, t.Ref)).ToArray())
        );

    private static Element Row(string name, ThemeRef token) => HStack(12,
        new BorderElement(Empty())
            .Background(token)
            .Size(40, 24)
            .WithBorder("#DDDDDD"),
        TextBlock(name).Width(220),
        TextBlock(token.ResourceKey).Opacity(0.6)
    );
}
// </snippet:swatch-grid>

// <snippet:bad-hardcoded>
// REACTOR_THEME_001 — the analyzer flags hardcoded color literals on theme-
// aware modifiers like .Background / .Foreground. Use Theme.* tokens (or
// Theme.Ref("...") for a custom XAML resource key) so the value follows the
// theme switch.
class HardcodedColorBad : Component
{
    public override Element Render() =>
        Button("Click me", () => { }).Background("#0066CC");   // REACTOR_THEME_001
}
// </snippet:bad-hardcoded>

// <snippet:good-theme-ref>
class ThemeRefGood : Component
{
    public override Element Render() =>
        Button("Click me", () => { }).Background(Theme.Accent);
}
// </snippet:good-theme-ref>

// <snippet:use-color-scheme>
// UseColorScheme reads the current scheme (Light / Dark) reactively so a
// component can branch on the value without re-implementing the resolver.
class SchemeAwareBadge : Component
{
    public override Element Render()
    {
        var scheme = UseColorScheme();
        var label = scheme == ColorScheme.Dark ? "Dark mode" : "Light mode";
        return TextBlock(label).Foreground(Theme.PrimaryText);
    }
}
// </snippet:use-color-scheme>
