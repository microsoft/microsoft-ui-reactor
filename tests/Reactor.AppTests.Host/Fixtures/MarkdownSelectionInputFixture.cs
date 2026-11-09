using Microsoft.UI.Reactor.Markdown;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

internal sealed class MarkdownSelectionInputFixture : Component
{
    private readonly MarkdownTextSelectionOptions _outerOptions = new();
    private readonly MarkdownTextSelectionOptions _innerOptions = new();
    private readonly MarkdownTextSelectionOptions _longOptions = new();
    private int _clicks;

    public override Element Render()
    {
        var (streamed, setStreamed) = UseState(false);
        Element Md(string text, MarkdownTextSelectionOptions options) =>
            Microsoft.UI.Reactor.Advanced.Factories.Markdown(text, new MarkdownOptions { TextSelection = options });
        var link = RichTextBlock("").Set(control =>
        {
            control.Blocks.Clear();
            var hyperlink = new Hyperlink();
            hyperlink.Inlines.Add(new Run { Text = "native link" });
            hyperlink.Click += (_, _) => _clicks++;
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(hyperlink);
            control.Blocks.Add(paragraph);
        });
        return VStack(
            TextBlock("Clicks 0").AutomationId("SelectionClicks").Set(text =>
            {
                var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                timer.Tick += (_, _) => text.Text = $"Clicks {_clicks}";
                text.Loaded += (_, _) => timer.Start();
                text.Unloaded += (_, _) => timer.Stop();
            }),
            Button("Stream", () => setStreamed(true)).AutomationId("SelectionStream"),
            MarkdownTextSelection.Scope(_outerOptions, VStack(
                Md("outer text", _outerOptions),
                MarkdownTextSelection.Scope(_innerOptions, Md(streamed ? "inner text streamed" : "inner text", _innerOptions))
                    .AutomationId("SelectionInner"),
                MarkdownTextSelection.Surface("native link", link)))
                .AutomationId("SelectionOuter"),
            ItemsView(new[] { "message" }, row => row, (_, _) =>
                ItemContainer(MarkdownTextSelection.Scope(_longOptions,
                    Md(string.Join("\n\n", Enumerable.Range(0, 70).Select(i => $"drag paragraph {i:D2}")), _longOptions))
                    .AutomationId("SelectionLong")))
                .Height(180).AutomationId("SelectionTimeline")).Width(450);
    }
}
