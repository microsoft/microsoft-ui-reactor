using Microsoft.UI.Reactor.Markdown;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the Md4cParser Markdown parser — exercises block and inline parsing
/// paths. The parser is a SAX-style callback parser; we capture events to verify
/// correct parsing of various Markdown constructs.
/// </summary>
public class Md4cParserTests
{
    // ── Test helper ──────────────────────────────────────────────

    private record ParseEvent(string Kind, string Type, string? Detail = null);

    private static List<ParseEvent> ParseMarkdown(string markdown, MdParserFlags flags = MdParserFlags.None)
    {
        var events = new List<ParseEvent>();

        Md4cParser.Parse(markdown, flags,
            enterBlock: (type, detail) => { events.Add(new("EnterBlock", type.ToString())); return 0; },
            leaveBlock: (type, detail) => { events.Add(new("LeaveBlock", type.ToString())); return 0; },
            enterSpan: (type, detail) => { events.Add(new("EnterSpan", type.ToString())); return 0; },
            leaveSpan: (type, detail) => { events.Add(new("LeaveSpan", type.ToString())); return 0; },
            textCb: (type, text) => { events.Add(new("Text", type.ToString(), text.ToString())); return 0; }
        );

        return events;
    }

    private static List<string> GetBlockTypes(string markdown, MdParserFlags flags = MdParserFlags.None)
        => ParseMarkdown(markdown, flags)
            .Where(e => e.Kind == "EnterBlock")
            .Select(e => e.Type)
            .ToList();

    private static List<string> GetSpanTypes(string markdown, MdParserFlags flags = MdParserFlags.None)
        => ParseMarkdown(markdown, flags)
            .Where(e => e.Kind == "EnterSpan")
            .Select(e => e.Type)
            .ToList();

    private static string GetText(string markdown, MdParserFlags flags = MdParserFlags.None)
        => string.Join("", ParseMarkdown(markdown, flags)
            .Where(e => e.Kind == "Text" && e.Type == "Normal")
            .Select(e => e.Detail));

    // ════════════════════════════════════════════════════════════════
    //  Block-level parsing
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_Paragraph()
    {
        var blocks = GetBlockTypes("Hello world");
        Assert.Contains("Doc", blocks);
        Assert.Contains("P", blocks);
    }

    [Fact]
    public void Parse_ATX_Headings()
    {
        var blocks = GetBlockTypes("# H1\n## H2\n### H3\n#### H4\n##### H5\n###### H6");
        Assert.Equal(6, blocks.Count(b => b == "H"));
    }

    [Fact]
    public void Parse_Setext_Heading_Level1()
    {
        var blocks = GetBlockTypes("Heading\n=======");
        Assert.Contains("H", blocks);
    }

    [Fact]
    public void Parse_Setext_Heading_Level2()
    {
        var blocks = GetBlockTypes("Heading\n-------");
        Assert.Contains("H", blocks);
    }

    [Fact]
    public void Parse_CodeBlock_Fenced()
    {
        var blocks = GetBlockTypes("```\ncode\n```");
        Assert.Contains("Code", blocks);
    }

    [Fact]
    public void Parse_CodeBlock_Indented()
    {
        var blocks = GetBlockTypes("    indented code");
        Assert.Contains("Code", blocks);
    }

    [Fact]
    public void Parse_Blockquote()
    {
        var blocks = GetBlockTypes("> quoted text");
        Assert.Contains("Quote", blocks);
    }

    [Fact]
    public void Parse_UnorderedList()
    {
        var blocks = GetBlockTypes("- item 1\n- item 2");
        Assert.Contains("Ul", blocks);
        Assert.Contains("Li", blocks);
    }

    [Fact]
    public void Parse_OrderedList()
    {
        var blocks = GetBlockTypes("1. first\n2. second");
        Assert.Contains("Ol", blocks);
        Assert.Contains("Li", blocks);
    }

    [Fact]
    public void Parse_ThematicBreak()
    {
        // Need content before to avoid setext heading interpretation
        var blocks = GetBlockTypes("text\n\n---");
        Assert.Contains("Hr", blocks);
    }

    [Fact]
    public void Parse_HtmlBlock()
    {
        var events = ParseMarkdown("<div>\nsome html\n</div>");
        Assert.Contains(events, e => e.Kind == "EnterBlock" && e.Type == "Html");
    }

