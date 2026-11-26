using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Markdown;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class MarkdownNestedListTests
{
    [Theory]
    [InlineData(false, "# child")]
    [InlineData(true, "# child")]
    [InlineData(false, "> child")]
    [InlineData(true, "> child")]
    [InlineData(false, "```\nchild\n```")]
    [InlineData(true, "```\nchild\n```")]
    public void TightItemTextPrecedesOtherChildBlocks(bool structured, string childBlock)
    {
        var source = "- intro\n  " + childBlock.Replace("\n", "\n  ");
        var tree = global::Microsoft.UI.Reactor.Advanced.Factories.Markdown(source,
            new MarkdownOptions
            {
                UnifiedRichText = true,
                TextSelection = structured ? new() : null,
                BlockQuote = element => Assert.IsType<BorderElement>(element).Child!,
                CodeBlock = (code, _) => structured
                    ? MarkdownTextSelection.Surface(code, new TextBlockElement(code))
                    : new TextBlockElement(code),
            });
        var list = Assert.IsType<StackElement>(structured
            ? Assert.Single(Assert.IsType<StackElement>(tree).Children)
            : Assert.IsType<RichTextInlineUIContainer>(Assert.Single(
                Assert.Single(Assert.IsType<RichTextBlockElement>(tree).Paragraphs!).Inlines)).Child);
        var row = Assert.IsType<GridElement>(Assert.Single(list.Children));
        var content = Assert.IsType<StackElement>(row.Children[1]);
        Assert.Equal(2, content.Children.Length);
        var intro = Assert.IsType<RichTextBlockElement>(Unwrap(content.Children[0]));
        Assert.Equal("intro", Assert.IsType<RichTextRun>(
            Assert.Single(Assert.Single(intro.Paragraphs!).Inlines)).Text);
        var child = Unwrap(content.Children[1]);
        if (child is StackElement quote)
            child = Unwrap(Assert.Single(quote.Children));
        Assert.Equal("child", child switch
        {
            RichTextBlockElement rich => Assert.IsType<RichTextRun>(
                Assert.Single(Assert.Single(rich.Paragraphs!).Inlines)).Text,
            TextBlockElement code => code.Content,
            _ => throw new InvalidOperationException($"Unexpected child: {child.GetType().Name}"),
        });
        if (structured)
        {
            var document = new MarkdownSelectionDocument();
            document.Rebuild(MarkdownTextSelectionTests.Surfaces(tree)
                .Select(s => new SelectionPart(s, s.Text, s.SeparatorAfter)));
            Assert.Equal("\u2022 intro\nchild", document.Text);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MixedListsKeepFormattedTextAndLinksBeforeNestedItems(bool structured, bool loose)
    {
        var source = """
            7. **parent** [docs](https://example.com/guide)
               - *child*
                 1. `leaf`
               - child sibling
            8. parent sibling
            """;
        if (loose)
            source = source.Replace("\n", "\n\n");
        var tree = global::Microsoft.UI.Reactor.Advanced.Factories.Markdown(source,
            new MarkdownOptions { UnifiedRichText = true, TextSelection = structured ? new() : null });

        var list = Assert.IsType<StackElement>(structured
            ? Assert.Single(Assert.IsType<StackElement>(tree).Children)
            : Assert.IsType<RichTextInlineUIContainer>(Assert.Single(
                Assert.Single(Assert.IsType<RichTextBlockElement>(tree).Paragraphs!).Inlines)).Child);
        var markers = new List<string>();
        var bodies = new List<RichTextInline[]>();

        void ReadList(StackElement stack)
        {
            foreach (var child in stack.Children)
            {
                var row = Assert.IsType<GridElement>(child);
                markers.Add(Assert.IsType<TextBlockElement>(Unwrap(row.Children[0])).Content);
                var body = row.Children[1];
                if (body is StackElement item)
                {
                    Assert.Equal(2, item.Children.Length);
                    bodies.Add(ReadInlines(item.Children[0]));
                    ReadList(Assert.IsType<StackElement>(item.Children[1]));
                }
                else
                {
                    bodies.Add(ReadInlines(body));
                }
            }
        }

        ReadList(list);
        Assert.Equal(["7. ", "\u2022 ", "1. ", "\u2022 ", "8. "], markers);
        Assert.Collection(bodies,
            inlines =>
            {
                Assert.Equal("parent docs", InlineText(inlines));
                Assert.Contains(inlines, i => i is RichTextRun { Text: "parent", IsBold: true });
                Assert.Equal("https://example.com/guide",
                    Assert.Single(inlines.OfType<RichTextHyperlink>()).NavigateUri.AbsoluteUri);
            },
            inlines =>
            {
                var child = Assert.IsType<RichTextRun>(Assert.Single(inlines));
                Assert.Equal("child", child.Text);
                Assert.True(child.IsItalic);
            },
            inlines => Assert.Equal("leaf", InlineText(inlines)),
            inlines => Assert.Equal("child sibling", InlineText(inlines)),
            inlines => Assert.Equal("parent sibling", InlineText(inlines)));

        if (structured)
        {
            var document = new MarkdownSelectionDocument();
            document.Rebuild(MarkdownTextSelectionTests.Surfaces(tree)
                .Select(s => new SelectionPart(s, s.Text, s.SeparatorAfter)));
            Assert.Equal("7. parent docs\n\u2022 child\n1. leaf\n\u2022 child sibling\n8. parent sibling", document.Text);
        }

        static RichTextInline[] ReadInlines(Element element) =>
            Assert.Single(Assert.IsType<RichTextBlockElement>(Unwrap(element)).Paragraphs!).Inlines;

        static string InlineText(RichTextInline[] inlines) =>
            string.Concat(inlines.Select(i => i switch
            {
                RichTextRun run => run.Text,
                RichTextHyperlink link => link.Text,
                _ => throw new InvalidOperationException($"Unexpected inline: {i.GetType().Name}"),
            }));
    }

    private static Element Unwrap(Element element) =>
        element is MarkdownSelectionElement surface ? surface.Content : element;
}
