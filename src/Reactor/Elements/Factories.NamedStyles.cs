using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor;

// Named-style factories. Spec 039 §17.5 and §14 #6.
//
//   Card(child)                                       — preset BorderElement
//   Title / Subtitle / Body / BodyStrong / BodyLarge  — WinUI 3 type-ramp
public static partial class Factories
{
    // ── §17.5 Card factory ─────────────────────────────────────────────

    /// <summary>
    /// Renders <paramref name="child"/> inside a card-styled <see cref="BorderElement"/>.
    /// Defaults match the canonical WinUI card: 8px corner radius, ~16px
    /// padding, theme-aware <c>CardBackgroundFillColorDefaultBrush</c> +
    /// <c>CardStrokeColorDefaultBrush</c> 1px stroke. Re-themes on light /
    /// dark / contrast switches because the brushes resolve through
    /// <see cref="Core.ThemeRef"/>.
    /// </summary>
    /// <remarks>
    /// Override any preset by chaining a fluent on the returned border:
    /// <c>Card(child).Padding(24).CornerRadius(16).Background(Theme.SubtleFill)</c>.
    /// </remarks>
    public static BorderElement Card(Element child) =>
        Border(child)
            .Background(Core.Theme.CardBackground)
            .WithBorder(Core.Theme.CardStroke, thickness: 1)
            .CornerRadius(8)
            .Padding(16);

    // ── §14 #6 / §17.6 Type-ramp factories (WinUI 3 TextBlock styles) ──
    // Spec §17.6 explicitly forbids the two-ways trap: type-ramp lives
    // ONLY as factories, never as fluents. Chained .ApplyStyle / .Bold etc.
    // overrides are still legal because they layer on the returned element.

    /// <summary>WinUI 3 <c>TitleTextBlockStyle</c> — 28px Semibold heading.</summary>
    public static TextBlockElement Title(string content) =>
        TextBlock(content).ApplyStyle("TitleTextBlockStyle");

    /// <summary>WinUI 3 <c>SubtitleTextBlockStyle</c> — 20px Semibold sub-heading.</summary>
    public static TextBlockElement Subtitle(string content) =>
        TextBlock(content).ApplyStyle("SubtitleTextBlockStyle");

    /// <summary>WinUI 3 <c>BodyTextBlockStyle</c> — 14px regular body text.</summary>
    public static TextBlockElement Body(string content) =>
        TextBlock(content).ApplyStyle("BodyTextBlockStyle");

    /// <summary>WinUI 3 <c>BodyStrongTextBlockStyle</c> — 14px Semibold body text.</summary>
    public static TextBlockElement BodyStrong(string content) =>
        TextBlock(content).ApplyStyle("BodyStrongTextBlockStyle");

    /// <summary>WinUI 3 <c>BodyLargeTextBlockStyle</c> — 18px regular body text.</summary>
    public static TextBlockElement BodyLarge(string content) =>
        TextBlock(content).ApplyStyle("BodyLargeTextBlockStyle");
}