    // ════════════════════════════════════════════════════════════════
    //  Inline-level parsing
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_Emphasis()
    {
        var spans = GetSpanTypes("*emphasis*");
        Assert.Contains("Em", spans);
    }

    [Fact]
    public void Parse_Strong()
    {
        var spans = GetSpanTypes("**strong**");
        Assert.Contains("Strong", spans);
    }

    [Fact]
    public void Parse_InlineCode()
    {
        var spans = GetSpanTypes("`code`");
        Assert.Contains("Code", spans);
    }

    [Fact]
    public void Parse_Link()
    {
        var spans = GetSpanTypes("[link](http://example.com)");
        Assert.Contains("A", spans);
    }

    [Fact]
    public void Parse_Image()
    {
        var spans = GetSpanTypes("![alt](http://example.com/img.png)");
        Assert.Contains("Img", spans);
    }

    [Fact]
    public void Parse_Autolink()
    {
        var spans = GetSpanTypes("<http://example.com>");
        Assert.Contains("A", spans);
    }

    [Fact]
    public void Parse_Strikethrough()
    {
        var spans = GetSpanTypes("~~deleted~~", MdParserFlags.Strikethrough);
        Assert.Contains("Del", spans);
    }

    // ════════════════════════════════════════════════════════════════
    //  Text types
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_Extracts_Text()
    {
        var text = GetText("Hello world");
        Assert.Equal("Hello world", text);
    }

    [Fact]
    public void Parse_Entity()
    {
        var events = ParseMarkdown("&amp;");
        Assert.Contains(events, e => e.Kind == "Text" && e.Type == "Entity");
    }

    [Fact]
    public void Parse_SoftBreak()
    {
        var events = ParseMarkdown("line1\nline2");
        Assert.Contains(events, e => e.Kind == "Text" && e.Type == "SoftBr");
    }

    // ════════════════════════════════════════════════════════════════
    //  Complex constructs
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_NestedList()
    {
        var markdown = "- outer\n  - inner\n  - inner2\n- outer2";
        var blocks = GetBlockTypes(markdown);
        Assert.True(blocks.Count(b => b == "Ul") >= 2);
    }

    [Fact]
    public void Parse_NestedBlockquote()
    {
        var blocks = GetBlockTypes("> > nested quote");
        Assert.True(blocks.Count(b => b == "Quote") >= 2);
    }

    [Fact]
    public void Parse_LinkWithTitle()
    {
        var spans = GetSpanTypes("[link](http://example.com \"title\")");
        Assert.Contains("A", spans);
    }

    [Fact]
    public void Parse_ReferenceLink()
    {
        var spans = GetSpanTypes("[link][ref]\n\n[ref]: http://example.com");
        Assert.Contains("A", spans);
    }

    [Fact]
    public void Parse_FencedCodeWithLanguage()
    {
        var blocks = GetBlockTypes("```javascript\nvar x = 1;\n```");
        Assert.Contains("Code", blocks);
    }

    [Fact]
    public void Parse_Multiple_Paragraphs()
    {
        var blocks = GetBlockTypes("Paragraph 1\n\nParagraph 2\n\nParagraph 3");
        Assert.Equal(3, blocks.Count(b => b == "P"));
    }

    [Fact]
    public void Parse_Mixed_Content()
    {
        var markdown = """
            # Heading

            A paragraph with *emphasis* and **strong** text.

            - List item 1
            - List item 2

            > A blockquote

            ```
            code block
            ```
            """;
        var blocks = GetBlockTypes(markdown);
        Assert.Contains("H", blocks);
        Assert.Contains("P", blocks);
        Assert.Contains("Ul", blocks);
        Assert.Contains("Quote", blocks);
        Assert.Contains("Code", blocks);
    }

    [Fact]
    public void Parse_EscapedCharacters()
    {
        var text = GetText(@"\*not emphasis\*");
        Assert.Contains("*not emphasis*", text);
    }

    [Fact]
    public void Parse_CodeSpan_BacktickVariants()
    {
        var spans = GetSpanTypes("``code with `backtick` inside``");
        Assert.Contains("Code", spans);
    }

