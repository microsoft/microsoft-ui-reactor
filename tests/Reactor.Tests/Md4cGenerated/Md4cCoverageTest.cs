using Microsoft.UI.Reactor.Markdown;
using System.Text;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Md4cGenerated;

/// <summary>
/// Ported from md4c/test/coverage.txt
/// </summary>
public class Md4cCoverageTest
{
    [Fact]
    public void Example_0001()
    {
        var md = "*foo *bar";
        var expected = "<p>*foo *bar</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0002()
    {
        var md = "*foo¡*bar";
        var expected = "<p>*foo¡*bar</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0003()
    {
        var md = "[Příliš žluťoučký kůň úpěl ďábelské ódy.]\n\n[PŘÍLIŠ ŽLUŤOUČKÝ KŮŇ ÚPĚL ĎÁBELSKÉ ÓDY.]: /url";
        var expected = "<p><a href=\"/url\">Příliš žluťoučký kůň úpěl ďábelské ódy.</a></p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0004()
    {
        var md = "X__foo__X";
        var expected = "<p>X__foo__X</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0005()
    {
        var md = "Ř__foo__Ř";
        var expected = "<p>Ř__foo__Ř</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0006()
    {
        var md = "ண__foo__ண";
        var expected = "<p>ண__foo__ண</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0007()
    {
        var md = "𓅂__foo__𓅂";
        var expected = "<p>𓅂__foo__𓅂</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0008()
    {
        var md = "x\t__foo__\t";
        var expected = "<p>x <strong>foo</strong></p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0009()
    {
        var md = " __foo__";
        var expected = "<p> <strong>foo</strong> </p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0010()
    {
        var md = " __foo__";
        var expected = "<p> <strong>foo</strong> </p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0011()
    {
        var md = ".__foo__.";
        var expected = "<p>.<strong>foo</strong>.</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0012()
    {
        var md = "·__foo__·";
        var expected = "<p>·<strong>foo</strong>·</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0013()
    {
        var md = "಄__foo__಄";
        var expected = "<p>಄<strong>foo</strong>಄</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0014()
    {
        var md = "𐎟__foo__𐎟";
        var expected = "<p>𐎟<strong>foo</strong>𐎟</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0015()
    {
        var md = "[link](</url\\.with\\.escape>)";
        var expected = "<p><a href=\"/url.with.escape\">link</a></p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0016()
    {
        var md = "[foo bar]\n\n[foo bar]: /url";
        var expected = "<p><a href=\"/url\">foo bar</a></p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0017()
    {
        var md = "> [link](/url 'foo\n> bar')";
        var expected = "<blockquote>\n<p><a href=\"/url\" title=\"foo\nbar\">link</a></p>\n</blockquote>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0018()
    {
        var md = "[foo]: /foo\n[qnptgbh]: /qnptgbh\n[abgbrwcv]: /abgbrwcv\n[abgbrwcv]: /abgbrwcv2\n[abgbrwcv]: /abgbrwcv3\n[abgbrwcv]: /abgbrwcv4\n[alqadfgn]: /alqadfgn\n\n[foo]\n[qnptgbh]\n[abgbrwcv]\n[alqadfgn]\n[axgydtdu]";
        var expected = "<p><a href=\"/foo\">foo</a>\n<a href=\"/qnptgbh\">qnptgbh</a>\n<a href=\"/abgbrwcv\">abgbrwcv</a>\n<a href=\"/alqadfgn\">alqadfgn</a>\n[axgydtdu]</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0019()
    {
        var md = "foo  bar \t baz";
        var expected = "<p>foo bar baz</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.CollapseWhitespace);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

}
