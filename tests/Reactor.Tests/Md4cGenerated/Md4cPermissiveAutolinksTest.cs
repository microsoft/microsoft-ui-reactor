using Microsoft.UI.Reactor.Markdown;
using System.Text;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Md4cGenerated;

/// <summary>
/// Ported from md4c/test/spec-permissive-autolinks.txt
/// </summary>
public class Md4cPermissiveAutolinksTest
{
    [Fact]
    public void Example_0001()
    {
        var md = "<mailto:john.doe@gmail.com>\n<https://example.com>";
        var expected = "<p><a href=\"mailto:john.doe@gmail.com\">mailto:john.doe@gmail.com</a>\n<a href=\"https://example.com\">https://example.com</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0002()
    {
        var md = "john.doe@gmail.com\nhttps://www.example.com\nwww.example.com";
        var expected = "<p><a href=\"mailto:john.doe@gmail.com\">john.doe@gmail.com</a>\n<a href=\"https://www.example.com\">https://www.example.com</a>\n<a href=\"http://www.example.com\">www.example.com</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks | MarkdownParserFlags.PermissiveEmailAutolinks | MarkdownParserFlags.PermissiveUrlAutolinks | MarkdownParserFlags.PermissiveWwwAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0003()
    {
        var md = ":john.doe@gmail.com\n:https://www.example.com\n:www.example.com";
        var expected = "<p>:john.doe@gmail.com\n:https://www.example.com\n:www.example.com</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks | MarkdownParserFlags.PermissiveEmailAutolinks | MarkdownParserFlags.PermissiveUrlAutolinks | MarkdownParserFlags.PermissiveWwwAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0004()
    {
        var md = "[john.doe@gmail.com\n(https://www.example.com\n{www.example.com";
        var expected = "<p>[<a href=\"mailto:john.doe@gmail.com\">john.doe@gmail.com</a>\n(<a href=\"https://www.example.com\">https://www.example.com</a>\n{<a href=\"http://www.example.com\">www.example.com</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks | MarkdownParserFlags.PermissiveEmailAutolinks | MarkdownParserFlags.PermissiveUrlAutolinks | MarkdownParserFlags.PermissiveWwwAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0005()
    {
        var md = "john.doe@gmail.com]\nhttps://www.example.com)\nwww.example.com}";
        var expected = "<p><a href=\"mailto:john.doe@gmail.com\">john.doe@gmail.com</a>]\n<a href=\"https://www.example.com\">https://www.example.com</a>)\n<a href=\"http://www.example.com\">www.example.com</a>}</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks | MarkdownParserFlags.PermissiveEmailAutolinks | MarkdownParserFlags.PermissiveUrlAutolinks | MarkdownParserFlags.PermissiveWwwAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0006()
    {
        var md = "Have you ever visited http://zombo.com?";
        var expected = "<p>Have you ever visited <a href=\"http://zombo.com\">http://zombo.com</a>?</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks | MarkdownParserFlags.PermissiveUrlAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0007()
    {
        var md = "You may contact me at **john.doe@example.com**.";
        var expected = "<p>You may contact me at <strong><a href=\"mailto:john.doe@example.com\">john.doe@example.com</a></strong>.</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks | MarkdownParserFlags.PermissiveEmailAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0008()
    {
        var md = "*john.doe@example.com\n\njohn.doe@example.com*";
        var expected = "<p>*john.doe@example.com</p>\n<p>john.doe@example.com*</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks | MarkdownParserFlags.PermissiveEmailAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0009()
    {
        var md = "https://example.com\nhttp://example.com\nftp://example.com\n\nssh://example.com";
        var expected = "<p><a href=\"https://example.com\">https://example.com</a>\n<a href=\"http://example.com\">http://example.com</a>\n<a href=\"ftp://example.com\">ftp://example.com</a></p>\n<p>ssh://example.com</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks | MarkdownParserFlags.PermissiveUrlAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0010()
    {
        var md = "https://example.com/images/branding/logo_272x92.png";
        var expected = "<p><a href=\"https://example.com/images/branding/logo_272x92.png\">https://example.com/images/branding/logo_272x92.png</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks | MarkdownParserFlags.PermissiveUrlAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0011()
    {
        var md = "https://www.google.com/search?q=md4c+markdown";
        var expected = "<p><a href=\"https://www.google.com/search?q=md4c+markdown\">https://www.google.com/search?q=md4c+markdown</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks | MarkdownParserFlags.PermissiveUrlAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0012()
    {
        var md = "https://example.com#fragment";
        var expected = "<p><a href=\"https://example.com#fragment\">https://example.com#fragment</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks | MarkdownParserFlags.PermissiveUrlAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0013()
    {
        var md = "http://commonmark.org\n\n(Visit https://encrypted.google.com/search?q=Markup+(business))\n\nAnonymous FTP is available at ftp://foo.bar.baz.";
        var expected = "<p><a href=\"http://commonmark.org\">http://commonmark.org</a></p>\n<p>(Visit <a href=\"https://encrypted.google.com/search?q=Markup+(business)\">https://encrypted.google.com/search?q=Markup+(business)</a>)</p>\n<p>Anonymous FTP is available at <a href=\"ftp://foo.bar.baz\">ftp://foo.bar.baz</a>.</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks | MarkdownParserFlags.PermissiveUrlAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0014()
    {
        var md = "www.google.com/search?q=Markdown";
        var expected = "<p><a href=\"http://www.google.com/search?q=Markdown\">www.google.com/search?q=Markdown</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveAutolinks | MarkdownParserFlags.PermissiveWwwAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

}
