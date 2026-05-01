using Microsoft.UI.Reactor.Markdown;
using System.Text;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Md4cGenerated;

/// <summary>
/// Ported from md4c/test/spec-underline.txt
/// </summary>
public class Md4cUnderlineTest
{
    [Fact]
    public void Example_0001()
    {
        var md = "_foo_";
        var expected = "<p><u>foo</u></p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.Underline);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0002()
    {
        var md = "___foo___";
        var expected = "<p><u><u><u>foo</u></u></u></p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.Underline);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0003()
    {
        var md = "foo_bar_baz";
        var expected = "<p>foo_bar_baz</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.Underline);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0004()
    {
        var md = "_foo _bar";
        var expected = "<p>_foo _bar</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.Underline);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

}
