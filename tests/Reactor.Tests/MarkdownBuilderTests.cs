using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Markdown;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Unit tests for MarkdownBuilder.Build() — verifying that markdown strings
/// are converted to the correct Reactor Element tree structure.
/// Tests the public API via Factories.Markdown(...) which delegates to MarkdownBuilder.Build().
///
/// Note on WinUI dependencies:
/// The MarkdownBuilder's default rendering for certain block types (blockquote, code block,
/// thematic break, table header cells, HTML blocks) calls WinUI extension methods that create
/// SolidColorBrush COM objects (.Background(), .Foreground(), .WithBorder()). These require a
/// XAML Application context unavailable in unit tests.
///
/// For code blocks, thematic breaks, and HTML blocks, the MarkdownOptions callback is checked
/// BEFORE the default rendering, so providing a callback bypasses the WinUI dependency.
/// For blockquotes and table header cells, the WinUI calls happen unconditionally before any
/// callback, so these block types cannot be tested in pure unit tests.
///
/// Strategy:
/// - Inline formatting, paragraphs, headings, links, lists: tested directly (no WinUI deps).
/// - Code blocks, thematic breaks, HTML blocks: tested via MarkdownOptions callbacks.
/// - Blockquotes, tables: not testable without XAML context (callbacks run after WinUI calls).
/// - Images: callback invocation is tested; element placement depends on parent block type.
/// </summary>
public class MarkdownBuilderTests
{
    // ════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Build markdown to Element tree (avoids namespace/method name clash with Microsoft.UI.Reactor.Markdown).</summary>
    private static Element Md(string markdown) => Factories.Markdown(markdown);

    /// <summary>Build markdown to Element tree with custom options.</summary>
    private static Element Md(string markdown, MarkdownOptions options) => Factories.Markdown(markdown, options);

    /// <summary>Cast the top-level result to a VStack (StackElement with Vertical orientation).</summary>
    private static StackElement AsVStack(Element el)
    {
        var stack = Assert.IsType<StackElement>(el);
        Assert.Equal(Orientation.Vertical, stack.Orientation);
        return stack;
    }

    /// <summary>Get a specific child from a StackElement.</summary>
    private static T Child<T>(StackElement stack, int index) where T : Element
        => Assert.IsType<T>(stack.Children[index]);

    /// <summary>Get the first RichTextParagraph from a RichTextBlockElement.</summary>
    private static RichTextParagraph FirstParagraph(RichTextBlockElement rtb)
    {
        Assert.NotNull(rtb.Paragraphs);
        Assert.NotEmpty(rtb.Paragraphs);
        return rtb.Paragraphs![0];
    }

    /// <summary>Get a specific inline from the first paragraph of a RichTextBlockElement.</summary>
    private static T Inline<T>(RichTextBlockElement rtb, int index) where T : RichTextInline
    {
        var para = FirstParagraph(rtb);
        Assert.True(index < para.Inlines.Length,
            $"Expected inline at index {index}, but only {para.Inlines.Length} inlines");
        return Assert.IsType<T>(para.Inlines[index]);
    }

    /// <summary>Recursively flatten an element tree into a sequence.</summary>
    private static IEnumerable<Element> FlattenElements(Element el)
    {
        yield return el;
        if (el is StackElement stack)
            foreach (var child in stack.Children)
                foreach (var d in FlattenElements(child))
                    yield return d;
        else if (el is BorderElement border && border.Child is { } borderChild)
            foreach (var d in FlattenElements(borderChild))
                yield return d;
        else if (el is GridElement grid)
            foreach (var child in grid.Children)
                foreach (var d in FlattenElements(child))
                    yield return d;
    }

    // ════════════════════════════════════════════════════════════════════
    //  1. Basic structure
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyInput_ReturnsEmptyVStack()
    {
        var result = Md("");
        var stack = AsVStack(result);
        Assert.Empty(stack.Children);
    }

    [Fact]
    public void WhitespaceOnly_ReturnsEmptyVStack()
    {
        var result = Md("   \n\n   ");
        var stack = AsVStack(result);
        Assert.Empty(stack.Children);
    }

    [Fact]
    public void SingleParagraph_ReturnsVStackWithOneRichTextBlock()
    {
        var result = Md("Hello world");
        var stack = AsVStack(result);
        Assert.Single(stack.Children);

        var rtb = Child<RichTextBlockElement>(stack, 0);
        var run = Inline<RichTextRun>(rtb, 0);
        Assert.Equal("Hello world", run.Text);
    }

