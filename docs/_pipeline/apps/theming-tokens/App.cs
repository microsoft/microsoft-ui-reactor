using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

// Doc app for `theming-tokens.md` — renders a swatch grid for every named
// `Theme.*` token defined in src/Reactor/Core/Theme.cs. The doc-app harness
// captures `light` and `dark` variants for the page's lead screenshot pair.
ReactorApp.Run<ThemingTokensApp>("Theming Tokens", width: 760, height: 720
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
                ("SystemSolidAttention", Theme.SystemSolidAttention),
                ("SystemAttentionBackground", Theme.SystemAttentionBackground),
                ("SystemSuccessBackground", Theme.SystemSuccessBackground),
                ("SystemCautionBackground", Theme.SystemCautionBackground),
                ("SystemCriticalBackground", Theme.SystemCriticalBackground),
                ("SystemNeutralBackground", Theme.SystemNeutralBackground),
            })
        ).Padding(20)
    );

    private static Element SwatchSection(string title, (string Name, ThemeRef Ref)[] tokens) =>
        VStack(8,
            SubHeading(title),
            VStack(4, tokens.Select(t => Row(t.Name, t.Ref).WithKey(t.Name)).ToArray())
        );

    private static Element Row(string name, ThemeRef token) => HStack(12,
        new BorderElement(Empty())
            .Background(token)
            .Size(40, 24)
            .WithBorder(Theme.ControlStroke),
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
//
// Don't:
// Button("Click me", () => { }).Background("#0066CC");
class HardcodedColorBad : Component
{
    public override Element Render() =>
        Button("Click me", () => { }).Background(Theme.Accent);
}
// </snippet:bad-hardcoded>

// <snippet:good-theme-ref>
class ThemeRefGood : Component
{
    public override Element Render() =>
        Button("Click me", () => { }).Background(Theme.Accent);
}
// </snippet:good-theme-ref>

class CustomKeyButton : Component
{
    public override Element Render() =>
        // <snippet:custom-key>
        // Reference any XAML resource by string key — covers app-level overrides
        // and any token Reactor doesn't surface as a typed accessor.
        Button("Custom", () => { })
            .Background(Theme.Ref("MyAppTitleBarBackground"));
        // </snippet:custom-key>
}

class BrandPrimaryButton : Component
{
    public override Element Render() =>
        // <snippet:brand-primary-button>
        Button("Buy now", BuyAction)
            .Background(Theme.Ref("BrandPrimaryBrush"));
        // </snippet:brand-primary-button>

    private void BuyAction()
    {
    }
}

class LightPreviewPane : Component
{
    public override Element Render()
    {
        var content = VStack(8,
            TextBlock("Print preview").Foreground(Theme.PrimaryText),
            TextBlock("Always rendered with light theme tokens.")
                .Foreground(Theme.SecondaryText));

        // <snippet:per-element-theme>
        return ScrollView(content).RequestedTheme(ElementTheme.Light);
        // </snippet:per-element-theme>
    }
}

// <snippet:status-banner>
// Map a severity onto the Signal token pair (foreground + matching
// background) so the banner tracks the theme in both light and dark.
class StatusBannerDemo : Component
{
    public override Element Render() => VStack(8,
        StatusBanner("Saved", InfoBarSeverity.Success),
        StatusBanner("Disk almost full", InfoBarSeverity.Warning),
        StatusBanner("Upload failed", InfoBarSeverity.Error),
        StatusBanner("Sync scheduled", InfoBarSeverity.Informational)
    ).Padding(16);

    internal static Element StatusBanner(string text, InfoBarSeverity severity) => HStack(8,
        TextBlock(text).Foreground(severity switch
        {
            InfoBarSeverity.Success => Theme.SystemSuccess,
            InfoBarSeverity.Warning => Theme.SystemCaution,
            InfoBarSeverity.Error => Theme.SystemCritical,
            _ => Theme.SystemNeutral,
        })
    ).Background(severity switch
    {
        InfoBarSeverity.Success => Theme.SystemSuccessBackground,
        InfoBarSeverity.Warning => Theme.SystemCautionBackground,
        InfoBarSeverity.Error => Theme.SystemCriticalBackground,
        _ => Theme.SystemNeutralBackground,
    }).Padding(12).CornerRadius(4);
}
// </snippet:status-banner>

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
