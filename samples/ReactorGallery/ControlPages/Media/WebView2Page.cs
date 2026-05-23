using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Media;

class WebView2Page : Component
{
    public override Element Render()
    {
        var (url, setUrl) = UseState("https://learn.microsoft.com/windows/apps/");

        return ScrollView(
            VStack(16,
                PageHeader("WebView2",
                    "A control that hosts web content using the Edge rendering engine."),

                SampleCard("Load URL",
                    VStack(8,
                        TextBox(url, s => setUrl(s), placeholder: "Enter URL").Width(400),
                        WebView2(new Uri(url)).Width(600).Height(400)
                    ),
                    @"WebView2(new Uri(""https://learn.microsoft.com""))
    .Width(600).Height(400)"),

                SampleCard("WebView2 with Preset URLs",
                    VStack(8,
                        HStack(8,
                            Button("Microsoft Learn", () => setUrl("https://learn.microsoft.com")),
                            Button("Bing", () => setUrl("https://www.bing.com"))
                        ),
                        WebView2(new Uri(url)).Width(600).Height(300)
                    ),
                    @"Button(""Learn"", () => setUrl(""https://learn.microsoft.com""))
WebView2(new Uri(url)).Width(600).Height(300)")
            ).Margin(36, 24, 36, 36)
        );
    }
}
