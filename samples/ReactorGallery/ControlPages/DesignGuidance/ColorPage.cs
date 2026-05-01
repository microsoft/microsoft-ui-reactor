using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Styles;

class ColorPage : Component
{
    record BrushEntry(string Token, ThemeRef Color, string ResourceKey, string Recommendation);

    static readonly BrushEntry[] TextBrushes =
    [
        new("PrimaryText", Theme.PrimaryText, "TextFillColorPrimaryBrush",
            "Primary body text, titles, and labels on any background."),
        new("SecondaryText", Theme.SecondaryText, "TextFillColorSecondaryBrush",
            "Captions, subtitles, and supplementary information."),
        new("TertiaryText", Theme.TertiaryText, "TextFillColorTertiaryBrush",
            "Placeholder text and disabled secondary content."),
        new("DisabledText", Theme.DisabledText, "TextFillColorDisabledBrush",
            "Text in disabled controls or inactive states."),
        new("AccentText", Theme.AccentText, "AccentTextFillColorPrimaryBrush",
            "Hyperlinks, accent-colored labels, and interactive text."),
    ];

    static readonly BrushEntry[] AccentBrushes =
    [
        new("Accent", Theme.Accent, "AccentFillColorDefaultBrush",
            "Primary accent buttons and controls at rest."),
        new("AccentSecondary", Theme.AccentSecondary, "AccentFillColorSecondaryBrush",
            "Accent button hover state."),
        new("AccentTertiary", Theme.AccentTertiary, "AccentFillColorTertiaryBrush",
            "Accent button pressed state."),
        new("AccentDisabled", Theme.AccentDisabled, "AccentFillColorDisabledBrush",
            "Disabled accent controls."),
    ];

    static readonly BrushEntry[] ControlFillBrushes =
    [
        new("ControlFill", Theme.ControlFill, "ControlFillColorDefaultBrush",
            "Default rest state for standard controls like buttons and inputs."),
        new("ControlFillSecondary", Theme.ControlFillSecondary, "ControlFillColorSecondaryBrush",
            "Hover state of standard controls."),
        new("ControlFillTertiary", Theme.ControlFillTertiary, "ControlFillColorTertiaryBrush",
            "Pressed state of standard controls."),
        new("ControlFillDisabled", Theme.ControlFillDisabled, "ControlFillColorDisabledBrush",
            "Disabled state of standard controls."),
        new("ControlFillInputActive", Theme.ControlFillInputActive, "ControlFillColorInputActiveBrush",
            "Active/focused text input field backgrounds."),
    ];

    static readonly BrushEntry[] SurfaceBrushes =
    [
        new("SolidBackground", Theme.SolidBackground, "SolidBackgroundFillColorBaseBrush",
            "App and page-level background. The base layer of the visual hierarchy."),
        new("CardBackground", Theme.CardBackground, "CardBackgroundFillColorDefaultBrush",
            "Card and elevated surface backgrounds."),
        new("LayerFill", Theme.LayerFill, "LayerFillColorDefaultBrush",
            "Flyout, dialog, and pane backgrounds."),
        new("SubtleFill", Theme.SubtleFill, "SubtleFillColorSecondaryBrush",
            "Subtle control highlights and section backgrounds."),
        new("SmokeFill", Theme.SmokeFill, "SmokeFillColorDefaultBrush",
            "Semi-transparent overlay behind modal dialogs."),
    ];

    static readonly BrushEntry[] StrokeBrushes =
    [
        new("CardStroke", Theme.CardStroke, "CardStrokeColorDefaultBrush",
            "Borders on cards and elevated containers."),
        new("SurfaceStroke", Theme.SurfaceStroke, "SurfaceStrokeColorDefaultBrush",
            "Borders on flyouts, dialogs, and pane surfaces."),
        new("DividerStroke", Theme.DividerStroke, "DividerStrokeColorDefaultBrush",
            "Horizontal or vertical dividers between content sections."),
        new("ControlStroke", Theme.ControlStroke, "ControlStrokeColorDefaultBrush",
            "Borders on interactive controls at rest."),
        new("ControlStrokeSecondary", Theme.ControlStrokeSecondary, "ControlStrokeColorSecondaryBrush",
            "Bottom edge of controls for depth effect."),
    ];

    static readonly BrushEntry[] SignalBrushes =
    [
        new("SystemAttention", Theme.SystemAttention, "SystemFillColorAttentionBrush",
            "Informational indicators and badges."),
        new("SystemSuccess", Theme.SystemSuccess, "SystemFillColorSuccessBrush",
            "Success states and confirmations."),
        new("SystemCaution", Theme.SystemCaution, "SystemFillColorCautionBrush",
            "Warning states and cautionary indicators."),
        new("SystemCritical", Theme.SystemCritical, "SystemFillColorCriticalBrush",
            "Error states and critical alerts."),
        new("SystemNeutral", Theme.SystemNeutral, "SystemFillColorNeutralBrush",
            "Neutral status indicators."),
        new("SystemSolidNeutral", Theme.SystemSolidNeutral, "SystemFillColorSolidNeutralBrush",
            "Solid neutral fill for icons and non-text indicators."),
    ];

