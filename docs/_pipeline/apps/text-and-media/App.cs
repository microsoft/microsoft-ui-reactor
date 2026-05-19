using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<TextAndMediaApp>("Text and Media", width: 720, height: 1200
#if DEBUG
    , preview: true
#endif
);

// <snippet:text-variants>
class TextVariantsDemo : Component
{
    public override Element Render() => VStack(8,
        Heading("Heading — page or section title"),
        SubHeading("SubHeading — region header"),
        TextBlock("Body text. The default size and weight for prose."),
        Caption("Caption — secondary metadata, dates, labels.")
    ).Padding(24);
}
// </snippet:text-variants>

// <snippet:textblock-modifiers>
class TextBlockModifiersDemo : Component
{
    public override Element Render() => VStack(8,
        TextBlock("Bold + sized").Bold().FontSize(18),
        TextBlock("Selectable so the user can copy.").Selectable(),
        TextBlock(
            "A long paragraph that demonstrates wrapping behavior. " +
            "Without TextWrapping, content stays on one line and is " +
            "clipped or scrolls. With TextWrapping.Wrap, the block " +
            "flows across multiple lines inside its width.")
            .TextWrapping()
            .MaxLines(2)
            .TextTrimming(Microsoft.UI.Xaml.TextTrimming.WordEllipsis)
            .Width(320)
    ).Padding(24);
}
// </snippet:textblock-modifiers>

// <snippet:rich-text>
class RichTextDemo : Component
{
    public override Element Render() => VStack(8,
        SubHeading("Inline-formatted prose"),
        RichTextBlock([
            Paragraph(
                Run("Tap the "),
                Hyperlink("docs",
                    new Uri("https://learn.microsoft.com/windows/apps/")),
                Run(" to keep reading.")),
            Paragraph(
                Run("Reactor builds the paragraph tree from value-typed " +
                    "records. No XAML inlines, no DataTemplate."))
        ]).LineHeight(22).Width(420)
    ).Padding(24);
}
// </snippet:rich-text>

// <snippet:rich-edit>
class RichEditDemo : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState(
            "Edit me. RichEditBox supports paste-with-formatting, " +
            "spell-check, and Enter for new paragraphs.");

        return VStack(8,
            SubHeading("RichEditBox"),
            RichEditBox(text, setText)
                .AcceptsReturn()
                .IsSpellCheckEnabled()
                .TextWrapping()
                .Height(160).Width(420)
        ).Padding(24);
    }
}
// </snippet:rich-edit>

// <snippet:markdown>
class MarkdownDemo : Component
{
    public override Element Render()
    {
        const string source =
            "# Release notes\n\n" +
            "Reactor **0.42** ships:\n\n" +
            "- Compositor animations via `UseAnimation`.\n" +
            "- A new [Markdown](https://example.com) renderer.\n" +
            "- Bug fixes for `LazyVStack` keyed reorder.\n\n" +
            "> Migration guide lives in the spec.\n";

        return VStack(8,
            SubHeading("Markdown"),
            Markdown(source)
        ).Padding(24).Width(440);
    }
}
// </snippet:markdown>

// <snippet:image>
class ImageDemo : Component
{
    public override Element Render() => VStack(8,
        SubHeading("Image"),
        // Resource Uri — ms-appx:// for packaged assets, file:// for disk,
        // https:// for remote.
        Image("ms-appx:///Assets/StoreLogo.png")
            .Width(96).Height(96),
        TextBlock("Stretch.UniformToFill for cover art; " +
                  "ImageFailed to detect missing assets.").Opacity(0.6)
    ).Padding(24);
}
// </snippet:image>

// <snippet:media-player>
class MediaPlayerDemo : Component
{
    public override Element Render() => VStack(8,
        SubHeading("MediaPlayerElement"),
        MediaPlayerElement(
            "https://learn.microsoft.com/en-us/windows/apps/design/" +
            "controls/images/ic_fluent_play_24_regular.svg")
            .Width(420).Height(240)
            .Set(m =>
            {
                m.AreTransportControlsEnabled = true;
                m.AutoPlay = false;
            }),
        TextBlock("Use AreTransportControlsEnabled for play/pause UI.")
            .Opacity(0.6)
    ).Padding(24);
}
// </snippet:media-player>

// <snippet:webview>
class WebViewDemo : Component
{
    public override Element Render()
    {
        var (loaded, setLoaded) = UseState(false);

        return VStack(8,
            SubHeading("WebView2"),
            WebView2(new Uri("about:blank"))
                .NavigationCompleted(_ => setLoaded(true))
                .Width(420).Height(240),
            TextBlock(loaded ? "Loaded." : "Loading…").Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:webview>

// <snippet:map-control>
class MapControlDemo : Component
{
    public override Element Render() => VStack(8,
        SubHeading("MapControl"),
        // Token blank — replace with a real Bing Maps key for tile fetch.
        // Without a token the control renders the grid background only.
        MapControl(mapServiceToken: null, zoomLevel: 4)
            .Width(420).Height(240)
    ).Padding(24);
}
// </snippet:map-control>

class TextAndMediaApp : Component
{
    public override Element Render() => ScrollView(
        VStack(24,
            Heading("Text and Media"),
            Component<TextVariantsDemo>(),
            Component<TextBlockModifiersDemo>(),
            Component<RichTextDemo>(),
            Component<RichEditDemo>(),
            Component<MarkdownDemo>(),
            Component<ImageDemo>(),
            Component<MediaPlayerDemo>(),
            Component<WebViewDemo>(),
            Component<MapControlDemo>()
        ).Padding(24)
    );
}
