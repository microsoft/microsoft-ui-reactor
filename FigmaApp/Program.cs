// ═══════════════════════════════════════════════════════════
// FIGMA TRANSLATION SUMMARY
// Source: https://www.figma.com/design/NXvmCFnkYESqhSpEg4icEl?node-id=2017-2054
// Fidelity: Level 2
// Resolved: 28/32 visible elements
// TODOs: 4 items requiring manual review
//   - Video/media player placeholder (node 305940)
//   - Icon SVG images for cards (nodes 305948, 305956, 305964)
//   - App icon in title bar (decorative SVG)
// Hidden in Figma (excluded): Selector bars with opacity 0
// ═══════════════════════════════════════════════════════════

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

ReactorApp.Run<App>("FigmaApp", width: 1316, height: 865
#if DEBUG
    , devtools: true
#endif
);

class App : Component
{
    public override Element Render()
    {
        var controlCR = ThemeResource.CornerRadius("ControlCornerRadius");
        var overlayCR = ThemeResource.CornerRadius("OverlayCornerRadius");

        return VStack(0,
            // ── Title Bar ──
            TitleBar(controlCR),

            // ── Nav + Content ──
            HStack(0,
                // ── Side Nav ──
                SideNav(controlCR),

                // ── Content Area ──
                ContentArea(controlCR, overlayCR)
            ).HAlign(HorizontalAlignment.Stretch).VAlign(VerticalAlignment.Stretch)
        );
    }

