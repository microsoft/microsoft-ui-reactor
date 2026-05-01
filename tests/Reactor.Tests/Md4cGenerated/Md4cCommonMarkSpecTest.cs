using Microsoft.UI.Reactor.Markdown;
using System.Text;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Md4cGenerated;

/// <summary>
/// Ported from md4c/test/spec.txt
/// </summary>
public class Md4cCommonMarkSpecTest
{
    [Fact]
    public void Example_0001()
    {
        var md = "\tfoo\tbaz\t\tbim";
        var expected = "<pre><code>foo\tbaz\t\tbim\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0002()
    {
        var md = "  \tfoo\tbaz\t\tbim";
        var expected = "<pre><code>foo\tbaz\t\tbim\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0003()
    {
        var md = "    a\ta\n    ὐ\ta";
        var expected = "<pre><code>a\ta\nὐ\ta\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0004()
    {
        var md = "  - foo\n\n\tbar";
        var expected = "<ul>\n<li>\n<p>foo</p>\n<p>bar</p>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0005()
    {
        var md = "- foo\n\n\t\tbar";
        var expected = "<ul>\n<li>\n<p>foo</p>\n<pre><code>  bar\n</code></pre>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0006()
    {
        var md = ">\t\tfoo";
        var expected = "<blockquote>\n<pre><code>  foo\n</code></pre>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0007()
    {
        var md = "-\t\tfoo";
        var expected = "<ul>\n<li>\n<pre><code>  foo\n</code></pre>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0008()
    {
        var md = "    foo\n\tbar";
        var expected = "<pre><code>foo\nbar\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0009()
    {
        var md = " - foo\n   - bar\n\t - baz";
        var expected = "<ul>\n<li>foo\n<ul>\n<li>bar\n<ul>\n<li>baz</li>\n</ul>\n</li>\n</ul>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0010()
    {
        var md = "#\tFoo";
        var expected = "<h1>Foo</h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0011()
    {
        var md = "*\t*\t*\t";
        var expected = "<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0012()
    {
        var md = "\\!\\\"\\#\\$\\%\\&\\'\\(\\)\\*\\+\\,\\-\\.\\/\\:\\;\\<\\=\\>\\?\\@\\[\\\\\\]\\^\\_\\`\\{\\|\\}\\~";
        var expected = "<p>!&quot;#$%&amp;'()*+,-./:;&lt;=&gt;?@[\\]^_`{|}~</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0013()
    {
        var md = "\\\t\\A\\a\\ \\3\\φ\\«";
        var expected = "<p>\\\t\\A\\a\\ \\3\\φ\\«</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0014()
    {
        var md = "\\*not emphasized*\n\\<br/> not a tag\n\\[not a link](/foo)\n\\`not code`\n1\\. not a list\n\\* not a list\n\\# not a heading\n\\[foo]: /url \"not a reference\"\n\\&ouml; not a character entity";
        var expected = "<p>*not emphasized*\n&lt;br/&gt; not a tag\n[not a link](/foo)\n`not code`\n1. not a list\n* not a list\n# not a heading\n[foo]: /url &quot;not a reference&quot;\n&amp;ouml; not a character entity</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0015()
    {
        var md = "\\\\*emphasis*";
        var expected = "<p>\\<em>emphasis</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0016()
    {
        var md = "foo\\\nbar";
        var expected = "<p>foo<br />\nbar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0017()
    {
        var md = "`` \\[\\` ``";
        var expected = "<p><code>\\[\\`</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0018()
    {
        var md = "    \\[\\]";
        var expected = "<pre><code>\\[\\]\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0019()
    {
        var md = "~~~\n\\[\\]\n~~~";
        var expected = "<pre><code>\\[\\]\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0020()
    {
        var md = "<https://example.com?find=\\*>";
        var expected = "<p><a href=\"https://example.com?find=%5C*\">https://example.com?find=\\*</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0021()
    {
        var md = "<a href=\"/bar\\/)\">";
        var expected = "<a href=\"/bar\\/)\">\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0022()
    {
        var md = "[foo](/bar\\* \"ti\\*tle\")";
        var expected = "<p><a href=\"/bar*\" title=\"ti*tle\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0023()
    {
        var md = "[foo]\n\n[foo]: /bar\\* \"ti\\*tle\"";
        var expected = "<p><a href=\"/bar*\" title=\"ti*tle\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0024()
    {
        var md = "``` foo\\+bar\nfoo\n```";
        var expected = "<pre><code class=\"language-foo+bar\">foo\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0025()
    {
        var md = "&nbsp; &amp; &copy; &AElig; &Dcaron;\n&frac34; &HilbertSpace; &DifferentialD;\n&ClockwiseContourIntegral; &ngE;";
        var expected = "<p>  &amp; © Æ Ď\n¾ ℋ ⅆ\n∲ ≧̸</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0026()
    {
        var md = "&#35; &#1234; &#992; &#0;";
        var expected = "<p># Ӓ Ϡ �</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0027()
    {
        var md = "&#X22; &#XD06; &#xcab;";
        var expected = "<p>&quot; ആ ಫ</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0028()
    {
        var md = "&nbsp &x; &#; &#x;\n&#87654321;\n&#abcdef0;\n&ThisIsNotDefined; &hi?;";
        var expected = "<p>&amp;nbsp &amp;x; &amp;#; &amp;#x;\n&amp;#87654321;\n&amp;#abcdef0;\n&amp;ThisIsNotDefined; &amp;hi?;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0029()
    {
        var md = "&copy";
        var expected = "<p>&amp;copy</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0030()
    {
        var md = "&MadeUpEntity;";
        var expected = "<p>&amp;MadeUpEntity;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0031()
    {
        var md = "<a href=\"&ouml;&ouml;.html\">";
        var expected = "<a href=\"&ouml;&ouml;.html\">\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0032()
    {
        var md = "[foo](/f&ouml;&ouml; \"f&ouml;&ouml;\")";
        var expected = "<p><a href=\"/f%C3%B6%C3%B6\" title=\"föö\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0033()
    {
        var md = "[foo]\n\n[foo]: /f&ouml;&ouml; \"f&ouml;&ouml;\"";
        var expected = "<p><a href=\"/f%C3%B6%C3%B6\" title=\"föö\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0034()
    {
        var md = "``` f&ouml;&ouml;\nfoo\n```";
        var expected = "<pre><code class=\"language-föö\">foo\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0035()
    {
        var md = "`f&ouml;&ouml;`";
        var expected = "<p><code>f&amp;ouml;&amp;ouml;</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0036()
    {
        var md = "    f&ouml;f&ouml;";
        var expected = "<pre><code>f&amp;ouml;f&amp;ouml;\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0037()
    {
        var md = "&#42;foo&#42;\n*foo*";
        var expected = "<p>*foo*\n<em>foo</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0038()
    {
        var md = "&#42; foo\n\n* foo";
        var expected = "<p>* foo</p>\n<ul>\n<li>foo</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0039()
    {
        var md = "foo&#10;&#10;bar";
        var expected = "<p>foo\n\nbar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0040()
    {
        var md = "&#9;foo";
        var expected = "<p>\tfoo</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0041()
    {
        var md = "[a](url &quot;tit&quot;)";
        var expected = "<p>[a](url &quot;tit&quot;)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0042()
    {
        var md = "- `one\n- two`";
        var expected = "<ul>\n<li>`one</li>\n<li>two`</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0043()
    {
        var md = "***\n---\n___";
        var expected = "<hr />\n<hr />\n<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0044()
    {
        var md = "+++";
        var expected = "<p>+++</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0045()
    {
        var md = "===";
        var expected = "<p>===</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0046()
    {
        var md = "--\n**\n__";
        var expected = "<p>--\n**\n__</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0047()
    {
        var md = " ***\n  ***\n   ***";
        var expected = "<hr />\n<hr />\n<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0048()
    {
        var md = "    ***";
        var expected = "<pre><code>***\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0049()
    {
        var md = "Foo\n    ***";
        var expected = "<p>Foo\n***</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0050()
    {
        var md = "_____________________________________";
        var expected = "<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0051()
    {
        var md = " - - -";
        var expected = "<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0052()
    {
        var md = " **  * ** * ** * **";
        var expected = "<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0053()
    {
        var md = "-     -      -      -";
        var expected = "<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0054()
    {
        var md = "- - - -    ";
        var expected = "<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0055()
    {
        var md = "_ _ _ _ a\n\na------\n\n---a---";
        var expected = "<p>_ _ _ _ a</p>\n<p>a------</p>\n<p>---a---</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0056()
    {
        var md = " *-*";
        var expected = "<p><em>-</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0057()
    {
        var md = "- foo\n***\n- bar";
        var expected = "<ul>\n<li>foo</li>\n</ul>\n<hr />\n<ul>\n<li>bar</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0058()
    {
        var md = "Foo\n***\nbar";
        var expected = "<p>Foo</p>\n<hr />\n<p>bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0059()
    {
        var md = "Foo\n---\nbar";
        var expected = "<h2>Foo</h2>\n<p>bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0060()
    {
        var md = "* Foo\n* * *\n* Bar";
        var expected = "<ul>\n<li>Foo</li>\n</ul>\n<hr />\n<ul>\n<li>Bar</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0061()
    {
        var md = "- Foo\n- * * *";
        var expected = "<ul>\n<li>Foo</li>\n<li>\n<hr />\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0062()
    {
        var md = "# foo\n## foo\n### foo\n#### foo\n##### foo\n###### foo";
        var expected = "<h1>foo</h1>\n<h2>foo</h2>\n<h3>foo</h3>\n<h4>foo</h4>\n<h5>foo</h5>\n<h6>foo</h6>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0063()
    {
        var md = "####### foo";
        var expected = "<p>####### foo</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0064()
    {
        var md = "#5 bolt\n\n#hashtag";
        var expected = "<p>#5 bolt</p>\n<p>#hashtag</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0065()
    {
        var md = "\\## foo";
        var expected = "<p>## foo</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0066()
    {
        var md = "# foo *bar* \\*baz\\*";
        var expected = "<h1>foo <em>bar</em> *baz*</h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0067()
    {
        var md = "#                  foo                     ";
        var expected = "<h1>foo</h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0068()
    {
        var md = " ### foo\n  ## foo\n   # foo";
        var expected = "<h3>foo</h3>\n<h2>foo</h2>\n<h1>foo</h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0069()
    {
        var md = "    # foo";
        var expected = "<pre><code># foo\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0070()
    {
        var md = "foo\n    # bar";
        var expected = "<p>foo\n# bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0071()
    {
        var md = "## foo ##\n  ###   bar    ###";
        var expected = "<h2>foo</h2>\n<h3>bar</h3>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0072()
    {
        var md = "# foo ##################################\n##### foo ##";
        var expected = "<h1>foo</h1>\n<h5>foo</h5>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0073()
    {
        var md = "### foo ###     ";
        var expected = "<h3>foo</h3>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0074()
    {
        var md = "### foo ### b";
        var expected = "<h3>foo ### b</h3>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0075()
    {
        var md = "# foo#";
        var expected = "<h1>foo#</h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0076()
    {
        var md = "### foo \\###\n## foo #\\##\n# foo \\#";
        var expected = "<h3>foo ###</h3>\n<h2>foo ###</h2>\n<h1>foo #</h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0077()
    {
        var md = "****\n## foo\n****";
        var expected = "<hr />\n<h2>foo</h2>\n<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0078()
    {
        var md = "Foo bar\n# baz\nBar foo";
        var expected = "<p>Foo bar</p>\n<h1>baz</h1>\n<p>Bar foo</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0079()
    {
        var md = "## \n#\n### ###";
        var expected = "<h2></h2>\n<h1></h1>\n<h3></h3>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0080()
    {
        var md = "Foo *bar*\n=========\n\nFoo *bar*\n---------";
        var expected = "<h1>Foo <em>bar</em></h1>\n<h2>Foo <em>bar</em></h2>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0081()
    {
        var md = "Foo *bar\nbaz*\n====";
        var expected = "<h1>Foo <em>bar\nbaz</em></h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0082()
    {
        var md = "  Foo *bar\nbaz*\t\n====";
        var expected = "<h1>Foo <em>bar\nbaz</em></h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0083()
    {
        var md = "Foo\n-------------------------\n\nFoo\n=";
        var expected = "<h2>Foo</h2>\n<h1>Foo</h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0084()
    {
        var md = "   Foo\n---\n\n  Foo\n-----\n\n  Foo\n  ===";
        var expected = "<h2>Foo</h2>\n<h2>Foo</h2>\n<h1>Foo</h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0085()
    {
        var md = "    Foo\n    ---\n\n    Foo\n---";
        var expected = "<pre><code>Foo\n---\n\nFoo\n</code></pre>\n<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0086()
    {
        var md = "Foo\n   ----      ";
        var expected = "<h2>Foo</h2>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0087()
    {
        var md = "Foo\n    ---";
        var expected = "<p>Foo\n---</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0088()
    {
        var md = "Foo\n= =\n\nFoo\n--- -";
        var expected = "<p>Foo\n= =</p>\n<p>Foo</p>\n<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0089()
    {
        var md = "Foo  \n-----";
        var expected = "<h2>Foo</h2>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0090()
    {
        var md = "Foo\\\n----";
        var expected = "<h2>Foo\\</h2>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0091()
    {
        var md = "`Foo\n----\n`\n\n<a title=\"a lot\n---\nof dashes\"/>";
        var expected = "<h2>`Foo</h2>\n<p>`</p>\n<h2>&lt;a title=&quot;a lot</h2>\n<p>of dashes&quot;/&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0092()
    {
        var md = "> Foo\n---";
        var expected = "<blockquote>\n<p>Foo</p>\n</blockquote>\n<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0093()
    {
        var md = "> foo\nbar\n===";
        var expected = "<blockquote>\n<p>foo\nbar\n===</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0094()
    {
        var md = "- Foo\n---";
        var expected = "<ul>\n<li>Foo</li>\n</ul>\n<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0095()
    {
        var md = "Foo\nBar\n---";
        var expected = "<h2>Foo\nBar</h2>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0096()
    {
        var md = "---\nFoo\n---\nBar\n---\nBaz";
        var expected = "<hr />\n<h2>Foo</h2>\n<h2>Bar</h2>\n<p>Baz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0097()
    {
        var md = "\n====";
        var expected = "<p>====</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0098()
    {
        var md = "---\n---";
        var expected = "<hr />\n<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0099()
    {
        var md = "- foo\n-----";
        var expected = "<ul>\n<li>foo</li>\n</ul>\n<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0100()
    {
        var md = "    foo\n---";
        var expected = "<pre><code>foo\n</code></pre>\n<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0101()
    {
        var md = "> foo\n-----";
        var expected = "<blockquote>\n<p>foo</p>\n</blockquote>\n<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0102()
    {
        var md = "\\> foo\n------";
        var expected = "<h2>&gt; foo</h2>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0103()
    {
        var md = "Foo\n\nbar\n---\nbaz";
        var expected = "<p>Foo</p>\n<h2>bar</h2>\n<p>baz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0104()
    {
        var md = "Foo\nbar\n\n---\n\nbaz";
        var expected = "<p>Foo\nbar</p>\n<hr />\n<p>baz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0105()
    {
        var md = "Foo\nbar\n* * *\nbaz";
        var expected = "<p>Foo\nbar</p>\n<hr />\n<p>baz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0106()
    {
        var md = "Foo\nbar\n\\---\nbaz";
        var expected = "<p>Foo\nbar\n---\nbaz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0107()
    {
        var md = "    a simple\n      indented code block";
        var expected = "<pre><code>a simple\n  indented code block\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0108()
    {
        var md = "  - foo\n\n    bar";
        var expected = "<ul>\n<li>\n<p>foo</p>\n<p>bar</p>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0109()
    {
        var md = "1.  foo\n\n    - bar";
        var expected = "<ol>\n<li>\n<p>foo</p>\n<ul>\n<li>bar</li>\n</ul>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0110()
    {
        var md = "    <a/>\n    *hi*\n\n    - one";
        var expected = "<pre><code>&lt;a/&gt;\n*hi*\n\n- one\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0111()
    {
        var md = "    chunk1\n\n    chunk2\n  \n \n \n    chunk3";
        var expected = "<pre><code>chunk1\n\nchunk2\n\n\n\nchunk3\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0112()
    {
        var md = "    chunk1\n      \n      chunk2";
        var expected = "<pre><code>chunk1\n  \n  chunk2\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0113()
    {
        var md = "Foo\n    bar\n";
        var expected = "<p>Foo\nbar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0114()
    {
        var md = "    foo\nbar";
        var expected = "<pre><code>foo\n</code></pre>\n<p>bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0115()
    {
        var md = "# Heading\n    foo\nHeading\n------\n    foo\n----";
        var expected = "<h1>Heading</h1>\n<pre><code>foo\n</code></pre>\n<h2>Heading</h2>\n<pre><code>foo\n</code></pre>\n<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0116()
    {
        var md = "        foo\n    bar";
        var expected = "<pre><code>    foo\nbar\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0117()
    {
        var md = "\n    \n    foo\n    \n";
        var expected = "<pre><code>foo\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0118()
    {
        var md = "    foo  ";
        var expected = "<pre><code>foo  \n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0119()
    {
        var md = "```\n<\n >\n```";
        var expected = "<pre><code>&lt;\n &gt;\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0120()
    {
        var md = "~~~\n<\n >\n~~~";
        var expected = "<pre><code>&lt;\n &gt;\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0121()
    {
        var md = "``\nfoo\n``";
        var expected = "<p><code>foo</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0122()
    {
        var md = "```\naaa\n~~~\n```";
        var expected = "<pre><code>aaa\n~~~\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0123()
    {
        var md = "~~~\naaa\n```\n~~~";
        var expected = "<pre><code>aaa\n```\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0124()
    {
        var md = "````\naaa\n```\n``````";
        var expected = "<pre><code>aaa\n```\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0125()
    {
        var md = "~~~~\naaa\n~~~\n~~~~";
        var expected = "<pre><code>aaa\n~~~\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0126()
    {
        var md = "```";
        var expected = "<pre><code></code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0127()
    {
        var md = "`````\n\n```\naaa";
        var expected = "<pre><code>\n```\naaa\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0128()
    {
        var md = "> ```\n> aaa\n\nbbb";
        var expected = "<blockquote>\n<pre><code>aaa\n</code></pre>\n</blockquote>\n<p>bbb</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0129()
    {
        var md = "```\n\n  \n```";
        var expected = "<pre><code>\n  \n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0130()
    {
        var md = "```\n```";
        var expected = "<pre><code></code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0131()
    {
        var md = " ```\n aaa\naaa\n```";
        var expected = "<pre><code>aaa\naaa\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0132()
    {
        var md = "  ```\naaa\n  aaa\naaa\n  ```";
        var expected = "<pre><code>aaa\naaa\naaa\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0133()
    {
        var md = "   ```\n   aaa\n    aaa\n  aaa\n   ```";
        var expected = "<pre><code>aaa\n aaa\naaa\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0134()
    {
        var md = "    ```\n    aaa\n    ```";
        var expected = "<pre><code>```\naaa\n```\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0135()
    {
        var md = "```\naaa\n  ```";
        var expected = "<pre><code>aaa\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0136()
    {
        var md = "   ```\naaa\n  ```";
        var expected = "<pre><code>aaa\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0137()
    {
        var md = "```\naaa\n    ```";
        var expected = "<pre><code>aaa\n    ```\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0138()
    {
        var md = "``` ```\naaa";
        var expected = "<p><code> </code>\naaa</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0139()
    {
        var md = "~~~~~~\naaa\n~~~ ~~";
        var expected = "<pre><code>aaa\n~~~ ~~\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0140()
    {
        var md = "foo\n```\nbar\n```\nbaz";
        var expected = "<p>foo</p>\n<pre><code>bar\n</code></pre>\n<p>baz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0141()
    {
        var md = "foo\n---\n~~~\nbar\n~~~\n# baz";
        var expected = "<h2>foo</h2>\n<pre><code>bar\n</code></pre>\n<h1>baz</h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0142()
    {
        var md = "```ruby\ndef foo(x)\n  return 3\nend\n```";
        var expected = "<pre><code class=\"language-ruby\">def foo(x)\n  return 3\nend\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0143()
    {
        var md = "~~~~    ruby startline=3 $%@#$\ndef foo(x)\n  return 3\nend\n~~~~~~~";
        var expected = "<pre><code class=\"language-ruby\">def foo(x)\n  return 3\nend\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0144()
    {
        var md = "````;\n````";
        var expected = "<pre><code class=\"language-;\"></code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0145()
    {
        var md = "``` aa ```\nfoo";
        var expected = "<p><code>aa</code>\nfoo</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0146()
    {
        var md = "~~~ aa ``` ~~~\nfoo\n~~~";
        var expected = "<pre><code class=\"language-aa\">foo\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0147()
    {
        var md = "```\n``` aaa\n```";
        var expected = "<pre><code>``` aaa\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0148()
    {
        var md = "<table><tr><td>\n<pre>\n**Hello**,\n\n_world_.\n</pre>\n</td></tr></table>";
        var expected = "<table><tr><td>\n<pre>\n**Hello**,\n<p><em>world</em>.\n</pre></p>\n</td></tr></table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0149()
    {
        var md = "<table>\n  <tr>\n    <td>\n           hi\n    </td>\n  </tr>\n</table>\n\nokay.";
        var expected = "<table>\n  <tr>\n    <td>\n           hi\n    </td>\n  </tr>\n</table>\n<p>okay.</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0150()
    {
        var md = " <div>\n  *hello*\n         <foo><a>";
        var expected = " <div>\n  *hello*\n         <foo><a>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0151()
    {
        var md = "</div>\n*foo*";
        var expected = "</div>\n*foo*\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0152()
    {
        var md = "<DIV CLASS=\"foo\">\n\n*Markdown*\n\n</DIV>";
        var expected = "<DIV CLASS=\"foo\">\n<p><em>Markdown</em></p>\n</DIV>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0153()
    {
        var md = "<div id=\"foo\"\n  class=\"bar\">\n</div>";
        var expected = "<div id=\"foo\"\n  class=\"bar\">\n</div>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0154()
    {
        var md = "<div id=\"foo\" class=\"bar\n  baz\">\n</div>";
        var expected = "<div id=\"foo\" class=\"bar\n  baz\">\n</div>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0155()
    {
        var md = "<div>\n*foo*\n\n*bar*";
        var expected = "<div>\n*foo*\n<p><em>bar</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0156()
    {
        var md = "<div id=\"foo\"\n*hi*";
        var expected = "<div id=\"foo\"\n*hi*\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0157()
    {
        var md = "<div class\nfoo";
        var expected = "<div class\nfoo\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0158()
    {
        var md = "<div *???-&&&-<---\n*foo*";
        var expected = "<div *???-&&&-<---\n*foo*\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0159()
    {
        var md = "<div><a href=\"bar\">*foo*</a></div>";
        var expected = "<div><a href=\"bar\">*foo*</a></div>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0160()
    {
        var md = "<table><tr><td>\nfoo\n</td></tr></table>";
        var expected = "<table><tr><td>\nfoo\n</td></tr></table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0161()
    {
        var md = "<div></div>\n``` c\nint x = 33;\n```";
        var expected = "<div></div>\n``` c\nint x = 33;\n```\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0162()
    {
        var md = "<a href=\"foo\">\n*bar*\n</a>";
        var expected = "<a href=\"foo\">\n*bar*\n</a>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0163()
    {
        var md = "<Warning>\n*bar*\n</Warning>";
        var expected = "<Warning>\n*bar*\n</Warning>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0164()
    {
        var md = "<i class=\"foo\">\n*bar*\n</i>";
        var expected = "<i class=\"foo\">\n*bar*\n</i>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0165()
    {
        var md = "</ins>\n*bar*";
        var expected = "</ins>\n*bar*\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0166()
    {
        var md = "<del>\n*foo*\n</del>";
        var expected = "<del>\n*foo*\n</del>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0167()
    {
        var md = "<del>\n\n*foo*\n\n</del>";
        var expected = "<del>\n<p><em>foo</em></p>\n</del>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0168()
    {
        var md = "<del>*foo*</del>";
        var expected = "<p><del><em>foo</em></del></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0169()
    {
        var md = "<pre language=\"haskell\"><code>\nimport Text.HTML.TagSoup\n\nmain :: IO ()\nmain = print $ parseTags tags\n</code></pre>\nokay";
        var expected = "<pre language=\"haskell\"><code>\nimport Text.HTML.TagSoup\n\nmain :: IO ()\nmain = print $ parseTags tags\n</code></pre>\n<p>okay</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0170()
    {
        var md = "<script type=\"text/javascript\">\n// JavaScript example\n\ndocument.getElementById(\"demo\").innerHTML = \"Hello JavaScript!\";\n</script>\nokay";
        var expected = "<script type=\"text/javascript\">\n// JavaScript example\n\ndocument.getElementById(\"demo\").innerHTML = \"Hello JavaScript!\";\n</script>\n<p>okay</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0171()
    {
        var md = "<textarea>\n\n*foo*\n\n_bar_\n\n</textarea>";
        var expected = "<textarea>\n\n*foo*\n\n_bar_\n\n</textarea>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0172()
    {
        var md = "<style\n  type=\"text/css\">\nh1 {color:red;}\n\np {color:blue;}\n</style>\nokay";
        var expected = "<style\n  type=\"text/css\">\nh1 {color:red;}\n\np {color:blue;}\n</style>\n<p>okay</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0173()
    {
        var md = "<style\n  type=\"text/css\">\n\nfoo";
        var expected = "<style\n  type=\"text/css\">\n\nfoo\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0174()
    {
        var md = "> <div>\n> foo\n\nbar";
        var expected = "<blockquote>\n<div>\nfoo\n</blockquote>\n<p>bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0175()
    {
        var md = "- <div>\n- foo";
        var expected = "<ul>\n<li>\n<div>\n</li>\n<li>foo</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0176()
    {
        var md = "<style>p{color:red;}</style>\n*foo*";
        var expected = "<style>p{color:red;}</style>\n<p><em>foo</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0177()
    {
        var md = "<!-- foo -->*bar*\n*baz*";
        var expected = "<!-- foo -->*bar*\n<p><em>baz</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0178()
    {
        var md = "<script>\nfoo\n</script>1. *bar*";
        var expected = "<script>\nfoo\n</script>1. *bar*\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0179()
    {
        var md = "<!-- Foo\n\nbar\n   baz -->\nokay";
        var expected = "<!-- Foo\n\nbar\n   baz -->\n<p>okay</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0180()
    {
        var md = "<?php\n\n  echo '>';\n\n?>\nokay";
        var expected = "<?php\n\n  echo '>';\n\n?>\n<p>okay</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0181()
    {
        var md = "<!DOCTYPE html>";
        var expected = "<!DOCTYPE html>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0182()
    {
        var md = "<![CDATA[\nfunction matchwo(a,b)\n{\n  if (a < b && a < 0) then {\n    return 1;\n\n  } else {\n\n    return 0;\n  }\n}\n]]>\nokay";
        var expected = "<![CDATA[\nfunction matchwo(a,b)\n{\n  if (a < b && a < 0) then {\n    return 1;\n\n  } else {\n\n    return 0;\n  }\n}\n]]>\n<p>okay</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0183()
    {
        var md = "  <!-- foo -->\n\n    <!-- foo -->";
        var expected = "  <!-- foo -->\n<pre><code>&lt;!-- foo --&gt;\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0184()
    {
        var md = "  <div>\n\n    <div>";
        var expected = "  <div>\n<pre><code>&lt;div&gt;\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0185()
    {
        var md = "Foo\n<div>\nbar\n</div>";
        var expected = "<p>Foo</p>\n<div>\nbar\n</div>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0186()
    {
        var md = "<div>\nbar\n</div>\n*foo*";
        var expected = "<div>\nbar\n</div>\n*foo*\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0187()
    {
        var md = "Foo\n<a href=\"bar\">\nbaz";
        var expected = "<p>Foo\n<a href=\"bar\">\nbaz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0188()
    {
        var md = "<div>\n\n*Emphasized* text.\n\n</div>";
        var expected = "<div>\n<p><em>Emphasized</em> text.</p>\n</div>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0189()
    {
        var md = "<div>\n*Emphasized* text.\n</div>";
        var expected = "<div>\n*Emphasized* text.\n</div>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0190()
    {
        var md = "<table>\n\n<tr>\n\n<td>\nHi\n</td>\n\n</tr>\n\n</table>";
        var expected = "<table>\n<tr>\n<td>\nHi\n</td>\n</tr>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0191()
    {
        var md = "<table>\n\n  <tr>\n\n    <td>\n      Hi\n    </td>\n\n  </tr>\n\n</table>";
        var expected = "<table>\n  <tr>\n<pre><code>&lt;td&gt;\n  Hi\n&lt;/td&gt;\n</code></pre>\n  </tr>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0192()
    {
        var md = "[foo]: /url \"title\"\n\n[foo]";
        var expected = "<p><a href=\"/url\" title=\"title\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0193()
    {
        var md = "   [foo]: \n      /url  \n           'the title'  \n\n[foo]";
        var expected = "<p><a href=\"/url\" title=\"the title\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0194()
    {
        var md = "[Foo*bar\\]]:my_(url) 'title (with parens)'\n\n[Foo*bar\\]]";
        var expected = "<p><a href=\"my_(url)\" title=\"title (with parens)\">Foo*bar]</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0195()
    {
        var md = "[Foo bar]:\n<my url>\n'title'\n\n[Foo bar]";
        var expected = "<p><a href=\"my%20url\" title=\"title\">Foo bar</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0196()
    {
        var md = "[foo]: /url '\ntitle\nline1\nline2\n'\n\n[foo]";
        var expected = "<p><a href=\"/url\" title=\"\ntitle\nline1\nline2\n\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0197()
    {
        var md = "[foo]: /url 'title\n\nwith blank line'\n\n[foo]";
        var expected = "<p>[foo]: /url 'title</p>\n<p>with blank line'</p>\n<p>[foo]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0198()
    {
        var md = "[foo]:\n/url\n\n[foo]";
        var expected = "<p><a href=\"/url\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0199()
    {
        var md = "[foo]:\n\n[foo]";
        var expected = "<p>[foo]:</p>\n<p>[foo]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0200()
    {
        var md = "[foo]: <>\n\n[foo]";
        var expected = "<p><a href=\"\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0201()
    {
        var md = "[foo]: <bar>(baz)\n\n[foo]";
        var expected = "<p>[foo]: <bar>(baz)</p>\n<p>[foo]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0202()
    {
        var md = "[foo]: /url\\bar\\*baz \"foo\\\"bar\\baz\"\n\n[foo]";
        var expected = "<p><a href=\"/url%5Cbar*baz\" title=\"foo&quot;bar\\baz\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0203()
    {
        var md = "[foo]\n\n[foo]: url";
        var expected = "<p><a href=\"url\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0204()
    {
        var md = "[foo]\n\n[foo]: first\n[foo]: second";
        var expected = "<p><a href=\"first\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0205()
    {
        var md = "[FOO]: /url\n\n[Foo]";
        var expected = "<p><a href=\"/url\">Foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0206()
    {
        var md = "[ΑΓΩ]: /φου\n\n[αγω]";
        var expected = "<p><a href=\"/%CF%86%CE%BF%CF%85\">αγω</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0207()
    {
        var md = "[\nfoo\n]: /url\nbar";
        var expected = "<p>bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0208()
    {
        var md = "[foo]: /url \"title\" ok";
        var expected = "<p>[foo]: /url &quot;title&quot; ok</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0209()
    {
        var md = "[foo]: /url\n\"title\" ok";
        var expected = "<p>&quot;title&quot; ok</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0210()
    {
        var md = "    [foo]: /url \"title\"\n\n[foo]";
        var expected = "<pre><code>[foo]: /url &quot;title&quot;\n</code></pre>\n<p>[foo]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0211()
    {
        var md = "```\n[foo]: /url\n```\n\n[foo]";
        var expected = "<pre><code>[foo]: /url\n</code></pre>\n<p>[foo]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0212()
    {
        var md = "Foo\n[bar]: /baz\n\n[bar]";
        var expected = "<p>Foo\n[bar]: /baz</p>\n<p>[bar]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0213()
    {
        var md = "# [Foo]\n[foo]: /url\n> bar";
        var expected = "<h1><a href=\"/url\">Foo</a></h1>\n<blockquote>\n<p>bar</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0214()
    {
        var md = "[foo]: /url\nbar\n===\n[foo]";
        var expected = "<h1>bar</h1>\n<p><a href=\"/url\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0215()
    {
        var md = "[foo]: /url\n===\n[foo]";
        var expected = "<p>===\n<a href=\"/url\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0216()
    {
        var md = "[foo]: /foo-url \"foo\"\n[bar]: /bar-url\n  \"bar\"\n[baz]: /baz-url\n\n[foo],\n[bar],\n[baz]";
        var expected = "<p><a href=\"/foo-url\" title=\"foo\">foo</a>,\n<a href=\"/bar-url\" title=\"bar\">bar</a>,\n<a href=\"/baz-url\">baz</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0217()
    {
        var md = "[foo]\n\n> [foo]: /url";
        var expected = "<p><a href=\"/url\">foo</a></p>\n<blockquote>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0218()
    {
        var md = "aaa\n\nbbb";
        var expected = "<p>aaa</p>\n<p>bbb</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0219()
    {
        var md = "aaa\nbbb\n\nccc\nddd";
        var expected = "<p>aaa\nbbb</p>\n<p>ccc\nddd</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0220()
    {
        var md = "aaa\n\n\nbbb";
        var expected = "<p>aaa</p>\n<p>bbb</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0221()
    {
        var md = "  aaa\n bbb";
        var expected = "<p>aaa\nbbb</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0222()
    {
        var md = "aaa\n             bbb\n                                       ccc";
        var expected = "<p>aaa\nbbb\nccc</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0223()
    {
        var md = "   aaa\nbbb";
        var expected = "<p>aaa\nbbb</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0224()
    {
        var md = "    aaa\nbbb";
        var expected = "<pre><code>aaa\n</code></pre>\n<p>bbb</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0225()
    {
        var md = "aaa     \nbbb     ";
        var expected = "<p>aaa<br />\nbbb</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0226()
    {
        var md = "  \n\naaa\n  \n\n# aaa\n\n  ";
        var expected = "<p>aaa</p>\n<h1>aaa</h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0227()
    {
        var md = "> # Foo\n> bar\n> baz";
        var expected = "<blockquote>\n<h1>Foo</h1>\n<p>bar\nbaz</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0228()
    {
        var md = "># Foo\n>bar\n> baz";
        var expected = "<blockquote>\n<h1>Foo</h1>\n<p>bar\nbaz</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0229()
    {
        var md = "   > # Foo\n   > bar\n > baz";
        var expected = "<blockquote>\n<h1>Foo</h1>\n<p>bar\nbaz</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0230()
    {
        var md = "    > # Foo\n    > bar\n    > baz";
        var expected = "<pre><code>&gt; # Foo\n&gt; bar\n&gt; baz\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0231()
    {
        var md = "> # Foo\n> bar\nbaz";
        var expected = "<blockquote>\n<h1>Foo</h1>\n<p>bar\nbaz</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0232()
    {
        var md = "> bar\nbaz\n> foo";
        var expected = "<blockquote>\n<p>bar\nbaz\nfoo</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0233()
    {
        var md = "> foo\n---";
        var expected = "<blockquote>\n<p>foo</p>\n</blockquote>\n<hr />\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0234()
    {
        var md = "> - foo\n- bar";
        var expected = "<blockquote>\n<ul>\n<li>foo</li>\n</ul>\n</blockquote>\n<ul>\n<li>bar</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0235()
    {
        var md = ">     foo\n    bar";
        var expected = "<blockquote>\n<pre><code>foo\n</code></pre>\n</blockquote>\n<pre><code>bar\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0236()
    {
        var md = "> ```\nfoo\n```";
        var expected = "<blockquote>\n<pre><code></code></pre>\n</blockquote>\n<p>foo</p>\n<pre><code></code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0237()
    {
        var md = "> foo\n    - bar";
        var expected = "<blockquote>\n<p>foo\n- bar</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0238()
    {
        var md = ">";
        var expected = "<blockquote>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0239()
    {
        var md = ">\n>  \n> ";
        var expected = "<blockquote>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0240()
    {
        var md = ">\n> foo\n>  ";
        var expected = "<blockquote>\n<p>foo</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0241()
    {
        var md = "> foo\n\n> bar";
        var expected = "<blockquote>\n<p>foo</p>\n</blockquote>\n<blockquote>\n<p>bar</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0242()
    {
        var md = "> foo\n> bar";
        var expected = "<blockquote>\n<p>foo\nbar</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0243()
    {
        var md = "> foo\n>\n> bar";
        var expected = "<blockquote>\n<p>foo</p>\n<p>bar</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0244()
    {
        var md = "foo\n> bar";
        var expected = "<p>foo</p>\n<blockquote>\n<p>bar</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0245()
    {
        var md = "> aaa\n***\n> bbb";
        var expected = "<blockquote>\n<p>aaa</p>\n</blockquote>\n<hr />\n<blockquote>\n<p>bbb</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0246()
    {
        var md = "> bar\nbaz";
        var expected = "<blockquote>\n<p>bar\nbaz</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0247()
    {
        var md = "> bar\n\nbaz";
        var expected = "<blockquote>\n<p>bar</p>\n</blockquote>\n<p>baz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0248()
    {
        var md = "> bar\n>\nbaz";
        var expected = "<blockquote>\n<p>bar</p>\n</blockquote>\n<p>baz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0249()
    {
        var md = "> > > foo\nbar";
        var expected = "<blockquote>\n<blockquote>\n<blockquote>\n<p>foo\nbar</p>\n</blockquote>\n</blockquote>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0250()
    {
        var md = ">>> foo\n> bar\n>>baz";
        var expected = "<blockquote>\n<blockquote>\n<blockquote>\n<p>foo\nbar\nbaz</p>\n</blockquote>\n</blockquote>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0251()
    {
        var md = ">     code\n\n>    not code";
        var expected = "<blockquote>\n<pre><code>code\n</code></pre>\n</blockquote>\n<blockquote>\n<p>not code</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0252()
    {
        var md = "A paragraph\nwith two lines.\n\n    indented code\n\n> A block quote.";
        var expected = "<p>A paragraph\nwith two lines.</p>\n<pre><code>indented code\n</code></pre>\n<blockquote>\n<p>A block quote.</p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0253()
    {
        var md = "1.  A paragraph\n    with two lines.\n\n        indented code\n\n    > A block quote.";
        var expected = "<ol>\n<li>\n<p>A paragraph\nwith two lines.</p>\n<pre><code>indented code\n</code></pre>\n<blockquote>\n<p>A block quote.</p>\n</blockquote>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0254()
    {
        var md = "- one\n\n two";
        var expected = "<ul>\n<li>one</li>\n</ul>\n<p>two</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0255()
    {
        var md = "- one\n\n  two";
        var expected = "<ul>\n<li>\n<p>one</p>\n<p>two</p>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0256()
    {
        var md = " -    one\n\n     two";
        var expected = "<ul>\n<li>one</li>\n</ul>\n<pre><code> two\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0257()
    {
        var md = " -    one\n\n      two";
        var expected = "<ul>\n<li>\n<p>one</p>\n<p>two</p>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0258()
    {
        var md = "   > > 1.  one\n>>\n>>     two";
        var expected = "<blockquote>\n<blockquote>\n<ol>\n<li>\n<p>one</p>\n<p>two</p>\n</li>\n</ol>\n</blockquote>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0259()
    {
        var md = ">>- one\n>>\n  >  > two";
        var expected = "<blockquote>\n<blockquote>\n<ul>\n<li>one</li>\n</ul>\n<p>two</p>\n</blockquote>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0260()
    {
        var md = "-one\n\n2.two";
        var expected = "<p>-one</p>\n<p>2.two</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0261()
    {
        var md = "- foo\n\n\n  bar";
        var expected = "<ul>\n<li>\n<p>foo</p>\n<p>bar</p>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0262()
    {
        var md = "1.  foo\n\n    ```\n    bar\n    ```\n\n    baz\n\n    > bam";
        var expected = "<ol>\n<li>\n<p>foo</p>\n<pre><code>bar\n</code></pre>\n<p>baz</p>\n<blockquote>\n<p>bam</p>\n</blockquote>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0263()
    {
        var md = "- Foo\n\n      bar\n\n\n      baz";
        var expected = "<ul>\n<li>\n<p>Foo</p>\n<pre><code>bar\n\n\nbaz\n</code></pre>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0264()
    {
        var md = "123456789. ok";
        var expected = "<ol start=\"123456789\">\n<li>ok</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0265()
    {
        var md = "1234567890. not ok";
        var expected = "<p>1234567890. not ok</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0266()
    {
        var md = "0. ok";
        var expected = "<ol start=\"0\">\n<li>ok</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0267()
    {
        var md = "003. ok";
        var expected = "<ol start=\"3\">\n<li>ok</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0268()
    {
        var md = "-1. not ok";
        var expected = "<p>-1. not ok</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0269()
    {
        var md = "- foo\n\n      bar";
        var expected = "<ul>\n<li>\n<p>foo</p>\n<pre><code>bar\n</code></pre>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0270()
    {
        var md = "  10.  foo\n\n           bar";
        var expected = "<ol start=\"10\">\n<li>\n<p>foo</p>\n<pre><code>bar\n</code></pre>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0271()
    {
        var md = "    indented code\n\nparagraph\n\n    more code";
        var expected = "<pre><code>indented code\n</code></pre>\n<p>paragraph</p>\n<pre><code>more code\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0272()
    {
        var md = "1.     indented code\n\n   paragraph\n\n       more code";
        var expected = "<ol>\n<li>\n<pre><code>indented code\n</code></pre>\n<p>paragraph</p>\n<pre><code>more code\n</code></pre>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0273()
    {
        var md = "1.      indented code\n\n   paragraph\n\n       more code";
        var expected = "<ol>\n<li>\n<pre><code> indented code\n</code></pre>\n<p>paragraph</p>\n<pre><code>more code\n</code></pre>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0274()
    {
        var md = "   foo\n\nbar";
        var expected = "<p>foo</p>\n<p>bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0275()
    {
        var md = "-    foo\n\n  bar";
        var expected = "<ul>\n<li>foo</li>\n</ul>\n<p>bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0276()
    {
        var md = "-  foo\n\n   bar";
        var expected = "<ul>\n<li>\n<p>foo</p>\n<p>bar</p>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0277()
    {
        var md = "-\n  foo\n-\n  ```\n  bar\n  ```\n-\n      baz";
        var expected = "<ul>\n<li>foo</li>\n<li>\n<pre><code>bar\n</code></pre>\n</li>\n<li>\n<pre><code>baz\n</code></pre>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0278()
    {
        var md = "-   \n  foo";
        var expected = "<ul>\n<li>foo</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0279()
    {
        var md = "-\n\n  foo";
        var expected = "<ul>\n<li></li>\n</ul>\n<p>foo</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0280()
    {
        var md = "- foo\n-\n- bar";
        var expected = "<ul>\n<li>foo</li>\n<li></li>\n<li>bar</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0281()
    {
        var md = "- foo\n-   \n- bar";
        var expected = "<ul>\n<li>foo</li>\n<li></li>\n<li>bar</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0282()
    {
        var md = "1. foo\n2.\n3. bar";
        var expected = "<ol>\n<li>foo</li>\n<li></li>\n<li>bar</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0283()
    {
        var md = "*";
        var expected = "<ul>\n<li></li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0284()
    {
        var md = "foo\n*\n\nfoo\n1.";
        var expected = "<p>foo\n*</p>\n<p>foo\n1.</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0285()
    {
        var md = " 1.  A paragraph\n     with two lines.\n\n         indented code\n\n     > A block quote.";
        var expected = "<ol>\n<li>\n<p>A paragraph\nwith two lines.</p>\n<pre><code>indented code\n</code></pre>\n<blockquote>\n<p>A block quote.</p>\n</blockquote>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0286()
    {
        var md = "  1.  A paragraph\n      with two lines.\n\n          indented code\n\n      > A block quote.";
        var expected = "<ol>\n<li>\n<p>A paragraph\nwith two lines.</p>\n<pre><code>indented code\n</code></pre>\n<blockquote>\n<p>A block quote.</p>\n</blockquote>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0287()
    {
        var md = "   1.  A paragraph\n       with two lines.\n\n           indented code\n\n       > A block quote.";
        var expected = "<ol>\n<li>\n<p>A paragraph\nwith two lines.</p>\n<pre><code>indented code\n</code></pre>\n<blockquote>\n<p>A block quote.</p>\n</blockquote>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0288()
    {
        var md = "    1.  A paragraph\n        with two lines.\n\n            indented code\n\n        > A block quote.";
        var expected = "<pre><code>1.  A paragraph\n    with two lines.\n\n        indented code\n\n    &gt; A block quote.\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0289()
    {
        var md = "  1.  A paragraph\nwith two lines.\n\n          indented code\n\n      > A block quote.";
        var expected = "<ol>\n<li>\n<p>A paragraph\nwith two lines.</p>\n<pre><code>indented code\n</code></pre>\n<blockquote>\n<p>A block quote.</p>\n</blockquote>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0290()
    {
        var md = "  1.  A paragraph\n    with two lines.";
        var expected = "<ol>\n<li>A paragraph\nwith two lines.</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0291()
    {
        var md = "> 1. > Blockquote\ncontinued here.";
        var expected = "<blockquote>\n<ol>\n<li>\n<blockquote>\n<p>Blockquote\ncontinued here.</p>\n</blockquote>\n</li>\n</ol>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0292()
    {
        var md = "> 1. > Blockquote\n> continued here.";
        var expected = "<blockquote>\n<ol>\n<li>\n<blockquote>\n<p>Blockquote\ncontinued here.</p>\n</blockquote>\n</li>\n</ol>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0293()
    {
        var md = "- foo\n  - bar\n    - baz\n      - boo";
        var expected = "<ul>\n<li>foo\n<ul>\n<li>bar\n<ul>\n<li>baz\n<ul>\n<li>boo</li>\n</ul>\n</li>\n</ul>\n</li>\n</ul>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0294()
    {
        var md = "- foo\n - bar\n  - baz\n   - boo";
        var expected = "<ul>\n<li>foo</li>\n<li>bar</li>\n<li>baz</li>\n<li>boo</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0295()
    {
        var md = "10) foo\n    - bar";
        var expected = "<ol start=\"10\">\n<li>foo\n<ul>\n<li>bar</li>\n</ul>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0296()
    {
        var md = "10) foo\n   - bar";
        var expected = "<ol start=\"10\">\n<li>foo</li>\n</ol>\n<ul>\n<li>bar</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0297()
    {
        var md = "- - foo";
        var expected = "<ul>\n<li>\n<ul>\n<li>foo</li>\n</ul>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0298()
    {
        var md = "1. - 2. foo";
        var expected = "<ol>\n<li>\n<ul>\n<li>\n<ol start=\"2\">\n<li>foo</li>\n</ol>\n</li>\n</ul>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0299()
    {
        var md = "- # Foo\n- Bar\n  ---\n  baz";
        var expected = "<ul>\n<li>\n<h1>Foo</h1>\n</li>\n<li>\n<h2>Bar</h2>\nbaz</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0300()
    {
        var md = "- foo\n- bar\n+ baz";
        var expected = "<ul>\n<li>foo</li>\n<li>bar</li>\n</ul>\n<ul>\n<li>baz</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0301()
    {
        var md = "1. foo\n2. bar\n3) baz";
        var expected = "<ol>\n<li>foo</li>\n<li>bar</li>\n</ol>\n<ol start=\"3\">\n<li>baz</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0302()
    {
        var md = "Foo\n- bar\n- baz";
        var expected = "<p>Foo</p>\n<ul>\n<li>bar</li>\n<li>baz</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0303()
    {
        var md = "The number of windows in my house is\n14.  The number of doors is 6.";
        var expected = "<p>The number of windows in my house is\n14.  The number of doors is 6.</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0304()
    {
        var md = "The number of windows in my house is\n1.  The number of doors is 6.";
        var expected = "<p>The number of windows in my house is</p>\n<ol>\n<li>The number of doors is 6.</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0305()
    {
        var md = "- foo\n\n- bar\n\n\n- baz";
        var expected = "<ul>\n<li>\n<p>foo</p>\n</li>\n<li>\n<p>bar</p>\n</li>\n<li>\n<p>baz</p>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0306()
    {
        var md = "- foo\n  - bar\n    - baz\n\n\n      bim";
        var expected = "<ul>\n<li>foo\n<ul>\n<li>bar\n<ul>\n<li>\n<p>baz</p>\n<p>bim</p>\n</li>\n</ul>\n</li>\n</ul>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0307()
    {
        var md = "- foo\n- bar\n\n<!-- -->\n\n- baz\n- bim";
        var expected = "<ul>\n<li>foo</li>\n<li>bar</li>\n</ul>\n<!-- -->\n<ul>\n<li>baz</li>\n<li>bim</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0308()
    {
        var md = "-   foo\n\n    notcode\n\n-   foo\n\n<!-- -->\n\n    code";
        var expected = "<ul>\n<li>\n<p>foo</p>\n<p>notcode</p>\n</li>\n<li>\n<p>foo</p>\n</li>\n</ul>\n<!-- -->\n<pre><code>code\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0309()
    {
        var md = "- a\n - b\n  - c\n   - d\n  - e\n - f\n- g";
        var expected = "<ul>\n<li>a</li>\n<li>b</li>\n<li>c</li>\n<li>d</li>\n<li>e</li>\n<li>f</li>\n<li>g</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0310()
    {
        var md = "1. a\n\n  2. b\n\n   3. c";
        var expected = "<ol>\n<li>\n<p>a</p>\n</li>\n<li>\n<p>b</p>\n</li>\n<li>\n<p>c</p>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0311()
    {
        var md = "- a\n - b\n  - c\n   - d\n    - e";
        var expected = "<ul>\n<li>a</li>\n<li>b</li>\n<li>c</li>\n<li>d\n- e</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0312()
    {
        var md = "1. a\n\n  2. b\n\n    3. c";
        var expected = "<ol>\n<li>\n<p>a</p>\n</li>\n<li>\n<p>b</p>\n</li>\n</ol>\n<pre><code>3. c\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0313()
    {
        var md = "- a\n- b\n\n- c";
        var expected = "<ul>\n<li>\n<p>a</p>\n</li>\n<li>\n<p>b</p>\n</li>\n<li>\n<p>c</p>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0314()
    {
        var md = "* a\n*\n\n* c";
        var expected = "<ul>\n<li>\n<p>a</p>\n</li>\n<li></li>\n<li>\n<p>c</p>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0315()
    {
        var md = "- a\n- b\n\n  c\n- d";
        var expected = "<ul>\n<li>\n<p>a</p>\n</li>\n<li>\n<p>b</p>\n<p>c</p>\n</li>\n<li>\n<p>d</p>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0316()
    {
        var md = "- a\n- b\n\n  [ref]: /url\n- d";
        var expected = "<ul>\n<li>\n<p>a</p>\n</li>\n<li>\n<p>b</p>\n</li>\n<li>\n<p>d</p>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0317()
    {
        var md = "- a\n- ```\n  b\n\n\n  ```\n- c";
        var expected = "<ul>\n<li>a</li>\n<li>\n<pre><code>b\n\n\n</code></pre>\n</li>\n<li>c</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0318()
    {
        var md = "- a\n  - b\n\n    c\n- d";
        var expected = "<ul>\n<li>a\n<ul>\n<li>\n<p>b</p>\n<p>c</p>\n</li>\n</ul>\n</li>\n<li>d</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0319()
    {
        var md = "* a\n  > b\n  >\n* c";
        var expected = "<ul>\n<li>a\n<blockquote>\n<p>b</p>\n</blockquote>\n</li>\n<li>c</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0320()
    {
        var md = "- a\n  > b\n  ```\n  c\n  ```\n- d";
        var expected = "<ul>\n<li>a\n<blockquote>\n<p>b</p>\n</blockquote>\n<pre><code>c\n</code></pre>\n</li>\n<li>d</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0321()
    {
        var md = "- a";
        var expected = "<ul>\n<li>a</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0322()
    {
        var md = "- a\n  - b";
        var expected = "<ul>\n<li>a\n<ul>\n<li>b</li>\n</ul>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0323()
    {
        var md = "1. ```\n   foo\n   ```\n\n   bar";
        var expected = "<ol>\n<li>\n<pre><code>foo\n</code></pre>\n<p>bar</p>\n</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0324()
    {
        var md = "* foo\n  * bar\n\n  baz";
        var expected = "<ul>\n<li>\n<p>foo</p>\n<ul>\n<li>bar</li>\n</ul>\n<p>baz</p>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0325()
    {
        var md = "- a\n  - b\n  - c\n\n- d\n  - e\n  - f";
        var expected = "<ul>\n<li>\n<p>a</p>\n<ul>\n<li>b</li>\n<li>c</li>\n</ul>\n</li>\n<li>\n<p>d</p>\n<ul>\n<li>e</li>\n<li>f</li>\n</ul>\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0326()
    {
        var md = "`hi`lo`";
        var expected = "<p><code>hi</code>lo`</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0327()
    {
        var md = "`foo`";
        var expected = "<p><code>foo</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0328()
    {
        var md = "`` foo ` bar ``";
        var expected = "<p><code>foo ` bar</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0329()
    {
        var md = "` `` `";
        var expected = "<p><code>``</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0330()
    {
        var md = "`  ``  `";
        var expected = "<p><code> `` </code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0331()
    {
        var md = "` a`";
        var expected = "<p><code> a</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0332()
    {
        var md = "` b `";
        var expected = "<p><code> b </code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0333()
    {
        var md = "` `\n`  `";
        var expected = "<p><code> </code>\n<code>  </code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0334()
    {
        var md = "``\nfoo\nbar  \nbaz\n``";
        var expected = "<p><code>foo bar   baz</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0335()
    {
        var md = "``\nfoo \n``";
        var expected = "<p><code>foo </code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0336()
    {
        var md = "`foo   bar \nbaz`";
        var expected = "<p><code>foo   bar  baz</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0337()
    {
        var md = "`foo\\`bar`";
        var expected = "<p><code>foo\\</code>bar`</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0338()
    {
        var md = "``foo`bar``";
        var expected = "<p><code>foo`bar</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0339()
    {
        var md = "` foo `` bar `";
        var expected = "<p><code>foo `` bar</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0340()
    {
        var md = "*foo`*`";
        var expected = "<p>*foo<code>*</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0341()
    {
        var md = "[not a `link](/foo`)";
        var expected = "<p>[not a <code>link](/foo</code>)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0342()
    {
        var md = "`<a href=\"`\">`";
        var expected = "<p><code>&lt;a href=&quot;</code>&quot;&gt;`</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0343()
    {
        var md = "<a href=\"`\">`";
        var expected = "<p><a href=\"`\">`</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0344()
    {
        var md = "`<https://foo.bar.`baz>`";
        var expected = "<p><code>&lt;https://foo.bar.</code>baz&gt;`</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0345()
    {
        var md = "<https://foo.bar.`baz>`";
        var expected = "<p><a href=\"https://foo.bar.%60baz\">https://foo.bar.`baz</a>`</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0346()
    {
        var md = "```foo``";
        var expected = "<p>```foo``</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0347()
    {
        var md = "`foo";
        var expected = "<p>`foo</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0348()
    {
        var md = "`foo``bar``";
        var expected = "<p>`foo<code>bar</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0349()
    {
        var md = "*foo bar*";
        var expected = "<p><em>foo bar</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0350()
    {
        var md = "a * foo bar*";
        var expected = "<p>a * foo bar*</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0351()
    {
        var md = "a*\"foo\"*";
        var expected = "<p>a*&quot;foo&quot;*</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0352()
    {
        var md = "* a *";
        var expected = "<p>* a *</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0353()
    {
        var md = "*$*alpha.\n\n*£*bravo.\n\n*€*charlie.";
        var expected = "<p>*$*alpha.</p>\n<p>*£*bravo.</p>\n<p>*€*charlie.</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0354()
    {
        var md = "foo*bar*";
        var expected = "<p>foo<em>bar</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0355()
    {
        var md = "5*6*78";
        var expected = "<p>5<em>6</em>78</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0356()
    {
        var md = "_foo bar_";
        var expected = "<p><em>foo bar</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0357()
    {
        var md = "_ foo bar_";
        var expected = "<p>_ foo bar_</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0358()
    {
        var md = "a_\"foo\"_";
        var expected = "<p>a_&quot;foo&quot;_</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0359()
    {
        var md = "foo_bar_";
        var expected = "<p>foo_bar_</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0360()
    {
        var md = "5_6_78";
        var expected = "<p>5_6_78</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0361()
    {
        var md = "пристаням_стремятся_";
        var expected = "<p>пристаням_стремятся_</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0362()
    {
        var md = "aa_\"bb\"_cc";
        var expected = "<p>aa_&quot;bb&quot;_cc</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0363()
    {
        var md = "foo-_(bar)_";
        var expected = "<p>foo-<em>(bar)</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0364()
    {
        var md = "_foo*";
        var expected = "<p>_foo*</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0365()
    {
        var md = "*foo bar *";
        var expected = "<p>*foo bar *</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0366()
    {
        var md = "*foo bar\n*";
        var expected = "<p>*foo bar\n*</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0367()
    {
        var md = "*(*foo)";
        var expected = "<p>*(*foo)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0368()
    {
        var md = "*(*foo*)*";
        var expected = "<p><em>(<em>foo</em>)</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0369()
    {
        var md = "*foo*bar";
        var expected = "<p><em>foo</em>bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0370()
    {
        var md = "_foo bar _";
        var expected = "<p>_foo bar _</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0371()
    {
        var md = "_(_foo)";
        var expected = "<p>_(_foo)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0372()
    {
        var md = "_(_foo_)_";
        var expected = "<p><em>(<em>foo</em>)</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0373()
    {
        var md = "_foo_bar";
        var expected = "<p>_foo_bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0374()
    {
        var md = "_пристаням_стремятся";
        var expected = "<p>_пристаням_стремятся</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0375()
    {
        var md = "_foo_bar_baz_";
        var expected = "<p><em>foo_bar_baz</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0376()
    {
        var md = "_(bar)_.";
        var expected = "<p><em>(bar)</em>.</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0377()
    {
        var md = "**foo bar**";
        var expected = "<p><strong>foo bar</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0378()
    {
        var md = "** foo bar**";
        var expected = "<p>** foo bar**</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0379()
    {
        var md = "a**\"foo\"**";
        var expected = "<p>a**&quot;foo&quot;**</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0380()
    {
        var md = "foo**bar**";
        var expected = "<p>foo<strong>bar</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0381()
    {
        var md = "__foo bar__";
        var expected = "<p><strong>foo bar</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0382()
    {
        var md = "__ foo bar__";
        var expected = "<p>__ foo bar__</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0383()
    {
        var md = "__\nfoo bar__";
        var expected = "<p>__\nfoo bar__</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0384()
    {
        var md = "a__\"foo\"__";
        var expected = "<p>a__&quot;foo&quot;__</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0385()
    {
        var md = "foo__bar__";
        var expected = "<p>foo__bar__</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0386()
    {
        var md = "5__6__78";
        var expected = "<p>5__6__78</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0387()
    {
        var md = "пристаням__стремятся__";
        var expected = "<p>пристаням__стремятся__</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0388()
    {
        var md = "__foo, __bar__, baz__";
        var expected = "<p><strong>foo, <strong>bar</strong>, baz</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0389()
    {
        var md = "foo-__(bar)__";
        var expected = "<p>foo-<strong>(bar)</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0390()
    {
        var md = "**foo bar **";
        var expected = "<p>**foo bar **</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0391()
    {
        var md = "**(**foo)";
        var expected = "<p>**(**foo)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0392()
    {
        var md = "*(**foo**)*";
        var expected = "<p><em>(<strong>foo</strong>)</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0393()
    {
        var md = "**Gomphocarpus (*Gomphocarpus physocarpus*, syn.\n*Asclepias physocarpa*)**";
        var expected = "<p><strong>Gomphocarpus (<em>Gomphocarpus physocarpus</em>, syn.\n<em>Asclepias physocarpa</em>)</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0394()
    {
        var md = "**foo \"*bar*\" foo**";
        var expected = "<p><strong>foo &quot;<em>bar</em>&quot; foo</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0395()
    {
        var md = "**foo**bar";
        var expected = "<p><strong>foo</strong>bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0396()
    {
        var md = "__foo bar __";
        var expected = "<p>__foo bar __</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0397()
    {
        var md = "__(__foo)";
        var expected = "<p>__(__foo)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0398()
    {
        var md = "_(__foo__)_";
        var expected = "<p><em>(<strong>foo</strong>)</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0399()
    {
        var md = "__foo__bar";
        var expected = "<p>__foo__bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0400()
    {
        var md = "__пристаням__стремятся";
        var expected = "<p>__пристаням__стремятся</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0401()
    {
        var md = "__foo__bar__baz__";
        var expected = "<p><strong>foo__bar__baz</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0402()
    {
        var md = "__(bar)__.";
        var expected = "<p><strong>(bar)</strong>.</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0403()
    {
        var md = "*foo [bar](/url)*";
        var expected = "<p><em>foo <a href=\"/url\">bar</a></em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0404()
    {
        var md = "*foo\nbar*";
        var expected = "<p><em>foo\nbar</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0405()
    {
        var md = "_foo __bar__ baz_";
        var expected = "<p><em>foo <strong>bar</strong> baz</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0406()
    {
        var md = "_foo _bar_ baz_";
        var expected = "<p><em>foo <em>bar</em> baz</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0407()
    {
        var md = "__foo_ bar_";
        var expected = "<p><em><em>foo</em> bar</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0408()
    {
        var md = "*foo *bar**";
        var expected = "<p><em>foo <em>bar</em></em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0409()
    {
        var md = "*foo **bar** baz*";
        var expected = "<p><em>foo <strong>bar</strong> baz</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0410()
    {
        var md = "*foo**bar**baz*";
        var expected = "<p><em>foo<strong>bar</strong>baz</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0411()
    {
        var md = "*foo**bar*";
        var expected = "<p><em>foo**bar</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0412()
    {
        var md = "***foo** bar*";
        var expected = "<p><em><strong>foo</strong> bar</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0413()
    {
        var md = "*foo **bar***";
        var expected = "<p><em>foo <strong>bar</strong></em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0414()
    {
        var md = "*foo**bar***";
        var expected = "<p><em>foo<strong>bar</strong></em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0415()
    {
        var md = "foo***bar***baz";
        var expected = "<p>foo<em><strong>bar</strong></em>baz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0416()
    {
        var md = "foo******bar*********baz";
        var expected = "<p>foo<strong><strong><strong>bar</strong></strong></strong>***baz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0417()
    {
        var md = "*foo **bar *baz* bim** bop*";
        var expected = "<p><em>foo <strong>bar <em>baz</em> bim</strong> bop</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0418()
    {
        var md = "*foo [*bar*](/url)*";
        var expected = "<p><em>foo <a href=\"/url\"><em>bar</em></a></em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0419()
    {
        var md = "** is not an empty emphasis";
        var expected = "<p>** is not an empty emphasis</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0420()
    {
        var md = "**** is not an empty strong emphasis";
        var expected = "<p>**** is not an empty strong emphasis</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0421()
    {
        var md = "**foo [bar](/url)**";
        var expected = "<p><strong>foo <a href=\"/url\">bar</a></strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0422()
    {
        var md = "**foo\nbar**";
        var expected = "<p><strong>foo\nbar</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0423()
    {
        var md = "__foo _bar_ baz__";
        var expected = "<p><strong>foo <em>bar</em> baz</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0424()
    {
        var md = "__foo __bar__ baz__";
        var expected = "<p><strong>foo <strong>bar</strong> baz</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0425()
    {
        var md = "____foo__ bar__";
        var expected = "<p><strong><strong>foo</strong> bar</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0426()
    {
        var md = "**foo **bar****";
        var expected = "<p><strong>foo <strong>bar</strong></strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0427()
    {
        var md = "**foo *bar* baz**";
        var expected = "<p><strong>foo <em>bar</em> baz</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0428()
    {
        var md = "**foo*bar*baz**";
        var expected = "<p><strong>foo<em>bar</em>baz</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0429()
    {
        var md = "***foo* bar**";
        var expected = "<p><strong><em>foo</em> bar</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0430()
    {
        var md = "**foo *bar***";
        var expected = "<p><strong>foo <em>bar</em></strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0431()
    {
        var md = "**foo *bar **baz**\nbim* bop**";
        var expected = "<p><strong>foo <em>bar <strong>baz</strong>\nbim</em> bop</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0432()
    {
        var md = "**foo [*bar*](/url)**";
        var expected = "<p><strong>foo <a href=\"/url\"><em>bar</em></a></strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0433()
    {
        var md = "__ is not an empty emphasis";
        var expected = "<p>__ is not an empty emphasis</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0434()
    {
        var md = "____ is not an empty strong emphasis";
        var expected = "<p>____ is not an empty strong emphasis</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0435()
    {
        var md = "foo ***";
        var expected = "<p>foo ***</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0436()
    {
        var md = "foo *\\**";
        var expected = "<p>foo <em>*</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0437()
    {
        var md = "foo *_*";
        var expected = "<p>foo <em>_</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0438()
    {
        var md = "foo *****";
        var expected = "<p>foo *****</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0439()
    {
        var md = "foo **\\***";
        var expected = "<p>foo <strong>*</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0440()
    {
        var md = "foo **_**";
        var expected = "<p>foo <strong>_</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0441()
    {
        var md = "**foo*";
        var expected = "<p>*<em>foo</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0442()
    {
        var md = "*foo**";
        var expected = "<p><em>foo</em>*</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0443()
    {
        var md = "***foo**";
        var expected = "<p>*<strong>foo</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0444()
    {
        var md = "****foo*";
        var expected = "<p>***<em>foo</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0445()
    {
        var md = "**foo***";
        var expected = "<p><strong>foo</strong>*</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0446()
    {
        var md = "*foo****";
        var expected = "<p><em>foo</em>***</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0447()
    {
        var md = "foo ___";
        var expected = "<p>foo ___</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0448()
    {
        var md = "foo _\\__";
        var expected = "<p>foo <em>_</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0449()
    {
        var md = "foo _*_";
        var expected = "<p>foo <em>*</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0450()
    {
        var md = "foo _____";
        var expected = "<p>foo _____</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0451()
    {
        var md = "foo __\\___";
        var expected = "<p>foo <strong>_</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0452()
    {
        var md = "foo __*__";
        var expected = "<p>foo <strong>*</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0453()
    {
        var md = "__foo_";
        var expected = "<p>_<em>foo</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0454()
    {
        var md = "_foo__";
        var expected = "<p><em>foo</em>_</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0455()
    {
        var md = "___foo__";
        var expected = "<p>_<strong>foo</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0456()
    {
        var md = "____foo_";
        var expected = "<p>___<em>foo</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0457()
    {
        var md = "__foo___";
        var expected = "<p><strong>foo</strong>_</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0458()
    {
        var md = "_foo____";
        var expected = "<p><em>foo</em>___</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0459()
    {
        var md = "**foo**";
        var expected = "<p><strong>foo</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0460()
    {
        var md = "*_foo_*";
        var expected = "<p><em><em>foo</em></em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0461()
    {
        var md = "__foo__";
        var expected = "<p><strong>foo</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0462()
    {
        var md = "_*foo*_";
        var expected = "<p><em><em>foo</em></em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0463()
    {
        var md = "****foo****";
        var expected = "<p><strong><strong>foo</strong></strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0464()
    {
        var md = "____foo____";
        var expected = "<p><strong><strong>foo</strong></strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0465()
    {
        var md = "******foo******";
        var expected = "<p><strong><strong><strong>foo</strong></strong></strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0466()
    {
        var md = "***foo***";
        var expected = "<p><em><strong>foo</strong></em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0467()
    {
        var md = "_____foo_____";
        var expected = "<p><em><strong><strong>foo</strong></strong></em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0468()
    {
        var md = "*foo _bar* baz_";
        var expected = "<p><em>foo _bar</em> baz_</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0469()
    {
        var md = "*foo __bar *baz bim__ bam*";
        var expected = "<p><em>foo <strong>bar *baz bim</strong> bam</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0470()
    {
        var md = "**foo **bar baz**";
        var expected = "<p>**foo <strong>bar baz</strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0471()
    {
        var md = "*foo *bar baz*";
        var expected = "<p>*foo <em>bar baz</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0472()
    {
        var md = "*[bar*](/url)";
        var expected = "<p>*<a href=\"/url\">bar*</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0473()
    {
        var md = "_foo [bar_](/url)";
        var expected = "<p>_foo <a href=\"/url\">bar_</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0474()
    {
        var md = "*<img src=\"foo\" title=\"*\"/>";
        var expected = "<p>*<img src=\"foo\" title=\"*\"/></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0475()
    {
        var md = "**<a href=\"**\">";
        var expected = "<p>**<a href=\"**\"></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0476()
    {
        var md = "__<a href=\"__\">";
        var expected = "<p>__<a href=\"__\"></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0477()
    {
        var md = "*a `*`*";
        var expected = "<p><em>a <code>*</code></em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0478()
    {
        var md = "_a `_`_";
        var expected = "<p><em>a <code>_</code></em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0479()
    {
        var md = "**a<https://foo.bar/?q=**>";
        var expected = "<p>**a<a href=\"https://foo.bar/?q=**\">https://foo.bar/?q=**</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0480()
    {
        var md = "__a<https://foo.bar/?q=__>";
        var expected = "<p>__a<a href=\"https://foo.bar/?q=__\">https://foo.bar/?q=__</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0481()
    {
        var md = "[link](/uri \"title\")";
        var expected = "<p><a href=\"/uri\" title=\"title\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0482()
    {
        var md = "[link](/uri)";
        var expected = "<p><a href=\"/uri\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0483()
    {
        var md = "[](./target.md)";
        var expected = "<p><a href=\"./target.md\"></a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0484()
    {
        var md = "[link]()";
        var expected = "<p><a href=\"\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0485()
    {
        var md = "[link](<>)";
        var expected = "<p><a href=\"\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0486()
    {
        var md = "[]()";
        var expected = "<p><a href=\"\"></a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0487()
    {
        var md = "[link](/my uri)";
        var expected = "<p>[link](/my uri)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0488()
    {
        var md = "[link](</my uri>)";
        var expected = "<p><a href=\"/my%20uri\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0489()
    {
        var md = "[link](foo\nbar)";
        var expected = "<p>[link](foo\nbar)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0490()
    {
        var md = "[link](<foo\nbar>)";
        var expected = "<p>[link](<foo\nbar>)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0491()
    {
        var md = "[a](<b)c>)";
        var expected = "<p><a href=\"b)c\">a</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0492()
    {
        var md = "[link](<foo\\>)";
        var expected = "<p>[link](&lt;foo&gt;)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0493()
    {
        var md = "[a](<b)c\n[a](<b)c>\n[a](<b>c)";
        var expected = "<p>[a](&lt;b)c\n[a](&lt;b)c&gt;\n[a](<b>c)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0494()
    {
        var md = "[link](\\(foo\\))";
        var expected = "<p><a href=\"(foo)\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0495()
    {
        var md = "[link](foo(and(bar)))";
        var expected = "<p><a href=\"foo(and(bar))\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0496()
    {
        var md = "[link](foo(and(bar))";
        var expected = "<p>[link](foo(and(bar))</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0497()
    {
        var md = "[link](foo\\(and\\(bar\\))";
        var expected = "<p><a href=\"foo(and(bar)\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0498()
    {
        var md = "[link](<foo(and(bar)>)";
        var expected = "<p><a href=\"foo(and(bar)\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0499()
    {
        var md = "[link](foo\\)\\:)";
        var expected = "<p><a href=\"foo):\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0500()
    {
        var md = "[link](#fragment)\n\n[link](https://example.com#fragment)\n\n[link](https://example.com?foo=3#frag)";
        var expected = "<p><a href=\"#fragment\">link</a></p>\n<p><a href=\"https://example.com#fragment\">link</a></p>\n<p><a href=\"https://example.com?foo=3#frag\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0501()
    {
        var md = "[link](foo\\bar)";
        var expected = "<p><a href=\"foo%5Cbar\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0502()
    {
        var md = "[link](foo%20b&auml;)";
        var expected = "<p><a href=\"foo%20b%C3%A4\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0503()
    {
        var md = "[link](\"title\")";
        var expected = "<p><a href=\"%22title%22\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0504()
    {
        var md = "[link](/url \"title\")\n[link](/url 'title')\n[link](/url (title))";
        var expected = "<p><a href=\"/url\" title=\"title\">link</a>\n<a href=\"/url\" title=\"title\">link</a>\n<a href=\"/url\" title=\"title\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0505()
    {
        var md = "[link](/url \"title \\\"&quot;\")";
        var expected = "<p><a href=\"/url\" title=\"title &quot;&quot;\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0506()
    {
        var md = "[link](/url \"title\")";
        var expected = "<p><a href=\"/url%C2%A0%22title%22\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0507()
    {
        var md = "[link](/url \"title \"and\" title\")";
        var expected = "<p>[link](/url &quot;title &quot;and&quot; title&quot;)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0508()
    {
        var md = "[link](/url 'title \"and\" title')";
        var expected = "<p><a href=\"/url\" title=\"title &quot;and&quot; title\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0509()
    {
        var md = "[link](   /uri\n  \"title\"  )";
        var expected = "<p><a href=\"/uri\" title=\"title\">link</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0510()
    {
        var md = "[link] (/uri)";
        var expected = "<p>[link] (/uri)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0511()
    {
        var md = "[link [foo [bar]]](/uri)";
        var expected = "<p><a href=\"/uri\">link [foo [bar]]</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0512()
    {
        var md = "[link] bar](/uri)";
        var expected = "<p>[link] bar](/uri)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0513()
    {
        var md = "[link [bar](/uri)";
        var expected = "<p>[link <a href=\"/uri\">bar</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0514()
    {
        var md = "[link \\[bar](/uri)";
        var expected = "<p><a href=\"/uri\">link [bar</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0515()
    {
        var md = "[link *foo **bar** `#`*](/uri)";
        var expected = "<p><a href=\"/uri\">link <em>foo <strong>bar</strong> <code>#</code></em></a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0516()
    {
        var md = "[![moon](moon.jpg)](/uri)";
        var expected = "<p><a href=\"/uri\"><img src=\"moon.jpg\" alt=\"moon\" /></a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0517()
    {
        var md = "[foo [bar](/uri)](/uri)";
        var expected = "<p>[foo <a href=\"/uri\">bar</a>](/uri)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0518()
    {
        var md = "[foo *[bar [baz](/uri)](/uri)*](/uri)";
        var expected = "<p>[foo <em>[bar <a href=\"/uri\">baz</a>](/uri)</em>](/uri)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0519()
    {
        var md = "![[[foo](uri1)](uri2)](uri3)";
        var expected = "<p><img src=\"uri3\" alt=\"[foo](uri2)\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0520()
    {
        var md = "*[foo*](/uri)";
        var expected = "<p>*<a href=\"/uri\">foo*</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0521()
    {
        var md = "[foo *bar](baz*)";
        var expected = "<p><a href=\"baz*\">foo *bar</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0522()
    {
        var md = "*foo [bar* baz]";
        var expected = "<p><em>foo [bar</em> baz]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0523()
    {
        var md = "[foo <bar attr=\"](baz)\">";
        var expected = "<p>[foo <bar attr=\"](baz)\"></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0524()
    {
        var md = "[foo`](/uri)`";
        var expected = "<p>[foo<code>](/uri)</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0525()
    {
        var md = "[foo<https://example.com/?search=](uri)>";
        var expected = "<p>[foo<a href=\"https://example.com/?search=%5D(uri)\">https://example.com/?search=](uri)</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0526()
    {
        var md = "[foo][bar]\n\n[bar]: /url \"title\"";
        var expected = "<p><a href=\"/url\" title=\"title\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0527()
    {
        var md = "[link [foo [bar]]][ref]\n\n[ref]: /uri";
        var expected = "<p><a href=\"/uri\">link [foo [bar]]</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0528()
    {
        var md = "[link \\[bar][ref]\n\n[ref]: /uri";
        var expected = "<p><a href=\"/uri\">link [bar</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0529()
    {
        var md = "[link *foo **bar** `#`*][ref]\n\n[ref]: /uri";
        var expected = "<p><a href=\"/uri\">link <em>foo <strong>bar</strong> <code>#</code></em></a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0530()
    {
        var md = "[![moon](moon.jpg)][ref]\n\n[ref]: /uri";
        var expected = "<p><a href=\"/uri\"><img src=\"moon.jpg\" alt=\"moon\" /></a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0531()
    {
        var md = "[foo [bar](/uri)][ref]\n\n[ref]: /uri";
        var expected = "<p>[foo <a href=\"/uri\">bar</a>]<a href=\"/uri\">ref</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0532()
    {
        var md = "[foo *bar [baz][ref]*][ref]\n\n[ref]: /uri";
        var expected = "<p>[foo <em>bar <a href=\"/uri\">baz</a></em>]<a href=\"/uri\">ref</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0533()
    {
        var md = "*[foo*][ref]\n\n[ref]: /uri";
        var expected = "<p>*<a href=\"/uri\">foo*</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0534()
    {
        var md = "[foo *bar][ref]*\n\n[ref]: /uri";
        var expected = "<p><a href=\"/uri\">foo *bar</a>*</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0535()
    {
        var md = "[foo <bar attr=\"][ref]\">\n\n[ref]: /uri";
        var expected = "<p>[foo <bar attr=\"][ref]\"></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0536()
    {
        var md = "[foo`][ref]`\n\n[ref]: /uri";
        var expected = "<p>[foo<code>][ref]</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0537()
    {
        var md = "[foo<https://example.com/?search=][ref]>\n\n[ref]: /uri";
        var expected = "<p>[foo<a href=\"https://example.com/?search=%5D%5Bref%5D\">https://example.com/?search=][ref]</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0538()
    {
        var md = "[foo][BaR]\n\n[bar]: /url \"title\"";
        var expected = "<p><a href=\"/url\" title=\"title\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0539()
    {
        var md = "[ẞ]\n\n[SS]: /url";
        var expected = "<p><a href=\"/url\">ẞ</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0540()
    {
        var md = "[Foo\n  bar]: /url\n\n[Baz][Foo bar]";
        var expected = "<p><a href=\"/url\">Baz</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0541()
    {
        var md = "[foo] [bar]\n\n[bar]: /url \"title\"";
        var expected = "<p>[foo] <a href=\"/url\" title=\"title\">bar</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0542()
    {
        var md = "[foo]\n[bar]\n\n[bar]: /url \"title\"";
        var expected = "<p>[foo]\n<a href=\"/url\" title=\"title\">bar</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0543()
    {
        var md = "[foo]: /url1\n\n[foo]: /url2\n\n[bar][foo]";
        var expected = "<p><a href=\"/url1\">bar</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0544()
    {
        var md = "[bar][foo\\!]\n\n[foo!]: /url";
        var expected = "<p>[bar][foo!]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0545()
    {
        var md = "[foo][ref[]\n\n[ref[]: /uri";
        var expected = "<p>[foo][ref[]</p>\n<p>[ref[]: /uri</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0546()
    {
        var md = "[foo][ref[bar]]\n\n[ref[bar]]: /uri";
        var expected = "<p>[foo][ref[bar]]</p>\n<p>[ref[bar]]: /uri</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0547()
    {
        var md = "[[[foo]]]\n\n[[[foo]]]: /url";
        var expected = "<p>[[[foo]]]</p>\n<p>[[[foo]]]: /url</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0548()
    {
        var md = "[foo][ref\\[]\n\n[ref\\[]: /uri";
        var expected = "<p><a href=\"/uri\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0549()
    {
        var md = "[bar\\\\]: /uri\n\n[bar\\\\]";
        var expected = "<p><a href=\"/uri\">bar\\</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0550()
    {
        var md = "[]\n\n[]: /uri";
        var expected = "<p>[]</p>\n<p>[]: /uri</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0551()
    {
        var md = "[\n ]\n\n[\n ]: /uri";
        var expected = "<p>[\n]</p>\n<p>[\n]: /uri</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0552()
    {
        var md = "[foo][]\n\n[foo]: /url \"title\"";
        var expected = "<p><a href=\"/url\" title=\"title\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0553()
    {
        var md = "[*foo* bar][]\n\n[*foo* bar]: /url \"title\"";
        var expected = "<p><a href=\"/url\" title=\"title\"><em>foo</em> bar</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0554()
    {
        var md = "[Foo][]\n\n[foo]: /url \"title\"";
        var expected = "<p><a href=\"/url\" title=\"title\">Foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0555()
    {
        var md = "[foo] \n[]\n\n[foo]: /url \"title\"";
        var expected = "<p><a href=\"/url\" title=\"title\">foo</a>\n[]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0556()
    {
        var md = "[foo]\n\n[foo]: /url \"title\"";
        var expected = "<p><a href=\"/url\" title=\"title\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0557()
    {
        var md = "[*foo* bar]\n\n[*foo* bar]: /url \"title\"";
        var expected = "<p><a href=\"/url\" title=\"title\"><em>foo</em> bar</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0558()
    {
        var md = "[[*foo* bar]]\n\n[*foo* bar]: /url \"title\"";
        var expected = "<p>[<a href=\"/url\" title=\"title\"><em>foo</em> bar</a>]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0559()
    {
        var md = "[[bar [foo]\n\n[foo]: /url";
        var expected = "<p>[[bar <a href=\"/url\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0560()
    {
        var md = "[Foo]\n\n[foo]: /url \"title\"";
        var expected = "<p><a href=\"/url\" title=\"title\">Foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0561()
    {
        var md = "[foo] bar\n\n[foo]: /url";
        var expected = "<p><a href=\"/url\">foo</a> bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0562()
    {
        var md = "\\[foo]\n\n[foo]: /url \"title\"";
        var expected = "<p>[foo]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0563()
    {
        var md = "[foo*]: /url\n\n*[foo*]";
        var expected = "<p>*<a href=\"/url\">foo*</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0564()
    {
        var md = "[foo][bar]\n\n[foo]: /url1\n[bar]: /url2";
        var expected = "<p><a href=\"/url2\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0565()
    {
        var md = "[foo][]\n\n[foo]: /url1";
        var expected = "<p><a href=\"/url1\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0566()
    {
        var md = "[foo]()\n\n[foo]: /url1";
        var expected = "<p><a href=\"\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0567()
    {
        var md = "[foo](not a link)\n\n[foo]: /url1";
        var expected = "<p><a href=\"/url1\">foo</a>(not a link)</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0568()
    {
        var md = "[foo][bar][baz]\n\n[baz]: /url";
        var expected = "<p>[foo]<a href=\"/url\">bar</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0569()
    {
        var md = "[foo][bar][baz]\n\n[baz]: /url1\n[bar]: /url2";
        var expected = "<p><a href=\"/url2\">foo</a><a href=\"/url1\">baz</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0570()
    {
        var md = "[foo][bar][baz]\n\n[baz]: /url1\n[foo]: /url2";
        var expected = "<p>[foo]<a href=\"/url1\">bar</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0571()
    {
        var md = "![foo](/url \"title\")";
        var expected = "<p><img src=\"/url\" alt=\"foo\" title=\"title\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0572()
    {
        var md = "![foo *bar*]\n\n[foo *bar*]: train.jpg \"train & tracks\"";
        var expected = "<p><img src=\"train.jpg\" alt=\"foo bar\" title=\"train &amp; tracks\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0573()
    {
        var md = "![foo ![bar](/url)](/url2)";
        var expected = "<p><img src=\"/url2\" alt=\"foo bar\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0574()
    {
        var md = "![foo [bar](/url)](/url2)";
        var expected = "<p><img src=\"/url2\" alt=\"foo bar\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0575()
    {
        var md = "![foo *bar*][]\n\n[foo *bar*]: train.jpg \"train & tracks\"";
        var expected = "<p><img src=\"train.jpg\" alt=\"foo bar\" title=\"train &amp; tracks\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0576()
    {
        var md = "![foo *bar*][foobar]\n\n[FOOBAR]: train.jpg \"train & tracks\"";
        var expected = "<p><img src=\"train.jpg\" alt=\"foo bar\" title=\"train &amp; tracks\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0577()
    {
        var md = "![foo](train.jpg)";
        var expected = "<p><img src=\"train.jpg\" alt=\"foo\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0578()
    {
        var md = "My ![foo bar](/path/to/train.jpg  \"title\"   )";
        var expected = "<p>My <img src=\"/path/to/train.jpg\" alt=\"foo bar\" title=\"title\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0579()
    {
        var md = "![foo](<url>)";
        var expected = "<p><img src=\"url\" alt=\"foo\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0580()
    {
        var md = "![](/url)";
        var expected = "<p><img src=\"/url\" alt=\"\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0581()
    {
        var md = "![foo][bar]\n\n[bar]: /url";
        var expected = "<p><img src=\"/url\" alt=\"foo\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0582()
    {
        var md = "![foo][bar]\n\n[BAR]: /url";
        var expected = "<p><img src=\"/url\" alt=\"foo\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0583()
    {
        var md = "![foo][]\n\n[foo]: /url \"title\"";
        var expected = "<p><img src=\"/url\" alt=\"foo\" title=\"title\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0584()
    {
        var md = "![*foo* bar][]\n\n[*foo* bar]: /url \"title\"";
        var expected = "<p><img src=\"/url\" alt=\"foo bar\" title=\"title\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0585()
    {
        var md = "![Foo][]\n\n[foo]: /url \"title\"";
        var expected = "<p><img src=\"/url\" alt=\"Foo\" title=\"title\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0586()
    {
        var md = "![foo] \n[]\n\n[foo]: /url \"title\"";
        var expected = "<p><img src=\"/url\" alt=\"foo\" title=\"title\" />\n[]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0587()
    {
        var md = "![foo]\n\n[foo]: /url \"title\"";
        var expected = "<p><img src=\"/url\" alt=\"foo\" title=\"title\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0588()
    {
        var md = "![*foo* bar]\n\n[*foo* bar]: /url \"title\"";
        var expected = "<p><img src=\"/url\" alt=\"foo bar\" title=\"title\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0589()
    {
        var md = "![[foo]]\n\n[[foo]]: /url \"title\"";
        var expected = "<p>![[foo]]</p>\n<p>[[foo]]: /url &quot;title&quot;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0590()
    {
        var md = "![Foo]\n\n[foo]: /url \"title\"";
        var expected = "<p><img src=\"/url\" alt=\"Foo\" title=\"title\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0591()
    {
        var md = "!\\[foo]\n\n[foo]: /url \"title\"";
        var expected = "<p>![foo]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0592()
    {
        var md = "\\![foo]\n\n[foo]: /url \"title\"";
        var expected = "<p>!<a href=\"/url\" title=\"title\">foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0593()
    {
        var md = "<http://foo.bar.baz>";
        var expected = "<p><a href=\"http://foo.bar.baz\">http://foo.bar.baz</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0594()
    {
        var md = "<https://foo.bar.baz/test?q=hello&id=22&boolean>";
        var expected = "<p><a href=\"https://foo.bar.baz/test?q=hello&amp;id=22&amp;boolean\">https://foo.bar.baz/test?q=hello&amp;id=22&amp;boolean</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0595()
    {
        var md = "<irc://foo.bar:2233/baz>";
        var expected = "<p><a href=\"irc://foo.bar:2233/baz\">irc://foo.bar:2233/baz</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0596()
    {
        var md = "<MAILTO:FOO@BAR.BAZ>";
        var expected = "<p><a href=\"MAILTO:FOO@BAR.BAZ\">MAILTO:FOO@BAR.BAZ</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0597()
    {
        var md = "<a+b+c:d>";
        var expected = "<p><a href=\"a+b+c:d\">a+b+c:d</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0598()
    {
        var md = "<made-up-scheme://foo,bar>";
        var expected = "<p><a href=\"made-up-scheme://foo,bar\">made-up-scheme://foo,bar</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0599()
    {
        var md = "<https://../>";
        var expected = "<p><a href=\"https://../\">https://../</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0600()
    {
        var md = "<localhost:5001/foo>";
        var expected = "<p><a href=\"localhost:5001/foo\">localhost:5001/foo</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0601()
    {
        var md = "<https://foo.bar/baz bim>";
        var expected = "<p>&lt;https://foo.bar/baz bim&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0602()
    {
        var md = "<https://example.com/\\[\\>";
        var expected = "<p><a href=\"https://example.com/%5C%5B%5C\">https://example.com/\\[\\</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0603()
    {
        var md = "<foo@bar.example.com>";
        var expected = "<p><a href=\"mailto:foo@bar.example.com\">foo@bar.example.com</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0604()
    {
        var md = "<foo+special@Bar.baz-bar0.com>";
        var expected = "<p><a href=\"mailto:foo+special@Bar.baz-bar0.com\">foo+special@Bar.baz-bar0.com</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0605()
    {
        var md = "<foo\\+@bar.example.com>";
        var expected = "<p>&lt;foo+@bar.example.com&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0606()
    {
        var md = "<>";
        var expected = "<p>&lt;&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0607()
    {
        var md = "< https://foo.bar >";
        var expected = "<p>&lt; https://foo.bar &gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0608()
    {
        var md = "<m:abc>";
        var expected = "<p>&lt;m:abc&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0609()
    {
        var md = "<foo.bar.baz>";
        var expected = "<p>&lt;foo.bar.baz&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0610()
    {
        var md = "https://example.com";
        var expected = "<p>https://example.com</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0611()
    {
        var md = "foo@bar.example.com";
        var expected = "<p>foo@bar.example.com</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0612()
    {
        var md = "<a><bab><c2c>";
        var expected = "<p><a><bab><c2c></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0613()
    {
        var md = "<a/><b2/>";
        var expected = "<p><a/><b2/></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0614()
    {
        var md = "<a  /><b2\ndata=\"foo\" >";
        var expected = "<p><a  /><b2\ndata=\"foo\" ></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0615()
    {
        var md = "<a foo=\"bar\" bam = 'baz <em>\"</em>'\n_boolean zoop:33=zoop:33 />";
        var expected = "<p><a foo=\"bar\" bam = 'baz <em>\"</em>'\n_boolean zoop:33=zoop:33 /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0616()
    {
        var md = "Foo <responsive-image src=\"foo.jpg\" />";
        var expected = "<p>Foo <responsive-image src=\"foo.jpg\" /></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0617()
    {
        var md = "<33> <__>";
        var expected = "<p>&lt;33&gt; &lt;__&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0618()
    {
        var md = "<a h*#ref=\"hi\">";
        var expected = "<p>&lt;a h*#ref=&quot;hi&quot;&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0619()
    {
        var md = "<a href=\"hi'> <a href=hi'>";
        var expected = "<p>&lt;a href=&quot;hi'&gt; &lt;a href=hi'&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0620()
    {
        var md = "< a><\nfoo><bar/ >\n<foo bar=baz\nbim!bop />";
        var expected = "<p>&lt; a&gt;&lt;\nfoo&gt;&lt;bar/ &gt;\n&lt;foo bar=baz\nbim!bop /&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0621()
    {
        var md = "<a href='bar'title=title>";
        var expected = "<p>&lt;a href='bar'title=title&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0622()
    {
        var md = "</a></foo >";
        var expected = "<p></a></foo ></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0623()
    {
        var md = "</a href=\"foo\">";
        var expected = "<p>&lt;/a href=&quot;foo&quot;&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0624()
    {
        var md = "foo <!-- this is a --\ncomment - with hyphens -->";
        var expected = "<p>foo <!-- this is a --\ncomment - with hyphens --></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0625()
    {
        var md = "foo <!--> foo -->\n\nfoo <!---> foo -->";
        var expected = "<p>foo <!--> foo --&gt;</p>\n<p>foo <!---> foo --&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0626()
    {
        var md = "foo <?php echo $a; ?>";
        var expected = "<p>foo <?php echo $a; ?></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0627()
    {
        var md = "foo <!ELEMENT br EMPTY>";
        var expected = "<p>foo <!ELEMENT br EMPTY></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0628()
    {
        var md = "foo <![CDATA[>&<]]>";
        var expected = "<p>foo <![CDATA[>&<]]></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0629()
    {
        var md = "foo <a href=\"&ouml;\">";
        var expected = "<p>foo <a href=\"&ouml;\"></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0630()
    {
        var md = "foo <a href=\"\\*\">";
        var expected = "<p>foo <a href=\"\\*\"></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0631()
    {
        var md = "<a href=\"\\\"\">";
        var expected = "<p>&lt;a href=&quot;&quot;&quot;&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0632()
    {
        var md = "foo  \nbaz";
        var expected = "<p>foo<br />\nbaz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0633()
    {
        var md = "foo\\\nbaz";
        var expected = "<p>foo<br />\nbaz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0634()
    {
        var md = "foo       \nbaz";
        var expected = "<p>foo<br />\nbaz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0635()
    {
        var md = "foo  \n     bar";
        var expected = "<p>foo<br />\nbar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0636()
    {
        var md = "foo\\\n     bar";
        var expected = "<p>foo<br />\nbar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0637()
    {
        var md = "*foo  \nbar*";
        var expected = "<p><em>foo<br />\nbar</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0638()
    {
        var md = "*foo\\\nbar*";
        var expected = "<p><em>foo<br />\nbar</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0639()
    {
        var md = "`code  \nspan`";
        var expected = "<p><code>code   span</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0640()
    {
        var md = "`code\\\nspan`";
        var expected = "<p><code>code\\ span</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0641()
    {
        var md = "<a href=\"foo  \nbar\">";
        var expected = "<p><a href=\"foo  \nbar\"></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0642()
    {
        var md = "<a href=\"foo\\\nbar\">";
        var expected = "<p><a href=\"foo\\\nbar\"></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0643()
    {
        var md = "foo\\";
        var expected = "<p>foo\\</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0644()
    {
        var md = "foo  ";
        var expected = "<p>foo</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0645()
    {
        var md = "### foo\\";
        var expected = "<h3>foo\\</h3>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0646()
    {
        var md = "### foo  ";
        var expected = "<h3>foo</h3>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0647()
    {
        var md = "foo\nbaz";
        var expected = "<p>foo\nbaz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0648()
    {
        var md = "foo \n baz";
        var expected = "<p>foo\nbaz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0649()
    {
        var md = "hello $.;'there";
        var expected = "<p>hello $.;'there</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0650()
    {
        var md = "Foo χρῆν";
        var expected = "<p>Foo χρῆν</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0651()
    {
        var md = "Multiple     spaces";
        var expected = "<p>Multiple     spaces</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

}