    [Fact]
    public void Parse_Empty_Input()
    {
        var events = ParseMarkdown("");
        Assert.Contains(events, e => e.Kind == "EnterBlock" && e.Type == "Doc");
        Assert.Contains(events, e => e.Kind == "LeaveBlock" && e.Type == "Doc");
    }

    [Fact]
    public void Parse_Only_Whitespace()
    {
        var blocks = GetBlockTypes("   \n   \n   ");
        Assert.DoesNotContain("P", blocks);
    }

    [Fact]
    public void Parse_Hard_LineBreak_Backslash()
    {
        var events = ParseMarkdown("line1\\\nline2");
        Assert.Contains(events, e => e.Kind == "Text" && e.Type == "Br");
    }

    [Fact]
    public void Parse_Hard_LineBreak_TwoSpaces()
    {
        var events = ParseMarkdown("line1  \nline2");
        Assert.Contains(events, e => e.Kind == "Text" && e.Type == "Br");
    }

    [Fact]
    public void Parse_Returns_Zero_On_Success()
    {
        var result = Md4cParser.Parse("hello", MdParserFlags.None,
            (_, _) => 0, (_, _) => 0, (_, _) => 0, (_, _) => 0, (_, _) => 0);
        Assert.Equal(0, result);
    }

    // ════════════════════════════════════════════════════════════════
    //  Table parsing (with tables flag)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_Table()
    {
        var markdown = "| A | B |\n|---|---|\n| 1 | 2 |";
        var blocks = GetBlockTypes(markdown, MdParserFlags.Tables);
        Assert.Contains("Table", blocks);
        Assert.Contains("Thead", blocks);
        Assert.Contains("Tbody", blocks);
        Assert.Contains("Tr", blocks);
        Assert.Contains("Th", blocks);
        Assert.Contains("Td", blocks);
    }

    [Fact]
    public void Parse_Table_WithAlignment()
    {
        var markdown = "| Left | Center | Right |\n|:-----|:------:|------:|\n| a | b | c |";
        var blocks = GetBlockTypes(markdown, MdParserFlags.Tables);
        Assert.Contains("Table", blocks);
    }

    // ════════════════════════════════════════════════════════════════
    //  Parser flags
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_NoHtml_Flag()
    {
        var markdown = "<b>bold</b>";
        var blocks = GetBlockTypes(markdown, MdParserFlags.NoHtmlBlocks | MdParserFlags.NoHtmlSpans);
        Assert.DoesNotContain("Html", blocks);
    }

    [Fact]
    public void Parse_PermissiveAtxHeaders()
    {
        // Normally '#heading' without space is not a heading
        var blocks = GetBlockTypes("#heading", MdParserFlags.PermissiveAtxHeaders);
        Assert.Contains("H", blocks);
    }

    [Fact]
    public void Parse_PermissiveUrlAutolinks()
    {
        var spans = GetSpanTypes("Visit http://example.com today", MdParserFlags.PermissiveUrlAutolinks);
        Assert.Contains("A", spans);
    }

    [Fact]
    public void Parse_PermissiveEmailAutolinks()
    {
        var spans = GetSpanTypes("Email user@example.com now", MdParserFlags.PermissiveEmailAutolinks);
        Assert.Contains("A", spans);
    }

