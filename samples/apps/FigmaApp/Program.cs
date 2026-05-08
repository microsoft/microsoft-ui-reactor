// ═══════════════════════════════════════════════════════════
// FIGMA TRANSLATION SUMMARY
// Source: https://www.figma.com/design/NXvmCFnkYESqhSpEg4icEl?node-id=2017-2054
// Fidelity: Level 2
// Resolved: 25/28 visible elements
// TODOs: 2 items requiring manual review
//   - Video play button placeholder (decorative media control)
//   - Card icons are SVG images (using placeholder icon glyph)
// ═══════════════════════════════════════════════════════════

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

ReactorApp.Run<FigmaTestApp>("FigmaApp", width: 1316, height: 865
#if DEBUG
    , devtools: true
#endif
);

class FigmaTestApp : Component
{
    public override Element Render()
    {
        var (selectedTag, setSelectedTag) = UseState("item1");

        var controlCR = ThemeResource.CornerRadius("ControlCornerRadius");
        var overlayCR = ThemeResource.CornerRadius("OverlayCornerRadius");

        // ── Side Nav Items ──
        var navItems = new[]
        {
            NavItem("Text", tag: "item1") with { IconElement = FontIcon("\uE80F") },
            NavItem("Text", tag: "item2") with { IconElement = FontIcon("\uE8A1") },
        };

        // ── Hero Banner ──
        var heroTitleAndCaption = VStack(24,
            TextBlock("This is a test app title text")
                .ApplyStyle("TitleLargeTextBlockStyle"),
            TextBlock("Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.")
                .TextWrapping(Microsoft.UI.Xaml.TextWrapping.WrapWholeWords)
        ).MinWidth(354);

        // Video placeholder
        var videoPlaceholder = Border(
            // TODO [Figma node 2017:2055]: Video play button — replace with actual media
            Border(
                TextBlock("\uE768")
                    .Set(tb => tb.FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"])
                    .Foreground(PrimaryText)
                    .Center()
            )
            .Width(44).Height(44)
            .CornerRadius(22)
            .Background(Ref("SolidBackgroundFillColorBaseBrush"))
            .WithBorder(Ref("SurfaceStrokeColorDefaultBrush"), 4)
            .Center()
        )
        .MinHeight(300)
        .HAlign(HorizontalAlignment.Stretch)
        .Background(CardBackground)
        .CornerRadius(overlayCR.TopLeft);

        var heroBanner = HStack(24,
            heroTitleAndCaption,
            videoPlaceholder
        ).MinWidth(924).VAlign(VerticalAlignment.Center);

        // ── Section Heading with Divider ──
        var sectionHeading = VStack(4,
            Border(
                SubHeading("Section title")
            ).Padding(22, 0, 0, 0),
            Border(VStack())
                .Background(DividerStroke)
                .Height(1)
                .HAlign(HorizontalAlignment.Stretch)
        ).MinWidth(924);

        // ── Cards ──
        Element MakeCard(string captionText, string headerText, string bodyText, string iconGlyph) =>
            Border(
                VStack(12,
                    // Card icon placeholder
                    Border(
                        TextBlock(iconGlyph)
                            .Set(tb => tb.FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"])
                            .Foreground(Accent)
                            .Center()
                    ).Width(32).Height(32),
                    Caption(captionText).Foreground(SecondaryText),
                    VStack(7,
                        TextBlock(headerText)
                            .ApplyStyle("BodyLargeTextBlockStyle")
                            .SemiBold(),
                        TextBlock(bodyText)
                            .TextWrapping(Microsoft.UI.Xaml.TextWrapping.WrapWholeWords)
                            .Foreground(SecondaryText)
                    ),
                    Button("Button text", () => { })
                        .HAlign(HorizontalAlignment.Stretch)
                )
            )
            .MinWidth(300)
            .Background(CardBackground)
            .WithBorder(CardStroke, 1)
            .CornerRadius(overlayCR.TopLeft)
            .Padding(36, 36, 36, 100)
            .Translation(0, 0, 2)
            .Set(b => b.Shadow = new ThemeShadow());

        var cardRow = HStack(12,
            MakeCard("Caption text", "Small header",
                "Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.",
                "\uE790"),
            MakeCard("Caption text", "Small header",
                "Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.",
                "\uE8B7"),
            MakeCard("Caption text", "Small header",
                "Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.",
                "\uE943")
        );

        // ── Scrolling Content ──
        var scrollContent = ScrollView(
            Border(
                VStack(0,
                    heroBanner,
                    sectionHeading,
                    cardRow
                )
            ).Padding(24, 46, 24, 0)
        ).Set(sv => sv.HorizontalContentAlignment = HorizontalAlignment.Stretch);

        // ── NavigationView Shell ──
        var navView = (NavigationView(
            navItems,
            content: scrollContent
        ) with
        {
            SelectedTag = selectedTag,
            IsPaneOpen = true,
            IsSettingsVisible = true,
            OnSelectionChanged = tag =>
            {
                if (tag is not null)
                    setSelectedTag(tag);
            },
        })
        .Set(nv =>
        {
            nv.OpenPaneLength = 280;
            nv.IsPaneToggleButtonVisible = true;
            nv.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        });

        // ── Title Bar ──
        var titleBar = TitleBar("App name") with
        {
            Content = AutoSuggestBox("", v => { })
                .Set(box =>
                {
                    box.PlaceholderText = "Search";
                    box.QueryIcon = new SymbolIcon(Symbol.Find);
                })
                .Width(320),
            RightHeader = PersonPicture()
                .Set(pp => pp.DisplayName = "JD")
                .Width(24).Height(24),
        };

        // ── Root Layout ──
        return Border(
            Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Auto, GridSize.Star()],

                titleBar.Grid(row: 0),
                navView.Grid(row: 1)
            )
        ).Backdrop(BackdropKind.Mica);
    }
}