    static readonly BrushEntry[] SignalBackgroundBrushes =
    [
        new("SystemAttentionBackground", Theme.SystemAttentionBackground, "SystemFillColorAttentionBackgroundBrush",
            "Background for attention/info banners and bars."),
        new("SystemSuccessBackground", Theme.SystemSuccessBackground, "SystemFillColorSuccessBackgroundBrush",
            "Background for success banners and info bars."),
        new("SystemCautionBackground", Theme.SystemCautionBackground, "SystemFillColorCautionBackgroundBrush",
            "Background for warning banners and info bars."),
        new("SystemCriticalBackground", Theme.SystemCriticalBackground, "SystemFillColorCriticalBackgroundBrush",
            "Background for error banners and info bars."),
        new("SystemNeutralBackground", Theme.SystemNeutralBackground, "SystemFillColorNeutralBackgroundBrush",
            "Background for neutral status banners."),
        new("SystemSolidAttention", Theme.SystemSolidAttention, "SystemFillColorSolidAttentionBackgroundBrush",
            "Solid background for high-contrast attention surfaces."),
    ];

    public override Element Render()
    {
        return PageContent("Color",
            "The WinUI color system provides semantic tokens that automatically adapt across Light, Dark, and High Contrast themes. Use Theme tokens instead of hard-coded colors to ensure your app looks correct in every theme.",

            BrushSection("Text", "Use these brushes for text foreground colors.", TextBrushes),
            BrushSection("Accent Fill", "Accent colors for primary actions and interactive controls.", AccentBrushes),
            BrushSection("Control Fill", "Fill colors for the rest, hover, pressed, and disabled states of standard controls.", ControlFillBrushes),
            BrushSection("Surfaces & Backgrounds", "Background fills for app surfaces, cards, layers, and overlays.", SurfaceBrushes),
            BrushSection("Stroke & Border", "Border and divider colors for cards, controls, and content separation.", StrokeBrushes),
            BrushSection("System Signal", "Semantic status colors for success, warning, error, and informational states.", SignalBrushes),
            BrushSection("System Signal Backgrounds", "Background fills paired with signal colors for banners and info bars.", SignalBackgroundBrushes),

            ApplyingThemeColorsSection()
        );
    }

