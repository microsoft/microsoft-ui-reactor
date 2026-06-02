using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Markdown;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for <see cref="MarkdownOptions.UnifiedRichText"/> — the opt-in mode
/// that collapses an entire markdown document into a single
/// <see cref="RichTextBlockElement"/> (one paragraph per block, non-flow
/// blocks wrapped via <see cref="RichTextInlineUIContainer"/>). Enables
/// drag-text-selection across paragraphs.
/// </summary>
public class MarkdownUnifiedRichTextTests
{
    private static readonly MarkdownOptions Unified = new() { UnifiedRichText = true };

    private static RichTextBlockElement BuildRich(string md, MarkdownOptions? extra = null)
    {
        var opts = extra is null
            ? Unified
            : extra with { UnifiedRichText = true };
        return Assert.IsType<RichTextBlockElement>(Factories.Markdown(md, opts));
    }

    [Fact]
    public void Empty_ReturnsEmptyRichTextBlock_WithSelectionEnabled()
    {
        var rtb = BuildRich("");
        Assert.True(rtb.IsTextSelectionEnabled);
        Assert.True(rtb.Paragraphs is null || rtb.Paragraphs.Length == 0);
    }

    [Fact]
    public void MultipleParagraphs_CoalesceIntoOneRichTextBlock()
    {
        var rtb = BuildRich("First paragraph.\n\nSecond paragraph.\n\nThird paragraph.");

        Assert.True(rtb.IsTextSelectionEnabled);
        Assert.NotNull(rtb.Paragraphs);
        Assert.Equal(3, rtb.Paragraphs!.Length);

        Assert.Equal("First paragraph.",
            Assert.IsType<RichTextRun>(rtb.Paragraphs[0].Inlines[0]).Text);
        Assert.Equal("Second paragraph.",
            Assert.IsType<RichTextRun>(rtb.Paragraphs[1].Inlines[0]).Text);
        Assert.Equal("Third paragraph.",
            Assert.IsType<RichTextRun>(rtb.Paragraphs[2].Inlines[0]).Text);
    }

    [Fact]
    public void Heading_FontSizePropagatesToRuns_NotToRootRichTextBlock()
    {
        var rtb = BuildRich("# Big heading\n\nBody text.");

        Assert.Null(rtb.FontSize);
        Assert.Equal(2, rtb.Paragraphs!.Length);

        // Heading run keeps bold + heading-size FontSize.
        var headingRun = Assert.IsType<RichTextRun>(rtb.Paragraphs[0].Inlines[0]);
        Assert.Equal("Big heading", headingRun.Text);
        Assert.True(headingRun.IsBold);
        Assert.Equal(28.0, headingRun.FontSize);

        // Body paragraph stays default size.
        var bodyRun = Assert.IsType<RichTextRun>(rtb.Paragraphs[1].Inlines[0]);
        Assert.Equal("Body text.", bodyRun.Text);
        Assert.False(bodyRun.IsBold);
        Assert.Null(bodyRun.FontSize);
    }

    [Fact]
    public void NonFlowChild_IsWrappedInInlineUIContainer_AsItsOwnParagraph()
    {
        // Custom CodeBlock callback (so we skip the default Border which needs WinUI brushes).
        var rtb = BuildRich(
            "Para one.\n\n```\nlet x = 1;\n```\n\nPara two.",
            new MarkdownOptions { CodeBlock = (code, _) => TextBlock(code) });

        Assert.Equal(3, rtb.Paragraphs!.Length);

        // Para one — flow paragraph.
        Assert.Equal("Para one.",
            Assert.IsType<RichTextRun>(rtb.Paragraphs[0].Inlines[0]).Text);

        // Code block — InlineUI wrapper paragraph.
        var inlineUI = Assert.IsType<RichTextInlineUIContainer>(rtb.Paragraphs[1].Inlines[0]);
        Assert.NotNull(inlineUI.Child);
        Assert.IsType<TextBlockElement>(inlineUI.Child);

        // Para two — flow paragraph after the embedded block.
        Assert.Equal("Para two.",
            Assert.IsType<RichTextRun>(rtb.Paragraphs[2].Inlines[0]).Text);
    }

