// ═══════════════════════════════════════════════════════════
// FIGMA LIVE SYNC — Auto-generated from Figma
// Frame: Frame 3465089 (2001:21036)
// Generated: 2026-05-05 21:36:47
// DO NOT EDIT — this file is overwritten on each Figma change
// ═══════════════════════════════════════════════════════════

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

ReactorApp.Run<Frame3465089>("App name", width: 1316, height: 865
#if DEBUG
    , devtools: true
#endif
);

class Frame3465089 : Component
{
    public override Element Render()
    {
        var controlCR = ThemeResource.CornerRadius("ControlCornerRadius");
        var overlayCR = ThemeResource.CornerRadius("OverlayCornerRadius");

        return VStack(
                HStack(
                    VStack(
                        Border(
                            VStack(
                            VStack(
                                InfoBadge(),
                                InfoBadge())))
                            .Padding(0, 4, 0, 0),
                        Border(
                            VStack(
                            InfoBadge()))
                            .Padding(0, 0, 0, 4)),
                    VStack(
                        Border(
                            VStack(
                            HStack(24,
                                VStack(24,
                                    TextBlock("Test Title testing").ApplyStyle("TitleLargeTextBlockStyle"),
                                    TextBlock("Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.")
                                        .TextWrapping(TextWrapping.WrapWholeWords))),
                            VStack(4,
                                Border(
                                    VStack(4,
                                    HStack(48,
                                        VStack(12,
                                            HStack(12,
                                                SubHeading("Section title")))),
                                    Border(
                                        VStack(
                                        Border(VStack()).Height(1)
                                            .Background(DividerStroke)
                                            .HAlign(HorizontalAlignment.Stretch)))
                                        .Padding(0, 20, 0, 0)))
                                    .Padding(0, 24, 0, 0),
                                HStack(12,
                                    Border(
                                        VStack(12,
                                        VStack(
                                                /* [VECTOR] Rectangle */
                                                VStack(),
                                                VStack(
                                                    Border(VStack()).Width(8).Height(8),
                                                    Border(VStack()).Width(8).Height(8),
                                                    VStack(
                                                        Border(VStack()).Width(8).Height(8),
                                                        Border(VStack()).Width(8).Height(8)),
                                                    Border(VStack()).Width(8).Height(8),
                                                    VStack(
                                                        Border(VStack()).Width(8).Height(8),
                                                        Border(VStack()).Width(8).Height(8)),
                                                    Border(VStack()).Width(8).Height(8),
                                                    VStack(
                                                        Border(VStack()).Width(8).Height(8),
                                                        Border(VStack()).Width(8).Height(8)),
                                                    Border(VStack()).Width(8).Height(8))),
                                        Caption("Caption text"),
                                        VStack(8,
                                            TextBlock("Small header").ApplyStyle("BodyLargeTextBlockStyle").SemiBold(),
                                            TextBlock("Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.")
                                                .TextWrapping(TextWrapping.WrapWholeWords)),
                                        Button("Button text", () => { })))
                                        .Padding(36, 36, 36, 100)
                                        .Background(CardBackground)
                                        .WithBorder(CardStroke, 1)
                                        .CornerRadius(overlayCR.TopLeft),
                                    Border(
                                        VStack(12,
                                        VStack(
                                                /* [VECTOR] Rectangle */
                                                VStack(),
                                                VStack(
                                                    Border(VStack()).Width(8).Height(8),
                                                    Border(VStack()).Width(8).Height(8),
                                                    VStack(
                                                        Border(VStack()).Width(8).Height(8),
                                                        Border(VStack()).Width(8).Height(8)),
                                                    Border(VStack()).Width(8).Height(8),
                                                    VStack(
                                                        Border(VStack()).Width(8).Height(8),
                                                        Border(VStack()).Width(8).Height(8)),
                                                    Border(VStack()).Width(8).Height(8),
                                                    VStack(
                                                        Border(VStack()).Width(8).Height(8),
                                                        Border(VStack()).Width(8).Height(8)),
                                                    Border(VStack()).Width(8).Height(8))),
                                        Caption("Caption text"),
                                        VStack(8,
                                            TextBlock("Small header").ApplyStyle("BodyLargeTextBlockStyle").SemiBold(),
                                            TextBlock("Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.")
                                                .TextWrapping(TextWrapping.WrapWholeWords)),
                                        Button("Button text", () => { })))
                                        .Padding(36, 36, 36, 100)
                                        .Background(CardBackground)
                                        .WithBorder(CardStroke, 1)
                                        .CornerRadius(overlayCR.TopLeft),
                                    Border(
                                        VStack(12,
                                        VStack(
                                                /* [VECTOR] Rectangle */
                                                VStack(),
                                                VStack(
                                                    Border(VStack()).Width(8).Height(8),
                                                    Border(VStack()).Width(8).Height(8),
                                                    VStack(
                                                        Border(VStack()).Width(8).Height(8),
                                                        Border(VStack()).Width(8).Height(8)),
                                                    Border(VStack()).Width(8).Height(8),
                                                    VStack(
                                                        Border(VStack()).Width(8).Height(8),
                                                        Border(VStack()).Width(8).Height(8)),
                                                    Border(VStack()).Width(8).Height(8),
                                                    VStack(
                                                        Border(VStack()).Width(8).Height(8),
                                                        Border(VStack()).Width(8).Height(8)),
                                                    Border(VStack()).Width(8).Height(8))),
                                        Caption("Caption text"),
                                        VStack(8,
                                            TextBlock("Small header").ApplyStyle("BodyLargeTextBlockStyle").SemiBold(),
                                            TextBlock("Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.")
                                                .TextWrapping(TextWrapping.WrapWholeWords)),
                                        Button("Button text", () => { })))
                                        .Padding(36, 36, 36, 100)
                                        .Background(CardBackground)
                                        .WithBorder(CardStroke, 1)
                                        .CornerRadius(overlayCR.TopLeft)))))
                            .Padding(24, 48, 24, 0))),
                TitleBar("Search"))
                .CornerRadius(overlayCR.TopLeft);
    }
}
