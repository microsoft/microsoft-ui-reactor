using Microsoft.UI.Reactor.Markdown;
using System.Text;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Md4cGenerated;

/// <summary>
/// Ported from md4c/test/spec-strikethrough.txt
/// </summary>
public class Md4cStrikethroughTest
{
    [Fact]
    public void Example_0001()
    {
        var md = "~Hi~ Hello, world!";
        var expected = "<p><del>Hi</del> Hello, world!</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.Strikethrough);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0002()
    {
        var md = "This ~text~~ is curious.";
        var expected = "<p>This ~text~~ is curious.</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.Strikethrough);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0003()
    {
        var md = "foo ~~~bar~~~";
        var expected = "<p>foo ~~~bar~~~</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.Strikethrough);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0004()
    {
        var md = "~foo ~bar";
        var expected = "<p>~foo ~bar</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.Strikethrough);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0005()
    {
        var md = "This ~~has a\n\nnew paragraph~~.";
        var expected = "<p>This ~~has a</p>\n<p>new paragraph~~.</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.Strikethrough);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

}
