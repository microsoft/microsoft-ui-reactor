using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Media;

class WebView2Page : Component
{
    // One source of truth per card. `static readonly` matters for the two Uris: passing
    // `new Uri(...)` straight to UseState re-allocates on every render for a value the hook
    // only reads on the first one (REACTOR_HOOKS_013).
    const string DefaultUrl = "https://learn.microsoft.com/windows/apps/";
    static readonly Uri DefaultUri = new Uri(DefaultUrl);
    static readonly Uri LearnUri = new Uri("https://learn.microsoft.com");
    static readonly Uri BingUri = new Uri("https://www.bing.com");

    /// <summary>
    /// The URL the box currently holds, or <c>null</c> while it is not one yet. Returning null
    /// rather than throwing is the point: <c>new Uri(text)</c> throws on half-typed input, and a
    /// throw out of <c>Render()</c> replaces the whole page with the error boundary.
    /// The scheme check keeps <c>file:</c> and <c>javascript:</c> — both of which parse — out of a
    /// control that is here to demonstrate loading a web page.
    /// </summary>
    static Uri? ParseWebUrl(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;

    public override Element Render()
    {
        // Each card owns its state. Sharing one slot across two cards makes the preset buttons
        // below silently retarget the card above them.
        var (urlText, setUrlText) = UseState(DefaultUrl);
        var (loadedUrl, setLoadedUrl) = UseState(DefaultUri);
        var (presetUrl, setPresetUrl) = UseState(LearnUri);

        // What the box holds right now; null while it is mid-edit or malformed.
        var target = ParseWebUrl(urlText);

        return ScrollView(
            VStack(16,
                PageHeader("WebView2",
                    "A control that hosts web content using the Edge rendering engine."),

                SampleCard("Load URL",
                    VStack(8,
                        HStack(8,
                            TextBox(urlText, s => setUrlText(s), placeholderText: "https://example.com")
                                .Width(400)
                                .UrlInput(),
                            Button("Go", () => { if (target is not null) setLoadedUrl(target); })
                                .IsEnabled(target is not null)
                                .VAlign(VerticalAlignment.Center)
                        ),
                        target is null
                            ? Caption("Enter an absolute http:// or https:// URL.")
                                .Foreground(Theme.SystemCritical)
                            : Caption($"Showing {loadedUrl}").Foreground(Theme.SecondaryText),
                        WebView2(loadedUrl).Width(600).Height(400)
                    ),
                    sourceCode: @"
// ── type members ──
// new Uri(text) throws on half-typed input, and a throw out of Render() replaces the
// whole page with the error boundary — so parse first and only commit what parses.
static Uri? ParseWebUrl(string text) =>
    Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        ? uri : null;

const string DefaultUrl = ""https://learn.microsoft.com/windows/apps/"";
static readonly Uri DefaultUri = new Uri(DefaultUrl);

// ── inside Render() ──
var (urlText, setUrlText) = UseState(DefaultUrl);
var (loadedUrl, setLoadedUrl) = UseState(DefaultUri);
var target = ParseWebUrl(urlText);

VStack(8,
    HStack(8,
        TextBox(urlText, s => setUrlText(s), ""https://example.com"").Width(400).UrlInput(),
        Button(""Go"", () => { if (target is not null) setLoadedUrl(target); })
            .IsEnabled(target is not null)),
    WebView2(loadedUrl).Width(600).Height(400))
"),

                SampleCard("WebView2 with Preset URLs",
                    VStack(8,
                        HStack(8,
                            Button("Microsoft Learn", () => setPresetUrl(LearnUri)),
                            Button("Bing", () => setPresetUrl(BingUri))
                        ),
                        WebView2(presetUrl).Width(600).Height(300)
                    ),
                    sourceCode: @"
// ── type members ──
static readonly Uri LearnUri = new Uri(""https://learn.microsoft.com"");
static readonly Uri BingUri = new Uri(""https://www.bing.com"");

// ── inside Render() ──
// Its own state slot: a card sharing one with its neighbour drives the neighbour too.
var (presetUrl, setPresetUrl) = UseState(LearnUri);

VStack(8,
    HStack(8,
        Button(""Microsoft Learn"", () => setPresetUrl(LearnUri)),
        Button(""Bing"", () => setPresetUrl(BingUri))),
    WebView2(presetUrl).Width(600).Height(300))
")
            ).Margin(36, 24, 36, 36)
        );
    }
}
