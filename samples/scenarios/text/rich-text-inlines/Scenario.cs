// id: rich-text-inlines
// intent: rich text with bold, italic, and hyperlink runs
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System;
using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Rich Text Inlines", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        return RichTextBlock(new[]
        {
            Paragraph(
                Run("Rich text can mix "),
                Run("bold") with { IsBold = true },
                Run(", "),
                Run("italic") with { IsItalic = true },
                Run(", and "),
                Hyperlink("hyperlinks", new Uri("https://github.com/microsoft/microsoft-ui-reactor")),
                Run(" in one paragraph."))
        });
    }
}
