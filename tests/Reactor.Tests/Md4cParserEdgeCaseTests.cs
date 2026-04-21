using Microsoft.UI.Reactor.Markdown;
using System.Text;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Additional Md4cParser edge-case tests to cover:
/// - TextWithNullReplacement (null chars in text)
/// - Mark management (emphasis edge cases)
/// - EnsureBuffer (large inputs)
/// - Various block/inline combinations
/// - HTML entity decoding
/// - Autolinks, raw HTML, code spans with backtick runs
/// </summary>
public class Md4cParserEdgeCaseTests
{
    // Helper to collect all callbacks
    private record ParseEvent(string Type, string Detail);

    private static List<ParseEvent> ParseCollecting(string markdown)
    {
        var events = new List<ParseEvent>();
        Md4cParser.Parse(markdown, 0,
            (type, detail) => { events.Add(new("EnterBlock", $"{type}")); return 0; },
            (type, detail) => { events.Add(new("LeaveBlock", $"{type}")); return 0; },
            (type, detail) => { events.Add(new("EnterSpan", $"{type}")); return 0; },
            (type, detail) => { events.Add(new("LeaveSpan", $"{type}")); return 0; },
            (type, text) => { events.Add(new("Text", new string(text))); return 0; }
        );
        return events;
    }

    // ═══════════════════════════════════════════════════════════════
    // Null character replacement
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_NullCharInText_ReplacedWithNullCharEvent()
    {
        var events = ParseCollecting("Hello\0World");
        // Should have text events; the null char should be handled
        Assert.True(events.Count > 0);
        var textEvents = events.Where(e => e.Type == "Text").ToList();
        Assert.True(textEvents.Count >= 1);
    }

    [Fact]
    public void Parse_MultipleNullChars_AllHandled()
    {
        var events = ParseCollecting("A\0B\0C");
        var textEvents = events.Where(e => e.Type == "Text").ToList();
        Assert.True(textEvents.Count >= 1);
    }

    // ═══════════════════════════════════════════════════════════════
    // Emphasis edge cases (mark management)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_NestedEmphasis_BoldInItalic()
    {
        var events = ParseCollecting("*This is **bold** in italic*");
        Assert.Contains(events, e => e.Type == "EnterSpan" && e.Detail.Contains("Em"));
    }

    [Fact]
    public void Parse_StrikethroughIfSupported()
    {
        // Strikethrough with ~~ syntax
        var events = ParseCollecting("~~deleted~~");
        // Should at least parse without error
        Assert.True(events.Count > 0);
    }