    [Fact]
    public void UnorderedList_IsWrappedInInlineUIContainer()
    {
        var rtb = BuildRich("Before.\n\n- one\n- two\n\nAfter.");

        Assert.Equal(3, rtb.Paragraphs!.Length);
        Assert.IsType<RichTextRun>(rtb.Paragraphs[0].Inlines[0]);

        var listInline = Assert.IsType<RichTextInlineUIContainer>(rtb.Paragraphs[1].Inlines[0]);
        Assert.NotNull(listInline.Child);
        // Default list rendering is a VStack of HStack(marker, content) items.
        var listStack = Assert.IsType<StackElement>(listInline.Child);
        Assert.Equal(2, listStack.Children.Length);

        Assert.IsType<RichTextRun>(rtb.Paragraphs[2].Inlines[0]);
    }

    [Fact]
    public void InlineFormatting_PreservedAcrossCoalescedParagraphs()
    {
        var rtb = BuildRich("**Bold** then *italic*.\n\nNext paragraph.");

        Assert.Equal(2, rtb.Paragraphs!.Length);
        var first = rtb.Paragraphs[0];
        // md4c may split runs around inline span boundaries; what matters is
        // that bold and italic formatting survive into separate runs.
        Assert.Contains(first.Inlines, i => i is RichTextRun r && r.IsBold && r.Text == "Bold");
        Assert.Contains(first.Inlines, i => i is RichTextRun r && r.IsItalic && r.Text == "italic");
        Assert.Equal("Next paragraph.",
            Assert.IsType<RichTextRun>(rtb.Paragraphs[1].Inlines[0]).Text);
    }

    [Fact]
    public void UnifiedFalse_DefaultBehavior_StillReturnsVStack()
    {
        var result = Factories.Markdown("Para A.\n\nPara B.");
        var stack = Assert.IsType<StackElement>(result);
        Assert.Equal(2, stack.Children.Length);
        Assert.IsType<RichTextBlockElement>(stack.Children[0]);
        Assert.IsType<RichTextBlockElement>(stack.Children[1]);
    }

    [Fact]
    public void UnifiedTrue_IsTextSelectionEnabled_OnTheCoalescedRichTextBlock()
    {
        var rtb = BuildRich("Selectable text across\n\nmultiple paragraphs.");
        Assert.True(rtb.IsTextSelectionEnabled);
    }

    [Fact]
    public void ParagraphCallback_OutputIsWrappedInInlineUI_WhenItReturnsNonRichTextBlock()
    {
        // When a user Paragraph callback returns a non-RichTextBlock element,
        // unified mode still embeds it (as InlineUI) so the result remains a
        // single RichTextBlock.
        var rtb = BuildRich(
            "Para A.",
            new MarkdownOptions { Paragraph = _ => TextBlock("custom") });

        Assert.Single(rtb.Paragraphs!);
        var ui = Assert.IsType<RichTextInlineUIContainer>(rtb.Paragraphs![0].Inlines[0]);
        var tb = Assert.IsType<TextBlockElement>(ui.Child);
        Assert.Equal("custom", tb.Content);
    }

    [Fact]
    public void ParagraphCallback_ReturningRichTextBlock_IsAbsorbed()
    {
        // If a Paragraph callback returns a RichTextBlockElement, unified
        // mode lifts its paragraphs directly — selection stays unified.
        var rtb = BuildRich(
            "A.\n\nB.",
            new MarkdownOptions
            {
                Paragraph = el => el, // pass-through (default RTB)
            });

        Assert.Equal(2, rtb.Paragraphs!.Length);
        Assert.IsType<RichTextRun>(rtb.Paragraphs[0].Inlines[0]);
        Assert.IsType<RichTextRun>(rtb.Paragraphs[1].Inlines[0]);
    }
}
