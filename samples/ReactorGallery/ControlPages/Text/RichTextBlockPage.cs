using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor;

class RichTextBlockPage : Component
{
    public override Element Render()
    {
        var (fontSize, setFontSize) = UseState(14.0);

        return ScrollView(
            VStack(16,
                PageHeader("RichTextBlock", "Displays formatted, read-only rich text."),

                SampleCard("Basic RichTextBlock",
                    RichTextBlock("This is a simple rich text block displaying read-only content.").FontSize(fontSize),
                    @"RichTextBlock(""This is a simple rich text block..."")",
                    OptionPanel(
                        TextBlock("Font Size"),
                        Slider(fontSize, 10, 28, setFontSize)
                    )),

                SampleCard("Structured Rich Text",
                    RichTextBlock(new[]
                    {
                        Paragraph(Run("Bold introduction. ") with { IsBold = true }, Run("Followed by normal text.")),
                        Paragraph(Run("Italic emphasis ") with { IsItalic = true }, Run("mixed with "), Run("bold") with { IsBold = true }, Run(".")),
                        Paragraph(Run("A third paragraph with different content to show block-level formatting."))
                    }),
                    """
                    RichTextBlock(new[]
                    {
                        Paragraph(Run("Bold") with { IsBold = true }, Run("normal")),
                        Paragraph(Run("Italic") with { IsItalic = true })
                    })
                    """),

                SampleCard("Simple RichTextBlock String",
                    VStack(8,
                        RichTextBlock("Line one of text content."),
                        RichTextBlock("Line two with separate blocks.")
                    ),
                    """
                    RichTextBlock("Line one")
                    RichTextBlock("Line two")
                    """)
            ).Margin(36, 24, 36, 36)
        );
    }
}
