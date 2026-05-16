using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Layout;

/// <summary>
/// Phase 8.1 — exercises the spec-039 §17.5 <see cref="Factories.Card"/>
/// factory. The Card factory returns a preset <see cref="BorderElement"/>
/// with theme-aware <c>CardBackgroundFillColorDefaultBrush</c> +
/// <c>CardStrokeColorDefaultBrush</c> brushes that re-resolve on
/// light / dark / high-contrast switches.
/// </summary>
class CardPage : Component
{
    // Segoe Fluent Icons "Mail" glyph (U+E715). Kept as a constant for
    // readability — call sites reference MailGlyph instead of embedding
    // the literal character inline.
    const string MailGlyph = "";

    public override Element Render()
    {
        return PageContent("Card",
            "A theme-aware preset Border with the WinUI card brushes, 8px corner radius, and 16px padding. Override any preset by chaining a fluent on the returned border.",

            SampleCard("Card with icon, heading, and body",
                Card(
                    HStack(12,
                        // Icon — Segoe Fluent Icons glyph rendered in a coloured circle.
                        Border(
                            TextBlock(MailGlyph)
                                .FontSize(20)
                                .Foreground(Theme.AccentText)
                                .Set(tb => tb.FontFamily = new FontFamily("Segoe Fluent Icons"))
                                .HAlign(HorizontalAlignment.Center)
                                .VAlign(VerticalAlignment.Center)
                        )
                        .Background(Theme.SubtleFill)
                        .CornerRadius(20)
                        .Width(40).Height(40)
                        .VAlign(VerticalAlignment.Top),
                        // Heading + body.
                        VStack(4,
                            BodyStrong("New message"),
                            Body("You have 3 unread items in your inbox.").Foreground(Theme.SecondaryText)
                        )
                    )
                ).Width(360),
                sourceCode: @"Card(
    HStack(12,
        Border(TextBlock("""")...)   // 1. icon
            .Background(Theme.SubtleFill)
            .CornerRadius(20).Width(40).Height(40),
        VStack(4,
            BodyStrong(""New message""),            // 2. heading
            Body(""You have 3 unread items..."")    // 3. body
                .Foreground(Theme.SecondaryText))
    )
)"),

            SampleCard("Override defaults via chained fluents",
                Card(
                    VStack(8,
                        BodyStrong("Custom padding & radius"),
                        Body("Card(child) returns a BorderElement, so any Border fluent (Padding, CornerRadius, Background, etc.) chains as normal.")
                            .Foreground(Theme.SecondaryText)
                    )
                ).Padding(24).CornerRadius(16).Width(360),
                sourceCode: @"Card(child).Padding(24).CornerRadius(16)")
        );
    }
}
