using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Markdown;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Text;

class MarkdownPage : Component
{
    private readonly MarkdownTextSelectionOptions _selection = new();

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

            SampleCard("Message-wide selection (opt-in)",
                Border(MarkdownTextSelection.Scope(_selection, VStack(
                    Markdown("# Select across blocks\n\nDrag from this paragraph into the list and code below.",
                        new MarkdownOptions { TextSelection = _selection }),
                    Markdown("- First list item\n- Second list item\n\n```csharp\nvar selected = \"whole message\";\n```",
                        new MarkdownOptions { TextSelection = _selection }),
                    MarkdownTextSelection.Surface("Custom visible text", TextBlock("Custom visible text"))
                ))).Padding(16).Background(Theme.SubtleFill).CornerRadius(8),
                sourceCode: @"var selection = new MarkdownTextSelectionOptions();
var options = new MarkdownOptions { TextSelection = selection };
MarkdownTextSelection.Scope(selection, VStack(
    Markdown(""# Answer\n\nFirst paragraph."", options),
    Markdown(""- First item\n- Second item\n\n```cs\nvar x = 1;\n```"", options),
    MarkdownTextSelection.Surface(""Custom visible text"", TextBlock(""Custom visible text""))
))"),

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
