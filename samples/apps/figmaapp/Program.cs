// Figma Design Translation — Frame 3465089 (2001:21036)
// NavigationView app with TitleBar, hero banner, and card layout.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<FigmaApp>("App name", width: 1316, height: 865
#if DEBUG
    , devtools: true
#endif
);

class FigmaApp : Component
{
    public override Element Render()
    {
        var (searchText, setSearchText) = UseState("");
        var (selectedNav, setSelectedNav) = UseState<string?>("page1");

        return Border(
            Grid(
                columns: ["*"], rows: ["Auto", "*"],

                // ── Title Bar (1316×48) ──────────────────────────────────
                (TitleBar("App name") with
                {
                    Content = AutoSuggestBox(searchText, setSearchText)
                        .Width(494)
                        .OnMount(el =>
                        {
                            var asb = (Microsoft.UI.Xaml.Controls.AutoSuggestBox)el;
                            asb.PlaceholderText = "Search";
                            asb.QueryIcon = new SymbolIcon(Symbol.Find);
                        }),
                    RightHeader = PersonPicture().Initials("JD").Width(24).Height(24),
                }).Grid(row: 0),

                // ── Nav + Content (1316×817) ─────────────────────────────
                (NavigationView(
                    [
                        NavItem("Text", icon: "\uE8A5", tag: "page1"),
                        NavItem("Text", icon: "\uE8A5", tag: "page2"),
                    ],
                    content: ScrollView(
                        VStack(0,
                            HeroBanner(),
                            CardSection()
                        ).Padding(24, 46, 24, 0)
                    )
                ) with
                {
                    SelectedTag = selectedNav,
                    OnSelectionChanged = tag => { if (tag != null) setSelectedNav(tag); },
                    PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
                    IsSettingsVisible = true,
                })
                .Set(nv =>
                {
                    nv.OpenPaneLength = 280;
                    nv.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
                })
                .Grid(row: 1)
            )
        ).Background(Theme.SolidBackground);
    }

    /// <summary>Hero banner: title + description left, video placeholder right (gap=24).</summary>
    static Element HeroBanner() =>
        HStack(24,
            // Title and caption column (354 wide, gap=24)
            VStack(24,
                TextBlock("Section Title Test Change")
                    .ApplyStyle("TitleLargeTextBlockStyle")
                    .TextWrapping(TextWrapping.WrapWholeWords),
                TextBlock("Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.")
                    .TextWrapping(TextWrapping.WrapWholeWords)
            ).Width(354),

            // Video placeholder (546×307, r=8) with centered play button
            Border(
                Border(VStack())
                    .Width(48).Height(48)
                    .CornerRadius(24)
                    .Background(Theme.SmokeFill)
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center)
            )
            .Width(546).Height(307)
            .Background(Theme.SubtleFill)
            .CornerRadius(8)
        );

    /// <summary>Section heading with divider, then 3 content cards (gap=4).</summary>
    static Element CardSection() =>
        VStack(4,
            // Heading (gap=4, pad=0,22,0,0)
            VStack(4,
                SubHeading("Section title"),
                // Divider wrapper (pad=0,20,0,0) with 1px line
                Border(
                    Border(VStack())
                        .Height(1)
                        .Background(Theme.DividerStroke)
                        .HAlign(HorizontalAlignment.Stretch)
                ).Padding(0, 20, 0, 0)
            ).Padding(0, 22, 0, 0),

            // Card layout (gap=12)
            HStack(12,
                ContentCard("Caption text", "Small header",
                    "Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet."),
                ContentCard("Caption text", "Small header",
                    "Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet."),
                ContentCard("Caption text", "Small header",
                    "Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.")
            )
        );

    /// <summary>Card with icon, caption, header, body, and action button (300 wide, r=7).</summary>
    static Element ContentCard(string caption, string header, string body) =>
        Border(
            VStack(12,
                // Icon placeholder (32×32, r=4)
                Border(VStack())
                    .Width(32).Height(32)
                    .CornerRadius(4)
                    .Background(Theme.SubtleFill),
                // Caption tag
                Caption(caption).Foreground(Theme.SecondaryText),
                // Content (gap=7)
                VStack(7,
                    TextBlock(header).FontSize(18).SemiBold(),
                    TextBlock(body).TextWrapping(TextWrapping.WrapWholeWords)
                ),
                // Action button
                Button("Button text", () => { })
            )
        )
        .Width(300)
        .Padding(36, 36, 36, 100)
        .Background(Theme.CardBackground)
        .WithBorder(Theme.CardStroke, 1)
        .CornerRadius(7);
}