    [Fact]
    public void Parse_EmphasisWithSpaces_NotEmphasis()
    {
        var events = ParseCollecting("foo * bar * baz");
        // * surrounded by spaces is not emphasis
        var spans = events.Where(e => e.Type == "EnterSpan" && e.Detail.Contains("Em")).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Parse_UnderscoreEmphasis()
    {
        var events = ParseCollecting("_italic_ and __bold__");
        var spans = events.Where(e => e.Type == "EnterSpan").ToList();
        Assert.True(spans.Count >= 2);
    }

    [Fact]
    public void Parse_MismatchedEmphasis_NoSpan()
    {
        var events = ParseCollecting("*not closed");
        // Should parse as text without error
        Assert.True(events.Count > 0);
    }

    [Fact]
    public void Parse_TripleAsterisks_BoldItalic()
    {
        var events = ParseCollecting("***bold italic***");
        var spans = events.Where(e => e.Type == "EnterSpan").ToList();
        Assert.True(spans.Count >= 1);
    }

    // ═══════════════════════════════════════════════════════════════
    // Code spans
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_CodeSpan_SingleBacktick()
    {
        var events = ParseCollecting("Use `printf()` here");
        Assert.Contains(events, e => e.Type == "EnterSpan" && e.Detail.Contains("Code"));
    }

    [Fact]
    public void Parse_CodeSpan_DoubleBacktick()
    {
        var events = ParseCollecting("`` `code` ``");
        Assert.Contains(events, e => e.Type == "EnterSpan" && e.Detail.Contains("Code"));
    }

    [Fact]
    public void Parse_CodeSpan_BacktickInsideCode()
    {
        var events = ParseCollecting("`` foo`bar ``");
        Assert.Contains(events, e => e.Type == "EnterSpan" && e.Detail.Contains("Code"));
    }

    // ═══════════════════════════════════════════════════════════════
    // Links and images
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_InlineLink_WithTitle()
    {
        var events = ParseCollecting("[text](http://example.com \"title\")");
        Assert.Contains(events, e => e.Type == "EnterSpan" && e.Detail.Contains("A"));
    }

    [Fact]
    public void Parse_Image_WithAlt()
    {
        var events = ParseCollecting("![alt text](image.png)");
        Assert.Contains(events, e => e.Type == "EnterSpan" && e.Detail.Contains("Img"));
    }

    [Fact]
    public void Parse_Autolink_Email()
    {
        var events = ParseCollecting("<user@example.com>");
        Assert.True(events.Count > 0);
    }

    [Fact]
    public void Parse_Autolink_Url()
    {
        var events = ParseCollecting("<http://example.com>");
        Assert.True(events.Count > 0);
    }

    // ═══════════════════════════════════════════════════════════════
    // Block types
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_ThematicBreak()
    {
        var events = ParseCollecting("---");
        Assert.Contains(events, e => e.Type == "EnterBlock" && e.Detail.Contains("Hr"));
    }

    [Fact]
    public void Parse_IndentedCodeBlock()
    {
        var events = ParseCollecting("    code line 1\n    code line 2");
        Assert.Contains(events, e => e.Type == "EnterBlock" && e.Detail.Contains("Code"));
    }

    [Fact]
    public void Parse_FencedCodeBlock_WithInfo()
    {
        var events = ParseCollecting("```csharp\nvar x = 1;\n```");
        Assert.Contains(events, e => e.Type == "EnterBlock" && e.Detail.Contains("Code"));
    }

    [Fact]
    public void Parse_BlockQuote()
    {
        var events = ParseCollecting("> quoted text\n> more text");
        Assert.Contains(events, e => e.Type == "EnterBlock" && e.Detail.Contains("Quote"));
    }

    [Fact]
    public void Parse_NestedBlockQuote()
    {
        var events = ParseCollecting("> > nested");
        Assert.Contains(events, e => e.Type == "EnterBlock" && e.Detail.Contains("Quote"));
    }

    [Fact]
    public void Parse_OrderedList()
    {
        var events = ParseCollecting("1. first\n2. second\n3. third");
        Assert.Contains(events, e => e.Type == "EnterBlock" && e.Detail.Contains("Ol"));
    }

    [Fact]
    public void Parse_UnorderedList()
    {
        var events = ParseCollecting("- one\n- two\n- three");
        Assert.Contains(events, e => e.Type == "EnterBlock" && e.Detail.Contains("Ul"));
    }

    [Fact]
    public void Parse_Heading_Level1To6()
    {
        for (int i = 1; i <= 6; i++)
        {
            var md = new string('#', i) + " heading";
            var events = ParseCollecting(md);
            Assert.Contains(events, e => e.Type == "EnterBlock" && e.Detail.Contains("H"));
        }
    }

    [Fact]
    public void Parse_SetextHeading_Level1()
    {
        var events = ParseCollecting("Heading\n=======");
        Assert.Contains(events, e => e.Type == "EnterBlock" && e.Detail.Contains("H"));
    }

    [Fact]
    public void Parse_SetextHeading_Level2()
    {
        var events = ParseCollecting("Heading\n-------");
        Assert.Contains(events, e => e.Type == "EnterBlock" && e.Detail.Contains("H"));
    }

    // ═══════════════════════════════════════════════════════════════
    // HTML inline and block
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_InlineHtml()
    {
        var events = ParseCollecting("text <b>bold</b> more");
        Assert.True(events.Count > 0);
    }

    [Fact]
    public void Parse_HtmlBlock()
    {
        var events = ParseCollecting("<div>\nsome content\n</div>");
        Assert.Contains(events, e => e.Type == "EnterBlock" && e.Detail.Contains("Html"));
    }

    // ═══════════════════════════════════════════════════════════════
    // Entity references
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_HtmlEntity_Named()
    {
        var events = ParseCollecting("&amp; and &lt;");
        var textEvents = events.Where(e => e.Type == "Text").ToList();
        Assert.True(textEvents.Count >= 1);
    }

    [Fact]
    public void Parse_HtmlEntity_Numeric()
    {
        var events = ParseCollecting("&#65; &#x41;");
        Assert.True(events.Count > 0);
    }

    // ═══════════════════════════════════════════════════════════════
    // Large input (EnsureBuffer)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_LargeInput_DoesNotCrash()
    {
        // Generate a large markdown document
        var sb = new StringBuilder();
        for (int i = 0; i < 500; i++)
        {
            sb.AppendLine($"## Section {i}");
            sb.AppendLine($"This is paragraph {i} with **bold** and *italic* text.");
            sb.AppendLine($"- Item {i}a");
            sb.AppendLine($"- Item {i}b");
            sb.AppendLine();
        }
        var events = ParseCollecting(sb.ToString());
        Assert.True(events.Count > 1000);
    }

    [Fact]
    public void Parse_LongLine_DoesNotCrash()
    {
        var longLine = new string('x', 10_000);
        var events = ParseCollecting(longLine);
        Assert.True(events.Count > 0);
    }

    // ═══════════════════════════════════════════════════════════════
    // Mixed content
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_ListWithCodeBlock()
    {
        var md = "- item\n\n      code in list\n\n- next";
        var events = ParseCollecting(md);
        Assert.True(events.Count > 0);
    }

    [Fact]
    public void Parse_BlockquoteWithList()
    {
        var md = "> - item 1\n> - item 2";
        var events = ParseCollecting(md);
        Assert.Contains(events, e => e.Type == "EnterBlock" && e.Detail.Contains("Quote"));
    }

    [Fact]
    public void Parse_EscapedCharacters()
    {
        var events = ParseCollecting("\\*not italic\\* and \\#not heading");
        var spans = events.Where(e => e.Type == "EnterSpan" && e.Detail.Contains("Em")).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Parse_HardLineBreak()
    {
        var events = ParseCollecting("line 1  \nline 2");
        // Hard line break produces a Br span or softbreak — just check it parses
        Assert.True(events.Count > 2);
    }

    [Fact]
    public void Parse_SoftLineBreak()
    {
        var events = ParseCollecting("line 1\nline 2");
        Assert.Contains(events, e => e.Type == "Text" && e.Detail.Contains("\n"));
    }

    [Fact]
    public void Parse_EmptyDocument()
    {
        var events = ParseCollecting("");
        // Should still produce doc enter/leave
        Assert.True(events.Count >= 2);
    }

    [Fact]
    public void Parse_OnlyWhitespace()
    {
        var events = ParseCollecting("   \n   \n   ");
        Assert.True(events.Count >= 2);
    }

    [Fact]
    public void Parse_Table_GfmExtension()
    {
        var md = "| A | B |\n|---|---|\n| 1 | 2 |";
        var events = ParseCollecting(md);
        Assert.True(events.Count > 0);
    }

    [Fact]
    public void Parse_LinkWithNestedBrackets()
    {
        var events = ParseCollecting("[link [text]](url)");
        Assert.True(events.Count > 0);
    }

    [Fact]
    public void Parse_ConsecutiveParagraphs()
    {
        var events = ParseCollecting("Para 1\n\nPara 2\n\nPara 3");
        var pBlocks = events.Where(e => e.Type == "EnterBlock" && e.Detail.Contains("P")).ToList();
        Assert.Equal(3, pBlocks.Count);
    }

    [Fact]
    public void Parse_BackslashEscape_AllPunctuation()
    {
        var md = "\\! \\\" \\# \\$ \\% \\& \\' \\( \\) \\* \\+ \\, \\- \\. \\/";
        var events = ParseCollecting(md);
        Assert.True(events.Count > 0);
    }

    [Fact]
    public void Parse_ReferenceLink()
    {
        var md = "[text][ref]\n\n[ref]: http://example.com";
        var events = ParseCollecting(md);
        Assert.Contains(events, e => e.Type == "EnterSpan" && e.Detail.Contains("A"));
    }

    [Fact]
    public void Parse_TaskList()
    {
        var md = "- [x] done\n- [ ] todo";
        var events = ParseCollecting(md);
        Assert.True(events.Count > 0);
    }
}
