using System.Text;
using Microsoft.UI.Reactor.Markdown;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Unit tests for the Markdown parser (Md4cParser) and HTML renderer (MarkdownHtml).
/// Tests the full parsing pipeline through the public MarkdownHtml.Render API,
/// which exercises Md4cParser, Md4cEntity, and Md4cUnicode under the hood.
/// No WinUI dependencies — pure algorithmic tests.
/// </summary>
public class MarkdownTests
{
    private static string ToHtml(string markdown, MarkdownParserFlags flags = MarkdownParserFlags.DialectGitHub)
    {
        var sb = new StringBuilder();
        int result = MarkdownHtml.Render(markdown, flags, MarkdownHtml.HtmlFlags.None, sb);
        Assert.Equal(0, result);
        return sb.ToString().Trim();
    }

    // ════════════════════════════════════════════════════════════════════
    //  Basic block elements
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Paragraph_Simple()
    {
        var html = ToHtml("Hello world");
        Assert.Equal("<p>Hello world</p>", html);
    }

    [Fact]
    public void Paragraph_Multiple()
    {
        var html = ToHtml("First\n\nSecond");
        Assert.Contains("<p>First</p>", html);
        Assert.Contains("<p>Second</p>", html);
    }

    [Fact]
    public void Heading_ATX_Levels()
    {
        Assert.Contains("<h1>H1</h1>", ToHtml("# H1"));
        Assert.Contains("<h2>H2</h2>", ToHtml("## H2"));
        Assert.Contains("<h3>H3</h3>", ToHtml("### H3"));
        Assert.Contains("<h4>H4</h4>", ToHtml("#### H4"));
        Assert.Contains("<h5>H5</h5>", ToHtml("##### H5"));
        Assert.Contains("<h6>H6</h6>", ToHtml("###### H6"));
    }

    [Fact]
    public void Heading_Setext()
    {
        Assert.Contains("<h1>H1</h1>", ToHtml("H1\n==="));
        Assert.Contains("<h2>H2</h2>", ToHtml("H2\n---"));
    }

    [Fact]
    public void ThematicBreak()
    {
        Assert.Contains("<hr", ToHtml("---"));
        Assert.Contains("<hr", ToHtml("***"));
        Assert.Contains("<hr", ToHtml("___"));
    }

    // ════════════════════════════════════════════════════════════════════
    //  Inline formatting
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emphasis_Italic()
    {
        Assert.Contains("<em>italic</em>", ToHtml("*italic*"));
        Assert.Contains("<em>italic</em>", ToHtml("_italic_"));
    }

    [Fact]
    public void Strong_Bold()
    {
        Assert.Contains("<strong>bold</strong>", ToHtml("**bold**"));
        Assert.Contains("<strong>bold</strong>", ToHtml("__bold__"));
    }

    [Fact]
    public void Strong_And_Emphasis_Nested()
    {
        var html = ToHtml("***bold italic***");
        // Parser may nest as <em><strong> or <strong><em> — both are valid
        Assert.Contains("<em>", html);
        Assert.Contains("<strong>", html);
        Assert.Contains("bold italic", html);
    }

    [Fact]
    public void InlineCode()
    {
        Assert.Contains("<code>code</code>", ToHtml("`code`"));
    }

    [Fact]
    public void InlineCode_With_Backticks()
    {
        Assert.Contains("<code>a`b</code>", ToHtml("``a`b``"));
    }

