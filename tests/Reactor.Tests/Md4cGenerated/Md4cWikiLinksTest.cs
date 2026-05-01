using Microsoft.UI.Reactor.Markdown;
using System.Text;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Md4cGenerated;

/// <summary>
/// Ported from md4c/test/spec-wiki-links.txt
/// </summary>
public class Md4cWikiLinksTest
{
    [Fact]
    public void Example_0001()
    {
        var md = "[[foo]]";
        var expected = "<p><x-wikilink data-target=\"foo\">foo</x-wikilink></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0002()
    {
        var md = "[[foo|bar]]";
        var expected = "<p><x-wikilink data-target=\"foo\">bar</x-wikilink></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0003()
    {
        var md = "[[]]";
        var expected = "<p>[[]]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0004()
    {
        var md = "[[|foo]]";
        var expected = "<p>[[|foo]]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0005()
    {
        var md = "[[foo\nbar]]";
        var expected = "<p>[[foo\nbar]]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0006()
    {
        var md = "[[foo\nbar|baz]]";
        var expected = "<p>[[foo\nbar|baz]]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0007()
    {
        var md = "[[*foo*]]";
        var expected = "<p><x-wikilink data-target=\"*foo*\">*foo*</x-wikilink></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0008()
    {
        var md = "[[foo|![bar](bar.jpg)]]";
        var expected = "<p><x-wikilink data-target=\"foo\"><img src=\"bar.jpg\" alt=\"bar\"></x-wikilink></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0009()
    {
        var md = "[[foo|bar|baz]]";
        var expected = "<p><x-wikilink data-target=\"foo\">bar|baz</x-wikilink></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0010()
    {
        var md = "[[foo\\|bar|baz]]";
        var expected = "<p><x-wikilink data-target=\"foo|bar\">baz</x-wikilink></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0011()
    {
        var md = "[[foo|*bar*]]";
        var expected = "<p><x-wikilink data-target=\"foo\"><em>bar</em></x-wikilink></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0012()
    {
        var md = "[[foo|]]";
        var expected = "<p><x-wikilink data-target=\"foo\">foo</x-wikilink></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0013()
    {
        var md = "[[foo|foo\nbar\nbaz]]";
        var expected = "<p><x-wikilink data-target=\"foo\">foo\nbar\nbaz</x-wikilink></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0014()
    {
        var md = "[[foo]](foo.jpg)";
        var expected = "<p><x-wikilink data-target=\"foo\">foo</x-wikilink>(foo.jpg)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0015()
    {
        var md = "[foo]: /url\n\n[[foo]]";
        var expected = "<p><x-wikilink data-target=\"foo\">foo</x-wikilink></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0016()
    {
        var md = "| A                | B   |\n|------------------|-----|\n| [[foo|*bar*]]    | baz |";
        var expected = "<table>\n<thead>\n<tr>\n<th>A</th>\n<th>B</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td><x-wikilink data-target=\"foo\"><em>bar</em></x-wikilink></td>\n<td>baz</td>\n</tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks | MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0017()
    {
        var md = "![[foo]](foo.jpg)";
        var expected = "<p><img src=\"foo.jpg\" alt=\"[foo]\"></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0018()
    {
        var md = "[[foo]\n\n[foo]: /url";
        var expected = "<p>[<a href=\"/url\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0019()
    {
        var md = "\\[[foo]]\n\n[foo]: /url";
        var expected = "<p>[<a href=\"/url\">foo</a>]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0020()
    {
        var md = "[[foo[[bar]]]]";
        var expected = "<p>[[foo<x-wikilink data-target=\"bar\">bar</x-wikilink>]]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0021()
    {
        var md = "[[12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901]]\n[[12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901|foo]]";
        var expected = "<p>[[12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901]]\n[[12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901|foo]]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0022()
    {
        var md = "[[1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890]]\n[[1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890|foo]]";
        var expected = "<p><x-wikilink data-target=\"1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890\">1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890</x-wikilink>\n<x-wikilink data-target=\"1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890\">foo</x-wikilink></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0023()
    {
        var md = "> [[12345678901234567890123456789012345678901234567890|1234567890\n> 1234567890\n> 1234567890\n> 1234567890\n> 123456789]]";
        var expected = "<blockquote>\n<p><x-wikilink data-target=\"12345678901234567890123456789012345678901234567890\">1234567890\n1234567890\n1234567890\n1234567890\n123456789</x-wikilink></p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

}