    Element TitleBar(CornerRadius controlCR)
    {
        return Border(
            HStack(0,
                // ── App Icon & Title ──
                Border(
                    HStack(16,
                        TextBlock("\uE737")
                            .Set(tb => tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"))
                            .Set(tb => tb.FontSize = 16)
                            .AccessibilityHidden(),
                        Caption("App name")
                            .VAlign(VerticalAlignment.Center)
                    )
                ).Padding(0, 8, 0, 16)
                 .VAlign(VerticalAlignment.Center),

                // ── Spacer ──
                Border(Empty()).HAlign(HorizontalAlignment.Stretch),

                // ── Search Box ──
                Border(
                    HStack(12,
                        TextBlock("\uE721")
                            .Set(tb => tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"))
                            .Set(tb => tb.FontSize = 16)
                            .Foreground(Theme.SecondaryText)
                            .AccessibilityHidden(),
                        TextBlock("Search")
                            .Foreground(Theme.SecondaryText)
                    ).VAlign(VerticalAlignment.Center)
                )
                .Background(Theme.ControlFill)
                .WithBorder(Theme.ControlStroke, 1)
                .CornerRadius(controlCR.TopLeft)
                .MinWidth(200)
                .MinHeight(32)
                .Padding(16, 4, 16, 4)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center),

                // ── Spacer ──
                Border(Empty()).HAlign(HorizontalAlignment.Stretch),

                // ── Caption Controls (Person Picture) ──
                HStack(0,
                    Border(
                        Caption("JD")
                            .Center()
                    )
                    .Background(Theme.Ref("ControlFillColorQuarternaryBrush"))
                    .CornerRadius(12)
                    .Width(24).Height(24)
                    .Center()
                    .Margin(12)
                    .AutomationName("User profile")
                ).VAlign(VerticalAlignment.Center)
            )
        )
        .MinHeight(48)
        .Background(Theme.Ref("LayerFillColorDefaultBrush"))
        .HAlign(HorizontalAlignment.Stretch)
        .Landmark(AutomationLandmarkType.Custom)
        .AutomationName("Title bar");
    }

    Element SideNav(CornerRadius controlCR)
    {
        return Border(
            VStack(0,
                // ── Header ──
                Border(
                    VStack(0,
                        // Menu button
                        Border(
                            TextBlock("\uE700")
                                .Set(tb => tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"))
                                .Set(tb => tb.FontSize = 16)
                                .Center()
                        )
                        .MinWidth(48).MinHeight(40)
                        .AutomationName("Navigation menu"),

                        // Nav Items
                        VStack(0,
                            NavItem("\uEA37", "Text", isSelected: true, controlCR),
                            NavItem("\uEA37", "Text", isSelected: false, controlCR)
                        ).HAlign(HorizontalAlignment.Stretch)
                    )
                ).Padding(4, 0, 0, 0).HAlign(HorizontalAlignment.Stretch),

                // ── Spacer ──
                Border(Empty()).VAlign(VerticalAlignment.Stretch),

                // ── Footer ──
                Border(
                    VStack(0,
                        NavItem("\uE713", "Settings", isSelected: false, controlCR)
                    )
                ).Padding(0, 0, 4, 0).HAlign(HorizontalAlignment.Stretch)
            )
        )
        .MinWidth(280)
        .VAlign(VerticalAlignment.Stretch)
        .Landmark(AutomationLandmarkType.Navigation)
        .AutomationName("Side navigation");
    }

    Element NavItem(string icon, string text, bool isSelected, CornerRadius controlCR)
    {
        var item = HStack(12,
            TextBlock(icon)
                .Set(tb => tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"))
                .Set(tb => tb.FontSize = 16)
                .Center()
                .Margin(16, 0, 0, 0)
                .AccessibilityHidden(),
            TextBlock(text)
                .VAlign(VerticalAlignment.Center)
        )
        .MinHeight(40)
        .HAlign(HorizontalAlignment.Stretch);

        if (isSelected)
        {
            return Border(item)
                .Background(Theme.Ref("SubtleFillColorSecondaryBrush"))
                .CornerRadius(controlCR.TopLeft)
                .Margin(4, 0, 4, 0);
        }

        return Border(item)
            .CornerRadius(controlCR.TopLeft)
            .Margin(4, 0, 4, 0);
    }

    Element ContentArea(CornerRadius controlCR, CornerRadius overlayCR)
    {
        return Border(
            ScrollView(
                VStack(0,
                    // ── Hero Banner ──
                    HeroBanner(overlayCR),

                    // ── Card Row with Header ──
                    CardSection(controlCR, overlayCR)
                )
                .HAlign(HorizontalAlignment.Stretch)
            )
            .Set(sv => sv.HorizontalContentAlignment = HorizontalAlignment.Stretch)
        )
        .Background(Theme.LayerFill)
        .HAlign(HorizontalAlignment.Stretch)
        .VAlign(VerticalAlignment.Stretch)
        .Padding(48, 24, 0, 24)
        .Landmark(AutomationLandmarkType.Main)
        .AutomationName("Main content");
    }

    Element HeroBanner(CornerRadius overlayCR)
    {
        return HStack(24,
            // ── Title and caption ──
            VStack(24,
                TextBlock("This is a test app title text")
                    .ApplyStyle("TitleLargeTextBlockStyle")
                    .Set(tb => tb.TextWrapping = TextWrapping.WrapWholeWords)
                    .HeadingLevel(AutomationHeadingLevel.Level1),
                TextBlock("Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.")
                    .Set(tb => tb.TextWrapping = TextWrapping.WrapWholeWords)
            ).MinWidth(354),

            // ── Video placeholder ──
            Border(
                // TODO [Figma node 305940]: Video/media player placeholder
                Border(
                    TextBlock("\uF5B0")
                        .Set(tb => tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"))
                        .Set(tb => tb.FontSize = 24)
                        .Center()
                )
                .Width(44).Height(44)
                .CornerRadius(22)
                .Background(Theme.Ref("SolidBackgroundFillColorBaseBrush"))
                .Center()
                .AutomationName("Play video")
            )
            .Background(Theme.ControlFillSecondary)
            .CornerRadius(overlayCR.TopLeft)
            .MinHeight(200)
            .HAlign(HorizontalAlignment.Stretch)
        )
        .VAlign(VerticalAlignment.Center)
        .HAlign(HorizontalAlignment.Stretch)
        .Margin(0, 0, 0, 24);
    }

    Element CardSection(CornerRadius controlCR, CornerRadius overlayCR)
    {
        return VStack(4,
            // ── Section Heading ──
            Border(
                VStack(4,
                    SubHeading("Section title")
                        .HeadingLevel(AutomationHeadingLevel.Level2),
                    Border(Empty())
                        .Background(Theme.DividerStroke)
                        .Height(1)
                        .HAlign(HorizontalAlignment.Stretch)
                        .Margin(0, 20, 0, 0)
                )
            ).Padding(24, 0, 0, 0),

            // ── Card Layout ──
            HStack(12,
                Card("Caption text", "Small header",
                    "Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.",
                    "Button text", controlCR, overlayCR),
                Card("Caption text", "Small header",
                    "Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.",
                    "Button text", controlCR, overlayCR),
                Card("Caption text", "Small header",
                    "Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.",
                    "Button text", controlCR, overlayCR)
            )
        ).HAlign(HorizontalAlignment.Stretch);
    }

    Element Card(string caption, string header, string body, string buttonText,
        CornerRadius controlCR, CornerRadius overlayCR)
    {
        return Border(
            VStack(12,
                // TODO [Figma node 305948]: Card icon SVG placeholder
                Border(
                    TextBlock("\uE8B7")
                        .Set(tb => tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"))
                        .Set(tb => tb.FontSize = 16)
                        .Center()
                )
                .Width(32).Height(32)
                .Background(Theme.Accent)
                .CornerRadius(controlCR.TopLeft)
                .AccessibilityHidden(),

                Caption(caption)
                    .Foreground(Theme.SecondaryText),

                // Content
                // Note: Figma spacing was 7px, rounded to 8px (4px grid)
                VStack(8,
                    TextBlock(header)
                        .ApplyStyle("BodyLargeTextBlockStyle")
                        .SemiBold()
                        .Set(tb => tb.TextWrapping = TextWrapping.WrapWholeWords),
                    TextBlock(body)
                        .Set(tb => tb.TextWrapping = TextWrapping.WrapWholeWords)
                ),

                Button(buttonText, () => { })
                    .HAlign(HorizontalAlignment.Stretch)
                    .Margin(0, 24, 0, 0)
            )
        )
        .Background(Theme.CardBackground)
        .WithBorder(Theme.CardStroke, 1)
        .CornerRadius(overlayCR.TopLeft)
        .Padding(36)
        .Translation(0, 0, 32)
        .Set(b => b.Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow())
        .HAlign(HorizontalAlignment.Stretch);
    }
}