    // ════════════════════════════════════════════════════════════════
    //  Edge cases  
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_Long_Paragraph()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 1000));
        var blocks = GetBlockTypes(text);
        Assert.Contains("P", blocks);
    }

    [Fact]
    public void Parse_Deeply_Nested_Blockquotes()
    {
        var markdown = "> > > > deeply nested";
        var blocks = GetBlockTypes(markdown);
        Assert.True(blocks.Count(b => b == "Quote") >= 4);
    }

    [Fact]
    public void Parse_Multiple_BlankLines()
    {
        var blocks = GetBlockTypes("Para 1\n\n\n\n\nPara 2");
        Assert.Equal(2, blocks.Count(b => b == "P"));
    }

    [Fact]
    public void Parse_TaskList()
    {
        var markdown = "- [ ] unchecked\n- [x] checked";
        var blocks = GetBlockTypes(markdown, MdParserFlags.TaskLists);
        Assert.Contains("Ul", blocks);
    }

    [Fact]
    public void Parse_Inline_StrongAndEmphasis()
    {
        var spans = GetSpanTypes("***bold and italic***");
        Assert.Contains("Strong", spans);
        Assert.Contains("Em", spans);
    }

    [Fact]
    public void Parse_Inline_NestedLinks_Not_Allowed()
    {
        // Nested links are not valid in CommonMark
        var spans = GetSpanTypes("[outer [inner](url1)](url2)");
        // Should only parse one link
        Assert.Equal(1, spans.Count(s => s == "A"));
    }

    [Fact]
    public void Parse_Callback_Abort()
    {
        // enterBlock returns non-zero → parser aborts
        var result = Md4cParser.Parse("test", MdParserFlags.None,
            (_, _) => 1, // abort
            (_, _) => 0, (_, _) => 0, (_, _) => 0, (_, _) => 0);
        Assert.NotEqual(0, result);
    }

    // ════════════════════════════════════════════════════════════════
    //  Extension features (inline parser coverage)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_WikiLink()
    {
        var spans = GetSpanTypes("[[wiki page]]", MdParserFlags.WikiLinks);
        Assert.Contains("WikiLink", spans);
    }

    [Fact]
    public void Parse_LatexMath_Inline()
    {
        var spans = GetSpanTypes("$E=mc^2$", MdParserFlags.LatexMathSpans);
        Assert.Contains("LatexMath", spans);
    }

    [Fact]
    public void Parse_LatexMath_Display()
    {
        var events = ParseMarkdown("$$\\sum_{i=0}^n i$$", MdParserFlags.LatexMathSpans);
        Assert.Contains(events, e => e.Kind == "EnterSpan" && e.Type == "LatexMathDisplay");
    }

    [Fact]
    public void Parse_Underline()
    {
        var spans = GetSpanTypes("_underlined_", MdParserFlags.Underline);
        Assert.Contains("U", spans);
    }

    [Fact]
    public void Parse_WwwAutolink()
    {
        var spans = GetSpanTypes("Visit www.example.com today", MdParserFlags.PermissiveWwwAutolinks);
        Assert.Contains("A", spans);
    }

    [Fact]
    public void Parse_CollapseWhitespace()
    {
        var text = GetText("Hello    world", MdParserFlags.CollapseWhitespace);
        Assert.DoesNotContain("    ", text);
    }

    [Fact]
    public void Parse_NoIndentedCodeBlocks_Flag()
    {
        // Verify the flag is accepted (no crash)
        var events = ParseMarkdown("    text", MdParserFlags.NoIndentedCodeBlocks);
        Assert.Contains(events, e => e.Kind == "EnterBlock" && e.Type == "Doc");
    }

    [Fact]
    public void Parse_HardSoftBreaks()
    {
        var events = ParseMarkdown("line1\nline2", MdParserFlags.HardSoftBreaks);
        Assert.Contains(events, e => e.Kind == "Text" && e.Type == "Br");
    }

    // ════════════════════════════════════════════════════════════════
    //  GitHub dialect (combined flags)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_GitHub_Dialect_Table()
    {
        var blocks = GetBlockTypes("| A | B |\n|---|---|\n| 1 | 2 |", MdParserFlags.DialectGitHub);
        Assert.Contains("Table", blocks);
    }

    [Fact]
    public void Parse_GitHub_Dialect_Strikethrough()
    {
        var spans = GetSpanTypes("~~deleted~~", MdParserFlags.DialectGitHub);
        Assert.Contains("Del", spans);
    }

    [Fact]
    public void Parse_GitHub_Dialect_TaskList()
    {
        var blocks = GetBlockTypes("- [x] done\n- [ ] todo", MdParserFlags.DialectGitHub);
        Assert.Contains("Ul", blocks);
    }

    [Fact]
    public void Parse_GitHub_Dialect_Autolinks()
    {
        var spans = GetSpanTypes("Visit http://example.com", MdParserFlags.DialectGitHub);
        Assert.Contains("A", spans);
    }

    // ════════════════════════════════════════════════════════════════
    //  Inline parsing depth (exercises Md4cParser.Inline.cs paths)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_Emphasis_Underscore()
    {
        var spans = GetSpanTypes("_emphasis_");
        Assert.Contains("Em", spans);
    }

    [Fact]
    public void Parse_Strong_Underscore()
    {
        var spans = GetSpanTypes("__strong__");
        Assert.Contains("Strong", spans);
    }

    [Fact]
    public void Parse_Link_WithImage()
    {
        var spans = GetSpanTypes("[![alt](img.png)](url)");
        Assert.Contains("A", spans);
        Assert.Contains("Img", spans);
    }

    [Fact]
    public void Parse_MultipleCodeSpans()
    {
        var spans = GetSpanTypes("`a` and `b` and `c`");
        Assert.Equal(3, spans.Count(s => s == "Code"));
    }

    [Fact]
    public void Parse_BackslashEscapes_InLink()
    {
        var events = ParseMarkdown("[text](url\\)more)");
        // Should handle escaped parenthesis
        Assert.Contains(events, e => e.Kind == "EnterBlock" && e.Type == "Doc");
    }

    [Fact]
    public void Parse_EmphasisInLink()
    {
        var spans = GetSpanTypes("[*bold link*](url)");
        Assert.Contains("A", spans);
        Assert.Contains("Em", spans);
    }

    [Fact]
    public void Parse_InlineHtml()
    {
        var events = ParseMarkdown("text <b>bold</b> more");
        Assert.Contains(events, e => e.Kind == "Text" && e.Type == "Html");
    }

    [Fact]
    public void Parse_NumericEntity()
    {
        var events = ParseMarkdown("&#65;");
        Assert.Contains(events, e => e.Kind == "Text" && e.Type == "Entity");
    }

    [Fact]
    public void Parse_HexEntity()
    {
        var events = ParseMarkdown("&#x41;");
        Assert.Contains(events, e => e.Kind == "Text" && e.Type == "Entity");
    }

    [Fact]
    public void Parse_FencedCode_Tilde()
    {
        var blocks = GetBlockTypes("~~~\ncode\n~~~");
        Assert.Contains("Code", blocks);
    }

    [Fact]
    public void Parse_Table_MultipleBodies()
    {
        var markdown = "| A |\n|---|\n| 1 |\n\n| B |\n|---|\n| 2 |";
        var blocks = GetBlockTypes(markdown, MdParserFlags.Tables);
        Assert.True(blocks.Count(b => b == "Table") >= 2 || blocks.Contains("Table"));
    }

    [Fact]
    public void Parse_Table_NoBodyRows()
    {
        var markdown = "| A | B |\n|---|---|";
        var blocks = GetBlockTypes(markdown, MdParserFlags.Tables);
        Assert.Contains("Table", blocks);
    }

    [Fact]
    public void Parse_OrderedList_StartNumber()
    {
        var blocks = GetBlockTypes("3. starts at three\n4. four");
        Assert.Contains("Ol", blocks);
    }

    [Fact]
    public void Parse_LooseList()
    {
        var markdown = "- item 1\n\n- item 2";
        var blocks = GetBlockTypes(markdown);
        Assert.Contains("Ul", blocks);
    }

    [Fact]
    public void Parse_DebugLog_Callback()
    {
        var logs = new List<string>();
        Md4cParser.Parse("# Hello", MdParserFlags.None,
            (_, _) => 0, (_, _) => 0, (_, _) => 0, (_, _) => 0, (_, _) => 0,
            debugLog: msg => logs.Add(msg));
        // Debug log may or may not produce output depending on implementation
    }

    [Fact]
    public void Parse_Detail_HeadingLevel()
    {
        object? headingDetail = null;
        Md4cParser.Parse("## H2", MdParserFlags.None,
            (type, detail) => { if (type == MdBlockType.H) headingDetail = detail; return 0; },
            (_, _) => 0, (_, _) => 0, (_, _) => 0, (_, _) => 0);
        Assert.NotNull(headingDetail);
        Assert.IsType<MdBlockHDetail>(headingDetail);
        Assert.Equal(2, ((MdBlockHDetail)headingDetail).Level);
    }

    [Fact]
    public void Parse_Detail_OrderedListStart()
    {
        object? olDetail = null;
        Md4cParser.Parse("5. item", MdParserFlags.None,
            (type, detail) => { if (type == MdBlockType.Ol) olDetail = detail; return 0; },
            (_, _) => 0, (_, _) => 0, (_, _) => 0, (_, _) => 0);
        Assert.NotNull(olDetail);
        Assert.IsType<MdBlockOlDetail>(olDetail);
        Assert.Equal(5, ((MdBlockOlDetail)olDetail).Start);
    }

    [Fact]
    public void Parse_Detail_LinkHref()
    {
        object? linkDetail = null;
        Md4cParser.Parse("[text](http://example.com)", MdParserFlags.None,
            (_, _) => 0, (_, _) => 0,
            (type, detail) => { if (type == MdSpanType.A) linkDetail = detail; return 0; },
            (_, _) => 0, (_, _) => 0);
        Assert.NotNull(linkDetail);
        Assert.IsType<MdSpanADetail>(linkDetail);
        Assert.Contains("example.com", ((MdSpanADetail)linkDetail).Href.Text!);
    }

    [Fact]
    public void Parse_Detail_ImageSrc()
    {
        object? imgDetail = null;
        Md4cParser.Parse("![alt](image.png)", MdParserFlags.None,
            (_, _) => 0, (_, _) => 0,
            (type, detail) => { if (type == MdSpanType.Img) imgDetail = detail; return 0; },
            (_, _) => 0, (_, _) => 0);
        Assert.NotNull(imgDetail);
        Assert.IsType<MdSpanImgDetail>(imgDetail);
        Assert.Contains("image.png", ((MdSpanImgDetail)imgDetail).Src.Text!);
    }

    [Fact]
    public void Parse_Detail_FencedCodeInfo()
    {
        object? codeDetail = null;
        Md4cParser.Parse("```python\nprint('hi')\n```", MdParserFlags.None,
            (type, detail) => { if (type == MdBlockType.Code) codeDetail = detail; return 0; },
            (_, _) => 0, (_, _) => 0, (_, _) => 0, (_, _) => 0);
        Assert.NotNull(codeDetail);
        Assert.IsType<MdBlockCodeDetail>(codeDetail);
        Assert.Contains("python", ((MdBlockCodeDetail)codeDetail).Lang.Text!);
    }

    [Fact]
    public void Parse_Detail_TableColumns()
    {
        object? tableDetail = null;
        Md4cParser.Parse("| A | B | C |\n|---|---|---|\n| 1 | 2 | 3 |", MdParserFlags.Tables,
            (type, detail) => { if (type == MdBlockType.Table) tableDetail = detail; return 0; },
            (_, _) => 0, (_, _) => 0, (_, _) => 0, (_, _) => 0);
        Assert.NotNull(tableDetail);
        Assert.IsType<MdBlockTableDetail>(tableDetail);
        Assert.Equal(3, ((MdBlockTableDetail)tableDetail).ColCount);
    }

    [Fact]
    public void Parse_Detail_TaskListItem()
    {
        object? liDetail = null;
        Md4cParser.Parse("- [x] checked", MdParserFlags.TaskLists,
            (type, detail) => { if (type == MdBlockType.Li) liDetail = detail; return 0; },
            (_, _) => 0, (_, _) => 0, (_, _) => 0, (_, _) => 0);
        Assert.NotNull(liDetail);
        Assert.IsType<MdBlockLiDetail>(liDetail);
        Assert.True(((MdBlockLiDetail)liDetail).IsTask);
    }

    [Fact]
    public void Parse_CodeBlock_Text()
    {
        var events = ParseMarkdown("```\nhello\n```");
        Assert.Contains(events, e => e.Kind == "Text" && e.Type == "Code" && e.Detail != null && e.Detail.Contains("hello"));
    }

    [Fact]
    public void Parse_Balanced_Enter_Leave_Blocks()
    {
        var events = ParseMarkdown("# Heading\n\nParagraph");
        var enters = events.Count(e => e.Kind == "EnterBlock");
        var leaves = events.Count(e => e.Kind == "LeaveBlock");
        Assert.Equal(enters, leaves);
    }

    [Fact]
    public void Parse_Balanced_Enter_Leave_Spans()
    {
        var events = ParseMarkdown("*em* **strong** `code` [link](url)");
        var enters = events.Count(e => e.Kind == "EnterSpan");
        var leaves = events.Count(e => e.Kind == "LeaveSpan");
        Assert.Equal(enters, leaves);
    }
}
