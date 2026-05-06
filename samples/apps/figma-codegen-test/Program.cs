// Figma Design Translation — Frame 3465089 (2001:21036)
// Idiomatic Reactor WinUI app with NavigationView, TitleBar, cards layout.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        var (searchText, setSearchText) = UseState("");
        var (selectedNav, setSelectedNav) = UseState<string?>("page1");
        var controlCR = ThemeResource.CornerRadius("ControlCornerRadius");

        return FlexColumn(
            // ── Title bar ───────────────────────────────────────────
            (TitleBar("App name") with
            {
                Content = (AutoSuggestBox(searchText, onTextChanged: setSearchText) with
                {
                    PlaceholderText = "Search"
                }).Width(200),

                RightHeader = PersonPicture().Initials("JD").Width(32).Height(32),
            }).Flex(shrink: 0),

            // ── Navigation + content ────────────────────────────────
            (NavigationView(
                [
                    NavItem("Text", icon: "\uE8A5", tag: "page1"),
                    NavItem("Text", icon: "\uE8A5", tag: "page2"),
                ],
                // Scrollable page content
                ScrollView(
                    VStack(
                        // ── Hero banner ─────────────────────────────
                        HStack(24,
                            VStack(24,
                                TextBlock("Test Title")
                                    .ApplyStyle("TitleLargeTextBlockStyle"),
                                TextBlock("Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.")
                                    .TextWrapping(TextWrapping.WrapWholeWords)
                            ),
                            // Video placeholder
                            Border(VStack())
                                .Width(300).Height(180)
                                .Background(SubtleFill)
                                .CornerRadius(controlCR.TopLeft)
                        ).Padding(24, 48, 24, 0),

                        // ── Section heading + divider ───────────────
                        VStack(4,
                            SubHeading("Section title"),
                            Border(VStack()).Height(1)
                                .Background(DividerStroke)
                                .HAlign(HorizontalAlignment.Stretch)
                        ).Padding(24, 24, 24, 0),

                        // ── Card row ────────────────────────────────
                        HStack(12,
                            Card("Caption text", "Small header",
                                "Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.",
                                controlCR),
                            Card("Caption text", "Small header",
                                "Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.",
                                controlCR),
                            Card("Caption text", "Small header",
                                "Amet minim mollit non deserunt ullamco est sit aliqua dolor do amet sint. Velit officia consequat duis enim velit mollit. Exercitation veniam consequat sunt nostrud amet.",
                                controlCR)
                        ).Padding(24, 12, 24, 24)
                    )
                )
            ) with
            {
                IsSettingsVisible = true,
                SelectedTag = selectedNav,
                OnSelectionChanged = tag => setSelectedNav(tag),
                PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            }).Flex(grow: 1)
        );
    }

    /// <summary>Renders a content card with image placeholder, caption, header, body, and action button.</summary>
    static Element Card(string caption, string header, string body, CornerRadius cornerRadius) =>
        Border(
            VStack(12,
                // Image placeholder
                Border(VStack())
                    .Height(120)
                    .Background(SubtleFill)
                    .CornerRadius(cornerRadius.TopLeft)
                    .HAlign(HorizontalAlignment.Stretch),
                Caption(caption),
                VStack(8,
                    TextBlock(header)
                        .ApplyStyle("BodyLargeTextBlockStyle").SemiBold(),
                    TextBlock(body)
                        .TextWrapping(TextWrapping.WrapWholeWords)
                ),
                Button("Button text", () => { })
            )
        )
        .Padding(36, 36, 36, 36)
        .Background(CardBackground)
        .WithBorder(CardStroke, 1)
        .CornerRadius(cornerRadius.TopLeft);
}
