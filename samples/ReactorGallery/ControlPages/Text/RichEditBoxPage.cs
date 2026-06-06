using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class RichEditBoxPage : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("Type here to edit rich text content...");
        var (charCount, setCharCount) = UseState(0);

        return ScrollView(
            VStack(16,
                PageHeader("RichEditBox", "A rich text editing control with formatting support."),

                SampleCard("Basic RichEditBox",
                    RichEditBox(text, s => { setText(s); setCharCount(s.Length); })
                        .Width(400).Height(150),
                    """
                    var (text, setText) = UseState("Type here...");
                    RichEditBox(text, setText).Width(400).Height(150)
                    """),

                SampleCard("With Character Count",
                    VStack(8,
                        RichEditBox(text, s => { setText(s); setCharCount(s.Length); })
                            .Width(400).Height(120),
                        TextBlock($"Characters: {charCount}").Foreground(Theme.SecondaryText).FontSize(12)
                    ),
                    """
                    RichEditBox(text, s => { setText(s); setCharCount(s.Length); })
                    Text($"Characters: {charCount}")
                    """)
            ).Margin(36, 24, 36, 36)
        );
    }
}