    [Fact]
    public void MultipleParagraphs_ReturnsVStackWithMultipleChildren()
    {
        var result = Md("First\n\nSecond\n\nThird");
        var stack = AsVStack(result);
        Assert.Equal(3, stack.Children.Length);

        Assert.Equal("First", Inline<RichTextRun>(Child<RichTextBlockElement>(stack, 0), 0).Text);
        Assert.Equal("Second", Inline<RichTextRun>(Child<RichTextBlockElement>(stack, 1), 0).Text);
        Assert.Equal("Third", Inline<RichTextRun>(Child<RichTextBlockElement>(stack, 2), 0).Text);
    }

    [Fact]
    public void DocumentVStack_HasSpacing8()
    {
        var result = Md("Hello");
        var stack = AsVStack(result);
        Assert.Equal(8, stack.Spacing);
    }

    [Fact]
    public void Paragraph_IsTextSelectionEnabled()
    {
        var result = Md("Hello");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        Assert.True(rtb.IsTextSelectionEnabled);
    }

    // ════════════════════════════════════════════════════════════════════
    //  2. Headings
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("# H1", 28)]
    [InlineData("## H2", 24)]
    [InlineData("### H3", 20)]
    [InlineData("#### H4", 18)]
    [InlineData("##### H5", 16)]
    [InlineData("###### H6", 14)]
    public void Heading_FontSizeByLevel(string markdown, double expectedFontSize)
    {
        var result = Md(markdown);
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        Assert.Equal(expectedFontSize, rtb.FontSize);
    }

    [Fact]
    public void Heading_InlinesAreBold()
    {
        var result = Md("# My Title");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var run = Inline<RichTextRun>(rtb, 0);
        Assert.Equal("My Title", run.Text);
        Assert.True(run.IsBold);
    }

    [Fact]
    public void Heading_SetextH1()
    {
        var result = Md("Title\n===");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        Assert.Equal(28.0, rtb.FontSize);
        Assert.True(Inline<RichTextRun>(rtb, 0).IsBold);
    }

    [Fact]
    public void Heading_SetextH2()
    {
        var result = Md("Subtitle\n---");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        Assert.Equal(24.0, rtb.FontSize);
    }

    [Fact]
    public void Heading_WithInlineFormatting_PreservesBoldAndItalic()
    {
        var result = Md("# Hello *world*");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var inlines = FirstParagraph(rtb).Inlines;
        Assert.True(inlines.Length >= 2);

        // All heading runs should be bold
        var runs = inlines.OfType<RichTextRun>().ToArray();
        Assert.All(runs, r => Assert.True(r.IsBold));

        // The italic run should also have IsItalic
        var italicRun = runs.FirstOrDefault(r => r.IsItalic);
        Assert.NotNull(italicRun);
        Assert.Contains("world", italicRun!.Text);
    }

    [Fact]
    public void Heading_IsTextSelectionEnabled()
    {
        var result = Md("# Title");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        Assert.True(rtb.IsTextSelectionEnabled);
    }

    // ════════════════════════════════════════════════════════════════════
    //  3. Inline formatting
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Bold_SetsBoldFlag()
    {
        var result = Md("**bold**");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var run = Inline<RichTextRun>(rtb, 0);
        Assert.Equal("bold", run.Text);
        Assert.True(run.IsBold);
        Assert.False(run.IsItalic);
    }

    [Fact]
    public void Bold_Underscores_SetsBoldFlag()
    {
        var result = Md("__bold__");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var run = Inline<RichTextRun>(rtb, 0);
        Assert.Equal("bold", run.Text);
        Assert.True(run.IsBold);
    }

    [Fact]
    public void Italic_SetsItalicFlag()
    {
        var result = Md("*italic*");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var run = Inline<RichTextRun>(rtb, 0);
        Assert.Equal("italic", run.Text);
        Assert.True(run.IsItalic);
        Assert.False(run.IsBold);
    }

    [Fact]
    public void Strikethrough_SetsStrikethroughFlag()
    {
        var result = Md("~~deleted~~");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var run = Inline<RichTextRun>(rtb, 0);
        Assert.Equal("deleted", run.Text);
        Assert.True(run.IsStrikethrough);
    }

    [Fact]
    public void InlineCode_SetsCodeFontFamily()
    {
        var result = Md("`code`");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var run = Inline<RichTextRun>(rtb, 0);
        Assert.Equal("code", run.Text);
        Assert.Equal("Consolas", run.FontFamily);
    }

    [Fact]
    public void BoldItalic_SetsBothFlags()
    {
        var result = Md("***bold italic***");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var boldItalicRun = FirstParagraph(rtb).Inlines
            .OfType<RichTextRun>()
            .FirstOrDefault(r => r.Text.Contains("bold italic"));
        Assert.NotNull(boldItalicRun);
        Assert.True(boldItalicRun!.IsBold);
        Assert.True(boldItalicRun.IsItalic);
    }

    [Fact]
    public void MixedInlineFormatting_ProducesCorrectRuns()
    {
        var result = Md("normal **bold** *italic* `code`");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var runs = FirstParagraph(rtb).Inlines.OfType<RichTextRun>().ToArray();

        Assert.True(runs.Length >= 4);
        Assert.Contains(runs, r => r.Text == "bold" && r.IsBold);
        Assert.Contains(runs, r => r.Text == "italic" && r.IsItalic);
        Assert.Contains(runs, r => r.Text == "code" && r.FontFamily == "Consolas");
    }

    [Fact]
    public void PlainText_HasNoFormatting()
    {
        var result = Md("plain text");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var run = Inline<RichTextRun>(rtb, 0);
        Assert.False(run.IsBold);
        Assert.False(run.IsItalic);
        Assert.False(run.IsStrikethrough);
        Assert.Null(run.FontFamily);
    }

    [Fact]
    public void InlineCode_CustomFontFamily()
    {
        var options = new MarkdownOptions { CodeFontFamily = "Cascadia Code" };
        var result = Md("`inline`", options);
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var run = Inline<RichTextRun>(rtb, 0);
        Assert.Equal("Cascadia Code", run.FontFamily);
    }

    // ════════════════════════════════════════════════════════════════════
    //  4. Links
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Link_Https_CreatesHyperlink()
    {
        var result = Md("[click here](https://example.com)");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var link = Inline<RichTextHyperlink>(rtb, 0);
        Assert.Equal("click here", link.Text);
        Assert.Equal(new Uri("https://example.com"), link.NavigateUri);
    }

    [Fact]
    public void Link_Http_IsSafe()
    {
        var result = Md("[link](http://example.com)");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var link = Inline<RichTextHyperlink>(rtb, 0);
        Assert.StartsWith("http://example.com", link.NavigateUri.ToString());
    }

    [Fact]
    public void Link_Mailto_IsSafe()
    {
        var result = Md("[email](mailto:test@example.com)");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var link = Inline<RichTextHyperlink>(rtb, 0);
        Assert.Contains("mailto:", link.NavigateUri.ToString());
    }

    [Fact]
    public void Link_Relative_IsBlocked()
    {
        // TASK-047: relative URIs no longer auto-pass IsSafeUri. The previous
        // "all relative URIs are safe" branch let `slack:`, `vscode:`, etc.
        // (parsed as relative by .NET Uri) bypass the allowlist. Apps that
        // want to allow site-relative links should resolve to an absolute
        // URI before handing the markdown to the builder.
        var result = Md("[page](./other-page)");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var inlines = FirstParagraph(rtb).Inlines;
        Assert.DoesNotContain(inlines, i => i is RichTextHyperlink);
    }

    [Fact]
    public void Link_JavascriptScheme_Blocked()
    {
        var result = Md("[xss](javascript:alert(1))");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var inlines = FirstParagraph(rtb).Inlines;
        Assert.DoesNotContain(inlines, i => i is RichTextHyperlink);
    }

    [Fact]
    public void Link_CustomScheme_Blocked()
    {
        // TASK-047: regression — schemes that .NET Uri parses as relative
        // (slack:, vscode:, intent:) used to slip past the allowlist.
        foreach (var url in new[] { "slack://team", "vscode://file/", "intent://example#" })
        {
            var result = Md($"[link]({url})");
            var stack = AsVStack(result);
            var rtb = Child<RichTextBlockElement>(stack, 0);
            var inlines = FirstParagraph(rtb).Inlines;
            Assert.DoesNotContain(inlines, i => i is RichTextHyperlink);
        }
    }

    [Fact]
    public void HugeInput_RejectedByMaxInputBytes()
    {
        // TASK-049: a 100MB blob should fail fast instead of allocating
        // tens of millions of mark structs.
        var huge = new string('*', 5 * 1024 * 1024); // 5 MiB > default 4 MiB cap
        Assert.Throws<ArgumentException>(() => Md(huge));
    }

    [Fact]
    public void Link_DataScheme_Blocked()
    {
        var result = Md("[data](data:text/html,<script>alert(1)</script>)");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var inlines = FirstParagraph(rtb).Inlines;
        Assert.DoesNotContain(inlines, i => i is RichTextHyperlink);
    }

    [Fact]
    public void Link_FlattensInlineTextToPlainString()
    {
        var result = Md("[**bold link**](https://example.com)");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var link = Inline<RichTextHyperlink>(rtb, 0);
        Assert.Equal("bold link", link.Text);
    }

    // ════════════════════════════════════════════════════════════════════
    //  5. Images (tested via callbacks — default rendering adds to
    //     parent frame which may be lost depending on block context)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Image_CustomCallback_InvokesWithAltAndUri()
    {
        string? capturedAlt = null;
        Uri? capturedUri = null;
        var options = new MarkdownOptions
        {
            Image = (alt, uri) =>
            {
                capturedAlt = alt;
                capturedUri = uri;
                return TextBlock($"IMG:{alt}");
            }
        };

        Md("![my alt](https://example.com/pic.jpg)", options);
        Assert.Equal("my alt", capturedAlt);
        Assert.Equal(new Uri("https://example.com/pic.jpg"), capturedUri);
    }

    [Fact]
    public void Image_InListItem_AppearsInTree()
    {
        // Images in list items appear because LeaveListItem uses frame.Children.
        var options = new MarkdownOptions
        {
            Image = (alt, uri) => TextBlock($"IMG:{alt}")
        };

        var result = Md("- ![photo](https://example.com/pic.jpg)", options);
        var allTexts = FlattenElements(result).OfType<TextBlockElement>().ToList();
        Assert.Contains(allTexts, t => t.Content == "IMG:photo");
    }

    [Fact]
    public void Image_UnsafeUri_JavascriptScheme_NoCallback()
    {
        bool callbackInvoked = false;
        var options = new MarkdownOptions
        {
            Image = (alt, uri) =>
            {
                callbackInvoked = true;
                return TextBlock("should not appear");
            }
        };

        Md("![alt](javascript:alert(1))", options);
        Assert.False(callbackInvoked, "Image callback should not be invoked for javascript: URIs");
    }

    [Fact]
    public void Image_SafeHttps_CallbackInvoked()
    {
        bool callbackInvoked = false;
        var options = new MarkdownOptions
        {
            Image = (alt, uri) =>
            {
                callbackInvoked = true;
                return Image(uri.ToString());
            }
        };

        Md("![alt](https://example.com/img.png)", options);
        Assert.True(callbackInvoked);
    }

    // ════════════════════════════════════════════════════════════════════
    //  6. Code blocks (callback bypasses WinUI brush calls)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void CodeBlock_Callback_ReceivesCodeAndLanguage()
    {
        string? capturedCode = null;
        string? capturedLang = null;
        var options = new MarkdownOptions
        {
            CodeBlock = (code, lang) =>
            {
                capturedCode = code;
                capturedLang = lang;
                return TextBlock(code);
            }
        };

        Md("```csharp\nvar x = 1;\n```", options);
        Assert.Equal("var x = 1;", capturedCode);
        Assert.Equal("csharp", capturedLang);
    }

    [Fact]
    public void CodeBlock_NoLanguage_LangIsEmptyOrNull()
    {
        string? capturedLang = "sentinel";
        var options = new MarkdownOptions
        {
            CodeBlock = (code, lang) =>
            {
                capturedLang = lang;
                return TextBlock(code);
            }
        };

        Md("```\ncode here\n```", options);
        // md4c may report empty string or null for unspecified language
        Assert.True(capturedLang is null or "",
            $"Expected null or empty for no language, got '{capturedLang}'");
    }

    [Fact]
    public void CodeBlock_TrimsTrailingNewline()
    {
        string? capturedCode = null;
        var options = new MarkdownOptions
        {
            CodeBlock = (code, lang) =>
            {
                capturedCode = code;
                return TextBlock(code);
            }
        };

        Md("```\nline1\nline2\n```", options);
        Assert.NotNull(capturedCode);
        Assert.False(capturedCode!.EndsWith('\n'), "Code should have trailing newline trimmed");
        Assert.Equal("line1\nline2", capturedCode);
    }

    [Fact]
    public void CodeBlock_Callback_ElementAppearsInTree()
    {
        var options = new MarkdownOptions
        {
            CodeBlock = (code, lang) => TextBlock($"CODE:{code}")
        };

        var result = Md("```\nvar x = 1;\n```", options);
        var stack = AsVStack(result);
        var textEl = Child<TextBlockElement>(stack, 0);
        Assert.Equal("CODE:var x = 1;", textEl.Content);
    }

    [Fact]
    public void CodeBlock_Callback_PreservesMultipleLines()
    {
        string? capturedCode = null;
        var options = new MarkdownOptions
        {
            CodeBlock = (code, lang) =>
            {
                capturedCode = code;
                return TextBlock(code);
            }
        };

        Md("```\nline1\nline2\nline3\n```", options);
        Assert.NotNull(capturedCode);
        Assert.Contains("line1", capturedCode!);
        Assert.Contains("line2", capturedCode);
        Assert.Contains("line3", capturedCode);
    }

    [Fact]
    public void IndentedCodeBlock_Callback_Works()
    {
        string? capturedCode = null;
        var options = new MarkdownOptions
        {
            CodeBlock = (code, lang) =>
            {
                capturedCode = code;
                return TextBlock(code);
            }
        };

        Md("    indented code", options);
        Assert.NotNull(capturedCode);
        Assert.Equal("indented code", capturedCode);
    }

    // ════════════════════════════════════════════════════════════════════
    //  7. Block quotes (WinUI brush dependency — tested indirectly)
    //     LeaveBlockQuote calls .Background() unconditionally before
    //     the callback, so we cannot test blockquotes in unit tests.
    //     We test that blockquote CONTENT (paragraphs, inline formatting)
    //     is parsed correctly by testing elements that don't require
    //     blockquote wrapping.
    // ════════════════════════════════════════════════════════════════════

    // Blockquote content parsing is verified indirectly through paragraph
    // and inline tests. The blockquote wrapper itself requires WinUI.

    // ════════════════════════════════════════════════════════════════════
    //  8. Lists
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnorderedList_CreatesVStackWithSpacing2()
    {
        var result = Md("- A\n- B\n- C");
        var stack = AsVStack(result);
        var listStack = Child<StackElement>(stack, 0);
        Assert.Equal(Orientation.Vertical, listStack.Orientation);
        Assert.Equal(2.0, listStack.Spacing);
        Assert.Equal(3, listStack.Children.Length);
    }

    [Fact]
    public void UnorderedList_ItemsAreHStacksWithBulletMarker()
    {
        var result = Md("- Item one");
        var stack = AsVStack(result);
        var listStack = Child<StackElement>(stack, 0);
        var item = Assert.IsType<StackElement>(listStack.Children[0]);
        Assert.Equal(Orientation.Horizontal, item.Orientation);
        Assert.Equal(4.0, item.Spacing);

        var marker = Assert.IsType<TextBlockElement>(item.Children[0]);
        Assert.Contains("\u2022", marker.Content);
    }

    [Fact]
    public void OrderedList_ItemsHaveNumberedMarkers()
    {
        var result = Md("1. First\n2. Second\n3. Third");
        var stack = AsVStack(result);
        var listStack = Child<StackElement>(stack, 0);
        Assert.Equal(3, listStack.Children.Length);

        var item1 = Assert.IsType<StackElement>(listStack.Children[0]);
        var marker1 = Assert.IsType<TextBlockElement>(item1.Children[0]);
        Assert.Contains("1.", marker1.Content);

        var item3 = Assert.IsType<StackElement>(listStack.Children[2]);
        var marker3 = Assert.IsType<TextBlockElement>(item3.Children[0]);
        Assert.Contains("3.", marker3.Content);
    }

    [Fact]
    public void OrderedList_StartOffset_RespectsStartNumber()
    {
        var result = Md("5. Five\n6. Six");
        var stack = AsVStack(result);
        var listStack = Child<StackElement>(stack, 0);

        var item1 = Assert.IsType<StackElement>(listStack.Children[0]);
        var marker1 = Assert.IsType<TextBlockElement>(item1.Children[0]);
        Assert.Contains("5.", marker1.Content);

        var item2 = Assert.IsType<StackElement>(listStack.Children[1]);
        var marker2 = Assert.IsType<TextBlockElement>(item2.Children[0]);
        Assert.Contains("6.", marker2.Content);
    }

    [Fact]
    public void TaskList_CheckedItem_ShowsCheckedBox()
    {
        var result = Md("- [x] Done");
        var stack = AsVStack(result);
        var listStack = Child<StackElement>(stack, 0);
        var item = Assert.IsType<StackElement>(listStack.Children[0]);
        var marker = Assert.IsType<TextBlockElement>(item.Children[0]);
        Assert.Contains("\u2611", marker.Content);
    }

    [Fact]
    public void TaskList_UncheckedItem_ShowsUncheckedBox()
    {
        var result = Md("- [ ] Todo");
        var stack = AsVStack(result);
        var listStack = Child<StackElement>(stack, 0);
        var item = Assert.IsType<StackElement>(listStack.Children[0]);
        var marker = Assert.IsType<TextBlockElement>(item.Children[0]);
        Assert.Contains("\u2610", marker.Content);
    }

    [Fact]
    public void NestedList_CreatesNestedStructure()
    {
        var result = Md("- A\n  - B\n  - C\n- D");
        var stack = AsVStack(result);
        var outerList = Child<StackElement>(stack, 0);
        Assert.True(outerList.Children.Length >= 2,
            "Outer list should have at least 2 items");
    }

    [Fact]
    public void UnorderedList_CustomCallback_ReceivesItems()
    {
        Element[]? capturedItems = null;
        var options = new MarkdownOptions
        {
            UnorderedList = (items) =>
            {
                capturedItems = items;
                return VStack(items);
            }
        };

        Md("- A\n- B\n- C", options);
        Assert.NotNull(capturedItems);
        Assert.Equal(3, capturedItems!.Length);
    }

    [Fact]
    public void OrderedList_CustomCallback_ReceivesItems()
    {
        Element[]? capturedItems = null;
        var options = new MarkdownOptions
        {
            OrderedList = (start, items) =>
            {
                capturedItems = items;
                return VStack(items);
            }
        };

        Md("1. A\n2. B", options);
        Assert.NotNull(capturedItems);
        Assert.Equal(2, capturedItems!.Length);
    }

    [Fact]
    public void OrderedList_CustomCallback_IsInvoked()
    {
        bool called = false;
        var options = new MarkdownOptions
        {
            OrderedList = (start, items) =>
            {
                called = true;
                return VStack(items);
            }
        };

        Md("1. A\n2. B", options);
        Assert.True(called);
    }

    [Fact]
    public void ListItem_CustomCallback_CalledForEachItem()
    {
        int callCount = 0;
        var options = new MarkdownOptions
        {
            ListItem = (defaultEl) =>
            {
                callCount++;
                return defaultEl;
            }
        };

        Md("- A\n- B\n- C", options);
        Assert.Equal(3, callCount);
    }

    // ════════════════════════════════════════════════════════════════════
    //  9. Tables (WinUI brush dependency on header cells — tested
    //     indirectly through the Table callback where possible)
    //     NOTE: Table header cells call .Background() unconditionally,
    //     causing COMException before the Table callback runs.
    //     Tables cannot be tested in pure unit tests.
    // ════════════════════════════════════════════════════════════════════

    // Table tests are omitted because header cell rendering in
    // LeaveTableCell calls .Background() before the Table callback
    // is invoked. This requires a WinUI XAML Application context.

    // ════════════════════════════════════════════════════════════════════
    //  10. Thematic breaks (callback bypasses WinUI brush calls)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ThematicBreak_CustomCallback_IsInvoked()
    {
        bool called = false;
        var options = new MarkdownOptions
        {
            ThematicBreak = () =>
            {
                called = true;
                return TextBlock("---break---");
            }
        };

        var result = Md("---", options);
        Assert.True(called);
        var stack = AsVStack(result);
        var textEl = Child<TextBlockElement>(stack, 0);
        Assert.Equal("---break---", textEl.Content);
    }

    [Fact]
    public void ThematicBreak_Asterisks_AlsoInvokesCallback()
    {
        bool called = false;
        var options = new MarkdownOptions
        {
            ThematicBreak = () =>
            {
                called = true;
                return TextBlock("hr");
            }
        };

        Md("***", options);
        Assert.True(called);
    }

    [Fact]
    public void ThematicBreak_Underscores_AlsoInvokesCallback()
    {
        bool called = false;
        var options = new MarkdownOptions
        {
            ThematicBreak = () =>
            {
                called = true;
                return TextBlock("hr");
            }
        };

        Md("___", options);
        Assert.True(called);
    }

    // ════════════════════════════════════════════════════════════════════
    //  11. HTML blocks (callback bypasses WinUI brush calls)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void HtmlBlock_Callback_ReceivesRawHtml()
    {
        string? capturedHtml = null;
        var options = new MarkdownOptions
        {
            HtmlBlock = (html) =>
            {
                capturedHtml = html;
                return TextBlock("html block");
            }
        };

        Md("<div>hello</div>", options);
        Assert.NotNull(capturedHtml);
        Assert.Contains("<div>hello</div>", capturedHtml);
    }

    [Fact]
    public void HtmlBlock_Callback_ElementAppearsInTree()
    {
        var options = new MarkdownOptions
        {
            HtmlBlock = (html) => TextBlock("CUSTOM_HTML")
        };

        var result = Md("<div>content</div>", options);
        var stack = AsVStack(result);
        var textEl = Child<TextBlockElement>(stack, 0);
        Assert.Equal("CUSTOM_HTML", textEl.Content);
    }

    // ════════════════════════════════════════════════════════════════════
    //  12. Text types: soft break, hard break, entity, inline HTML
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void SoftBreak_ProducesSpaceRun()
    {
        var result = Md("line1\nline2");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var inlines = FirstParagraph(rtb).Inlines;

        var spaceRun = inlines.OfType<RichTextRun>().FirstOrDefault(r => r.Text == " ");
        Assert.NotNull(spaceRun);
    }

    [Fact]
    public void HardBreak_ProducesLineBreak()
    {
        var result = Md("line1  \nline2");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var inlines = FirstParagraph(rtb).Inlines;

        Assert.Contains(inlines, i => i is RichTextLineBreak);
    }

    [Fact]
    public void Entity_Named_Amp_ResolvesToAmpersand()
    {
        var result = Md("&amp;");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var run = Inline<RichTextRun>(rtb, 0);
        Assert.Equal("&", run.Text);
    }

    [Fact]
    public void Entity_Named_Lt_ResolvesToLessThan()
    {
        var result = Md("&lt;");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var run = Inline<RichTextRun>(rtb, 0);
        Assert.Equal("<", run.Text);
    }

    [Fact]
    public void Entity_Named_Gt_ResolvesToGreaterThan()
    {
        var result = Md("&gt;test");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var allText = string.Join("", FirstParagraph(rtb).Inlines
            .OfType<RichTextRun>()
            .Select(r => r.Text));
        Assert.StartsWith(">", allText);
    }

    [Fact]
    public void InlineHtml_PassedThroughAsTextRun()
    {
        var result = Md("text <b>bold</b> text");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var allText = string.Join("", FirstParagraph(rtb).Inlines
            .OfType<RichTextRun>()
            .Select(r => r.Text));
        Assert.Contains("<b>", allText);
        Assert.Contains("</b>", allText);
    }

    // ════════════════════════════════════════════════════════════════════
    //  13. MarkdownOptions callbacks
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Options_Heading_CallbackOverridesDefault()
    {
        int capturedLevel = 0;
        var options = new MarkdownOptions
        {
            Heading = (level, defaultEl) =>
            {
                capturedLevel = level;
                return TextBlock($"H{level}");
            }
        };

        var result = Md("## Custom Heading", options);
        Assert.Equal(2, capturedLevel);
        var stack = AsVStack(result);
        var textEl = Child<TextBlockElement>(stack, 0);
        Assert.Equal("H2", textEl.Content);
    }

    [Fact]
    public void Options_Heading_ReceivesDefaultRichTextBlockElement()
    {
        Element? capturedDefault = null;
        var options = new MarkdownOptions
        {
            Heading = (level, defaultEl) =>
            {
                capturedDefault = defaultEl;
                return defaultEl;
            }
        };

        Md("### Title", options);
        Assert.NotNull(capturedDefault);
        var rtb = Assert.IsType<RichTextBlockElement>(capturedDefault);
        Assert.Equal(20.0, rtb.FontSize); // H3 = 20
    }

    [Fact]
    public void Options_Paragraph_CallbackOverridesDefault()
    {
        bool called = false;
        var options = new MarkdownOptions
        {
            Paragraph = (defaultEl) =>
            {
                called = true;
                return TextBlock("custom paragraph");
            }
        };

        var result = Md("Some text", options);
        Assert.True(called);
        var stack = AsVStack(result);
        var textEl = Child<TextBlockElement>(stack, 0);
        Assert.Equal("custom paragraph", textEl.Content);
    }

    [Fact]
    public void Options_Paragraph_ReceivesDefaultRichTextBlock()
    {
        Element? capturedDefault = null;
        var options = new MarkdownOptions
        {
            Paragraph = (defaultEl) =>
            {
                capturedDefault = defaultEl;
                return defaultEl;
            }
        };

        Md("Hello world", options);
        Assert.NotNull(capturedDefault);
        var rtb = Assert.IsType<RichTextBlockElement>(capturedDefault);
        var run = Inline<RichTextRun>(rtb, 0);
        Assert.Equal("Hello world", run.Text);
    }

    // ════════════════════════════════════════════════════════════════════
    //  14. Parser flags and defaults
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParserFlags_CommonMark_DisablesStrikethrough()
    {
        var options = new MarkdownOptions { ParserFlags = MarkdownParserFlags.None };
        var result = Md("~~text~~", options);
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var runs = FirstParagraph(rtb).Inlines.OfType<RichTextRun>().ToArray();
        Assert.True(runs.All(r => !r.IsStrikethrough),
            "Strikethrough should not be enabled in CommonMark mode");
    }

    [Fact]
    public void ParserFlags_Default_IsGitHub()
    {
        var options = new MarkdownOptions();
        Assert.Equal(MarkdownParserFlags.DialectGitHub, options.ParserFlags);
    }

    [Fact]
    public void DefaultCodeFontFamily_IsConsolas()
    {
        var options = new MarkdownOptions();
        Assert.Equal("Consolas", options.CodeFontFamily);
    }

    // ════════════════════════════════════════════════════════════════════
    //  15. Edge cases
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void LongDocument_AllParagraphsPresent()
    {
        var paragraphs = Enumerable.Range(0, 50).Select(i => $"Paragraph {i}").ToArray();
        var md = string.Join("\n\n", paragraphs);
        var result = Md(md);
        var stack = AsVStack(result);
        Assert.Equal(50, stack.Children.Length);
    }

    [Fact]
    public void EscapedCharacters_NotParsedAsFormatting()
    {
        var result = Md("\\*not italic\\*");
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var allText = string.Join("", FirstParagraph(rtb).Inlines
            .OfType<RichTextRun>()
            .Select(r => r.Text));
        Assert.Contains("*not italic*", allText);
    }

    [Fact]
    public void ComplexDocument_MixedBlocks_AllParsed()
    {
        var md = "# Title\n\nA paragraph.\n\n- Item 1\n- Item 2\n\n1. Ordered 1\n2. Ordered 2";
        var result = Md(md);
        var stack = AsVStack(result);
        // Title (H1) + paragraph + unordered list + ordered list = 4 children
        Assert.Equal(4, stack.Children.Length);

        // H1
        var h1 = Child<RichTextBlockElement>(stack, 0);
        Assert.Equal(28.0, h1.FontSize);

        // Paragraph
        var para = Child<RichTextBlockElement>(stack, 1);
        Assert.Equal("A paragraph.", Inline<RichTextRun>(para, 0).Text);

        // Unordered list
        var ul = Child<StackElement>(stack, 2);
        Assert.Equal(2.0, ul.Spacing);
        Assert.Equal(2, ul.Children.Length);

        // Ordered list
        var ol = Child<StackElement>(stack, 3);
        Assert.Equal(2.0, ol.Spacing);
        Assert.Equal(2, ol.Children.Length);
    }

    [Fact]
    public void StressManyInlineElements_AllParsed()
    {
        var parts = Enumerable.Range(0, 30)
            .Select(i => i % 3 == 0 ? $"**b{i}**" : i % 3 == 1 ? $"*i{i}*" : $"`c{i}`");
        var md = string.Join(" ", parts);
        var result = Md(md);
        var stack = AsVStack(result);
        var rtb = Child<RichTextBlockElement>(stack, 0);
        var runs = FirstParagraph(rtb).Inlines.OfType<RichTextRun>().ToArray();

        Assert.Contains(runs, r => r.Text == "b0" && r.IsBold);
        Assert.Contains(runs, r => r.Text == "i1" && r.IsItalic);
        Assert.Contains(runs, r => r.Text == "c2" && r.FontFamily == "Consolas");
    }

    [Fact]
    public void HeadingFollowedByParagraph_BothPresent()
    {
        var result = Md("# Title\n\nBody text");
        var stack = AsVStack(result);
        Assert.Equal(2, stack.Children.Length);

        var heading = Child<RichTextBlockElement>(stack, 0);
        Assert.Equal(28.0, heading.FontSize);

        var body = Child<RichTextBlockElement>(stack, 1);
        Assert.Equal("Body text", Inline<RichTextRun>(body, 0).Text);
    }

    [Fact]
    public void MultipleHeadingLevels_InSequence()
    {
        var result = Md("# H1\n\n## H2\n\n### H3");
        var stack = AsVStack(result);
        Assert.Equal(3, stack.Children.Length);

        Assert.Equal(28.0, Child<RichTextBlockElement>(stack, 0).FontSize);
        Assert.Equal(24.0, Child<RichTextBlockElement>(stack, 1).FontSize);
        Assert.Equal(20.0, Child<RichTextBlockElement>(stack, 2).FontSize);
    }

    [Fact]
    public void ListWithFormattedItems_InlinesPreserved()
    {
        var result = Md("- **bold item**\n- *italic item*");
        var stack = AsVStack(result);
        var listStack = Child<StackElement>(stack, 0);

        // First item: HStack with marker + content
        var item1 = Assert.IsType<StackElement>(listStack.Children[0]);
        var content1 = item1.Children[1]; // content is second child after marker
        var rtb1 = Assert.IsType<RichTextBlockElement>(content1);
        var run1 = Inline<RichTextRun>(rtb1, 0);
        Assert.True(run1.IsBold);

        // Second item
        var item2 = Assert.IsType<StackElement>(listStack.Children[1]);
        var content2 = item2.Children[1];
        var rtb2 = Assert.IsType<RichTextBlockElement>(content2);
        var run2 = Inline<RichTextRun>(rtb2, 0);
        Assert.True(run2.IsItalic);
    }

    [Fact]
    public void AllHeadingCallbackLevels_AreCorrect()
    {
        var levels = new List<int>();
        var options = new MarkdownOptions
        {
            Heading = (level, el) =>
            {
                levels.Add(level);
                return el;
            }
        };

        Md("# H1\n\n## H2\n\n### H3\n\n#### H4\n\n##### H5\n\n###### H6", options);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, levels);
    }
}