    static Element ApplyingThemeColorsSection() =>
        SampleCard("Applying Theme Colors",
            VStack(16,
                // Foreground
                TextBlock("Foreground")
                    .ApplyStyle("BodyStrongTextBlockStyle"),
                VStack(4,
                    TextBlock("Accent colored text").Foreground(Theme.Accent),
                    TextBlock("Primary text on any surface").Foreground(Theme.PrimaryText),
                    TextBlock("Secondary text for captions").Foreground(Theme.SecondaryText),
                    TextBlock("Tertiary text for placeholders").Foreground(Theme.TertiaryText),
                    TextBlock("Hyperlink-style accent text").Foreground(Theme.AccentText)
                ),

                // Background
                TextBlock("Background")
                    .ApplyStyle("BodyStrongTextBlockStyle"),
                VStack(8,
                    Border(TextBlock("SolidBackground — app page background").Foreground(Theme.PrimaryText).Margin(4))
                        .Background(Theme.SolidBackground)
                        .WithBorder(Theme.SurfaceStroke)
                        .CornerRadius(6),
                    Border(TextBlock("CardBackground — elevated card surface").Foreground(Theme.PrimaryText).Margin(4))
                        .Background(Theme.CardBackground)
                        .WithBorder(Theme.CardStroke)
                        .CornerRadius(6),
                    Border(TextBlock("LayerFill — flyout or dialog layer").Foreground(Theme.PrimaryText).Margin(4))
                        .Background(Theme.LayerFill)
                        .WithBorder(Theme.SurfaceStroke)
                        .CornerRadius(6),
                    Border(TextBlock("SubtleFill — subtle highlight area").Foreground(Theme.PrimaryText).Margin(4))
                        .Background(Theme.SubtleFill)
                        .WithBorder(Theme.DividerStroke)
                        .CornerRadius(6),
                    Border(TextBlock("Accent — primary action button fill").Foreground(Theme.Ref("TextOnAccentFillColorPrimaryBrush")).Margin(4))
                        .Background(Theme.Accent)
                        .CornerRadius(6)
                ),

                // Borders
                TextBlock("Border & Stroke")
                    .ApplyStyle("BodyStrongTextBlockStyle"),
                VStack(8,
                    Border(TextBlock("CardStroke border").Foreground(Theme.PrimaryText).Margin(4))
                        .Background(Theme.CardBackground)
                        .WithBorder(Theme.CardStroke)
                        .CornerRadius(6),
                    Border(TextBlock("SurfaceStroke border").Foreground(Theme.PrimaryText).Margin(4))
                        .Background(Theme.LayerFill)
                        .WithBorder(Theme.SurfaceStroke)
                        .CornerRadius(6),
                    Border(TextBlock("ControlStroke border").Foreground(Theme.PrimaryText).Margin(4))
                        .Background(Theme.ControlFill)
                        .WithBorder(Theme.ControlStroke)
                        .CornerRadius(6),
                    Border(VStack()).Height(1)
                        .Background(Theme.DividerStroke)
                        .HAlign(HorizontalAlignment.Stretch)
                ),

                // System signal usage
                TextBlock("System Signal Colors")
                    .ApplyStyle("BodyStrongTextBlockStyle"),
                VStack(8,
                    Border(HStack(8,
                        TextBlock("\uE946")
                            .Set(t => t.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"))
                            .Margin(4, 4, 0, 4)
                            .Foreground(Theme.SystemAttention),
                        TextBlock("Informational message").Foreground(Theme.PrimaryText)
                    ).Margin(4))
                        .Background(Theme.SystemAttentionBackground)
                        .WithBorder(Theme.SurfaceStroke)
                        .CornerRadius(6),
                    Border(HStack(8,
                        TextBlock("\uE930")
                            .Set(t => t.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"))
                            .Margin(4, 4, 0, 4)
                            .Foreground(Theme.SystemSuccess),
                        TextBlock("Operation completed successfully").Foreground(Theme.PrimaryText)
                    ).Margin(4))
                        .Background(Theme.SystemSuccessBackground)
                        .WithBorder(Theme.SurfaceStroke)
                        .CornerRadius(6),
                    Border(HStack(8,
                        TextBlock("\uE7BA")
                            .Set(t => t.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"))
                            .Margin(4, 4, 0, 4)
                            .Foreground(Theme.SystemCaution),
                        TextBlock("Proceed with caution").Foreground(Theme.PrimaryText)
                    ).Margin(4))
                        .Background(Theme.SystemCautionBackground)
                        .WithBorder(Theme.SurfaceStroke)
                        .CornerRadius(6),
                    Border(HStack(8,
                        TextBlock("\uEA39")
                            .Set(t => t.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"))
                            .Margin(4, 4, 0, 4)
                            .Foreground(Theme.SystemCritical),
                        TextBlock("A critical error has occurred").Foreground(Theme.PrimaryText)
                    ).Margin(4))
                        .Background(Theme.SystemCriticalBackground)
                        .WithBorder(Theme.SurfaceStroke)
                        .CornerRadius(6)
                )
            ),
            @"// Foreground
TextBlock(""Accent text"").Foreground(Theme.Accent)
TextBlock(""Primary"").Foreground(Theme.PrimaryText)
TextBlock(""Secondary"").Foreground(Theme.SecondaryText)
TextBlock(""Hyperlink"").Foreground(Theme.AccentText)

// Background
Border(content)
    .Background(Theme.CardBackground)
    .WithBorder(Theme.CardStroke)
    .CornerRadius(6)

Border(content)
    .Background(Theme.Accent)  // accent-filled button

// Border & Stroke
Border(content)
    .WithBorder(Theme.ControlStroke)

Border(VStack()).Height(1)
    .Background(Theme.DividerStroke)  // horizontal divider

// System Signal
Border(HStack(icon, message))
    .Background(Theme.SystemSuccessBackground)");

    static Element BrushSection(string title, string description, BrushEntry[] entries)
    {
        var sourceLines = string.Join("\n", entries.Select(e => $"Theme.{e.Token}  // {e.ResourceKey}"));

        return SampleCard(title,
            VStack(2,
                TextBlock(description)
                    .Foreground(Theme.SecondaryText)
                    .FontSize(13)
                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                    .Margin(0, 0, 0, 8),
                VStack(2, entries.Select(BrushRow).ToArray())
            ),
            sourceLines);
    }

    static Element BrushRow(BrushEntry entry) =>
        Grid(
            columns: [GridSize.Auto, GridSize.Px(240), GridSize.Auto, GridSize.Star()], rows: [GridSize.Auto],

            Border(VStack())
                .Background(entry.Color)
                .Width(32).Height(20)
                .CornerRadius(4)
                .WithBorder(Theme.SurfaceStroke)
                .VAlign(VerticalAlignment.Center)
                .Margin(0, 0, 8, 0)
                .Grid(column: 0),

            TextBlock($"Theme.{entry.Token}")
                .FontSize(13)
                .SemiBold()
                .Foreground(Theme.PrimaryText)
                .VAlign(VerticalAlignment.Center)
                .Grid(column: 1),

            TextBlock(entry.ResourceKey)
                .FontSize(11)
                .Foreground(Theme.TertiaryText)
                .Set(tb => tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas, monospace"))
                .VAlign(VerticalAlignment.Center)
                .Margin(0, 0, 12, 0)
                .Grid(column: 2),

            TextBlock(entry.Recommendation)
                .FontSize(12)
                .Foreground(Theme.SecondaryText)
                .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                .VAlign(VerticalAlignment.Center)
                .Grid(column: 3)
        ).Margin(0, 3, 0, 3);
}