    [Fact]
    public void Strikethrough_GitHub()
    {
        var html = ToHtml("~~deleted~~", MarkdownParserFlags.DialectGitHub);
        Assert.Contains("<del>deleted</del>", html);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Links and images
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Link_Inline()
    {
        var html = ToHtml("[link](https://example.com)");
        Assert.Contains("<a href=\"https://example.com\">link</a>", html);
    }

    [Fact]
    public void Link_With_Title()
    {
        var html = ToHtml("[link](https://example.com \"title\")");
        Assert.Contains("title=\"title\"", html);
    }

    [Fact]
    public void Image_Inline()
    {
        var html = ToHtml("![alt](image.png)");
        Assert.Contains("<img", html);
        Assert.Contains("src=\"image.png\"", html);
        Assert.Contains("alt=\"alt\"", html);
    }

    [Fact]
    public void AutoLink()
    {
        var html = ToHtml("<https://example.com>");
        Assert.Contains("href=\"https://example.com\"", html);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Lists
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnorderedList()
    {
        var html = ToHtml("- A\n- B\n- C");
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>", html);
        Assert.Contains("A", html);
        Assert.Contains("C", html);
    }

    [Fact]
    public void OrderedList()
    {
        var html = ToHtml("1. First\n2. Second\n3. Third");
        Assert.Contains("<ol>", html);
        Assert.Contains("<li>", html);
        Assert.Contains("First", html);
    }

    [Fact]
    public void NestedList()
    {
        var html = ToHtml("- A\n  - B\n  - C\n- D");
        // Should have nested ul
        Assert.Contains("<ul>", html);
        Assert.Contains("B", html);
    }

    [Fact]
    public void TaskList_GitHub()
    {
        var html = ToHtml("- [x] Done\n- [ ] Todo", MarkdownParserFlags.DialectGitHub);
        Assert.Contains("Done", html);
        Assert.Contains("Todo", html);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Code blocks
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void FencedCodeBlock()
    {
        var html = ToHtml("```\ncode here\n```");
        Assert.Contains("<code>", html);
        Assert.Contains("code here", html);
    }

    [Fact]
    public void FencedCodeBlock_With_Language()
    {
        var html = ToHtml("```csharp\nvar x = 1;\n```");
        Assert.Contains("language-csharp", html);
        Assert.Contains("var x = 1;", html);
    }

    [Fact]
    public void IndentedCodeBlock()
    {
        var html = ToHtml("    indented code");
        Assert.Contains("<code>", html);
        Assert.Contains("indented code", html);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Block quotes
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void BlockQuote()
    {
        var html = ToHtml("> quoted text");
        Assert.Contains("<blockquote>", html);
        Assert.Contains("quoted text", html);
    }

    [Fact]
    public void BlockQuote_Nested()
    {
        var html = ToHtml("> outer\n>> inner");
        Assert.Contains("<blockquote>", html);
        Assert.Contains("inner", html);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Tables (GitHub dialect)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Table_GitHub()
    {
        var md = "| A | B |\n|---|---|\n| 1 | 2 |";
        var html = ToHtml(md, MarkdownParserFlags.DialectGitHub);
        Assert.Contains("<table>", html);
        Assert.Contains("<th>", html);
        Assert.Contains("<td>", html);
        Assert.Contains("A", html);
        Assert.Contains("2", html);
    }

    [Fact]
    public void Table_Alignment()
    {
        var md = "| Left | Center | Right |\n|:---|:---:|---:|\n| a | b | c |";
        var html = ToHtml(md, MarkdownParserFlags.DialectGitHub);
        Assert.Contains("<table>", html);
        Assert.Contains("Left", html);
        Assert.Contains("Right", html);
    }

    // ════════════════════════════════════════════════════════════════════
    //  HTML entities
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void HtmlEntity_Named()
    {
        var html = ToHtml("&amp; &lt; &gt;");
        Assert.Contains("&amp;", html);
        Assert.Contains("&lt;", html);
        Assert.Contains("&gt;", html);
    }

    [Fact]
    public void HtmlEntity_Numeric()
    {
        var html = ToHtml("&#65;"); // 'A'
        Assert.Contains("A", html);
    }

    [Fact]
    public void HtmlEntity_Hex()
    {
        var html = ToHtml("&#x41;"); // 'A'
        Assert.Contains("A", html);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Special characters and escaping
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Escape_Backslash()
    {
        var html = ToHtml("\\*not italic\\*");
        Assert.Contains("*not italic*", html);
        Assert.DoesNotContain("<em>", html);
    }

    [Fact]
    public void HardLineBreak()
    {
        var html = ToHtml("line1  \nline2");
        Assert.Contains("<br", html);
    }

    [Fact]
    public void SoftLineBreak()
    {
        var html = ToHtml("line1\nline2");
        // Soft break becomes a newline in the paragraph, not a <br>
        Assert.Contains("line1", html);
        Assert.Contains("line2", html);
    }

    // ════════════════════════════════════════════════════════════════════
    //  HTML passthrough
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void InlineHtml()
    {
        var html = ToHtml("text <b>bold</b> text");
        Assert.Contains("<b>bold</b>", html);
    }

    [Fact]
    public void BlockHtml()
    {
        var html = ToHtml("<div>\ncustom\n</div>");
        Assert.Contains("<div>", html);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Edge cases
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Empty_Input()
    {
        var sb = new StringBuilder();
        int result = MarkdownHtml.Render("", MarkdownParserFlags.None, MarkdownHtml.HtmlFlags.None, sb);
        Assert.Equal(0, result);
        Assert.Equal("", sb.ToString().Trim());
    }

    [Fact]
    public void Whitespace_Only()
    {
        var html = ToHtml("   \n\n   ");
        Assert.Equal("", html);
    }

    [Fact]
    public void Long_Document()
    {
        var lines = Enumerable.Range(0, 100).Select(i => $"Paragraph {i}\n\n").ToArray();
        var html = ToHtml(string.Concat(lines));
        Assert.Contains("Paragraph 0", html);
        Assert.Contains("Paragraph 99", html);
    }

    [Fact]
    public void Deeply_Nested_Quotes()
    {
        var md = string.Concat(Enumerable.Range(0, 10).Select(i => new string('>', i + 1) + " level\n\n"));
        var html = ToHtml(md);
        Assert.Contains("<blockquote>", html);
        Assert.Contains("level", html);
    }

    [Fact]
    public void Complex_Document()
    {
        var md = """
            # Title

            A paragraph with **bold**, *italic*, and `code`.

            ## Lists

            - Item 1
            - Item 2
              - Nested
            - Item 3

            ## Code

            ```python
            def hello():
                print("world")
            ```

            > A blockquote

            | Col1 | Col2 |
            |------|------|
            | a    | b    |

            ---

            [Link](https://example.com)
            """;
        var html = ToHtml(md, MarkdownParserFlags.DialectGitHub);
        Assert.Contains("<h1>Title</h1>", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>italic</em>", html);
        Assert.Contains("<code>code</code>", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>", html);
        Assert.Contains("language-python", html);
        Assert.Contains("<blockquote>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("<hr", html);
        Assert.Contains("href=\"https://example.com\"", html);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Parser flags
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void CommonMark_Mode()
    {
        // In CommonMark mode, strikethrough should NOT be parsed
        var html = ToHtml("~~text~~", MarkdownParserFlags.None);
        Assert.DoesNotContain("<del>", html);
        Assert.Contains("~~text~~", html);
    }

    [Fact]
    public void GitHub_Mode_Tables()
    {
        var md = "| H |\n|---|\n| D |";
        // GitHub dialect enables tables
        var github = ToHtml(md, MarkdownParserFlags.DialectGitHub);
        Assert.Contains("<table>", github);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Renderer flags
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Xhtml_Mode()
    {
        var sb = new StringBuilder();
        MarkdownHtml.Render("---", MarkdownParserFlags.None, MarkdownHtml.HtmlFlags.Xhtml, sb);
        var html = sb.ToString();
        Assert.Contains("<hr />", html);
    }

    [Fact]
    public void SkipUtf8Bom()
    {
        var sb = new StringBuilder();
        MarkdownHtml.Render("\uFEFFHello", MarkdownParserFlags.None, MarkdownHtml.HtmlFlags.SkipUtf8Bom, sb);
        var html = sb.ToString();
        Assert.Contains("Hello", html);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Stress tests
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Stress_Many_Inline_Elements()
    {
        // Many inline elements in a single paragraph
        var parts = Enumerable.Range(0, 50)
            .Select(i => i % 3 == 0 ? $"**b{i}**" : i % 3 == 1 ? $"*i{i}*" : $"`c{i}`");
        var md = string.Join(" ", parts);
        var html = ToHtml(md);
        Assert.Contains("<strong>b0</strong>", html);
        Assert.Contains("<em>i1</em>", html);
        Assert.Contains("<code>c2</code>", html);
    }

    [Fact]
    public void Stress_Large_Table()
    {
        var header = "| " + string.Join(" | ", Enumerable.Range(0, 10).Select(i => $"H{i}")) + " |";
        var sep = "| " + string.Join(" | ", Enumerable.Repeat("---", 10)) + " |";
        var rows = Enumerable.Range(0, 20)
            .Select(r => "| " + string.Join(" | ", Enumerable.Range(0, 10).Select(c => $"R{r}C{c}")) + " |");
        var md = header + "\n" + sep + "\n" + string.Join("\n", rows);
        var html = ToHtml(md, MarkdownParserFlags.DialectGitHub);
        Assert.Contains("<table>", html);
        Assert.Contains("R19C9", html);
    }

    [Fact]
    public void Stress_Nested_Lists_Deep()
    {
        var md = "";
        for (int i = 0; i < 8; i++)
            md += new string(' ', i * 2) + $"- Level {i}\n";
        var html = ToHtml(md);
        Assert.Contains("Level 0", html);
        Assert.Contains("Level 7", html);
    }
}
