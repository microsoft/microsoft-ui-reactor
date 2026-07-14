using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Text;

class MarkdownPage : Component
{
    public override Element Render()
    {
        var introMarkdown = @"# Markdown sample

Markdown renders **bold** text, *italic* text, and `inline code`.

- Create declarative UI
- Compose reusable components
- Keep samples readable";

        var linkMarkdown = @"Learn more about [Reactor](https://github.com/microsoft/microsoft-ui-reactor).

1. Write elements
2. Render native WinUI controls
3. Update with state";

        return ScrollView(VStack(16,
            PageHeader("Markdown", "Markdown converts formatted text into a Reactor element tree."),

            SampleCard("Formatted markdown",
                Border(Markdown(introMarkdown))
                    .Padding(16)
                    .Background(Theme.SubtleFill)
                    .CornerRadius(8),
                sourceCode: @"var markdown = @""# Markdown sample

Markdown renders **bold** text, *italic* text, and `inline code`.

- Create declarative UI
- Compose reusable components
- Keep samples readable"";

Border(Markdown(markdown))
    .Padding(16)
    .Background(Theme.SubtleFill)
    .CornerRadius(8)"),

            SampleCard("Links and lists",
                Border(Markdown(linkMarkdown))
                    .Padding(16)
                    .Background(Theme.SubtleFill)
                    .CornerRadius(8),
                sourceCode: @"var markdown = @""Learn more about [Reactor](https://github.com/microsoft/microsoft-ui-reactor).

1. Write elements
2. Render native WinUI controls
3. Update with state"";

Markdown(markdown)")
        ).Margin(36, 24, 36, 36));
    }
}
