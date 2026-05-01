using Microsoft.UI.Reactor.Markdown;
using System.Text;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Md4cGenerated;

/// <summary>
/// Ported from md4c/test/regressions.txt
/// </summary>
public class Md4cRegressionsTest
{
    [Fact]
    public void Example_0001()
    {
        var md = "<gi att1=tok1 att2=tok2>";
        var expected = "<gi att1=tok1 att2=tok2>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0002()
    {
        var md = "foo <gi att1=tok1 att2=tok2> bar";
        var expected = "<p>foo <gi att1=tok1 att2=tok2> bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0003()
    {
        var md = "foo <gi att1=tok1\natt2=tok2> bar";
        var expected = "<p>foo <gi att1=tok1\natt2=tok2> bar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0004()
    {
        var md = "![alt text with *entity* &copy;](img.png 'title')";
        var expected = "<p><img src=\"img.png\" alt=\"alt text with entity ©\" title=\"title\"></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0005()
    {
        var md = "> [foo\n> bar]: /url\n>\n> [foo bar]";
        var expected = "<blockquote>\n<p><a href=\"/url\">foo\nbar</a></p>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0006()
    {
        var md = "[x]:\nx\n- <?\n\n  x";
        var expected = "<ul>\n<li><?\n\nx\n</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0007()
    {
        var md = "x [link](/url \"foo &ndash; bar\") x";
        var expected = "<p>x <a href=\"/url\" title=\"foo – bar\">link</a> x</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0008()
    {
        var md = "a***b* c*";
        var expected = "<p>a*<em><em>b</em> c</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0009()
    {
        var md = "***b* c*";
        var expected = "<p>*<em><em>b</em> c</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0010()
    {
        var md = "a*b**c*";
        var expected = "<p>a<em>b**c</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0011()
    {
        var md = "```&amp;&amp;&amp;&amp;&amp;&amp;&amp;&amp;";
        var expected = "<pre><code class=\"language-&amp;&amp;&amp;&amp;&amp;&amp;&amp;&amp;\"></code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0012()
    {
        var md = "__x_ _x___";
        var expected = "<p><em><em>x</em> <em>x</em></em>_</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0013()
    {
        var md = "[x](url\n'title'\n)x";
        var expected = "<p><a href=\"url\" title=\"title\">x</a>x</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0014()
    {
        var md = "* x|x\n---|---";
        var expected = "<ul>\n<li>x|x\n---|---</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0015()
    {
        var md = "* x|x\n  ---|---\nx|x";
        var expected = "<ul>\n<li><table>\n<thead>\n<tr>\n<th>x</th>\n<th>x</th>\n</tr>\n</thead>\n</table>\n</li>\n</ul>\n<p>x|x</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0016()
    {
        var md = "] http://x.x *x*\n\n|x|x|\n|---|---|\n|x|";
        var expected = "<p>] http://x.x <em>x</em></p>\n<table>\n<thead>\n<tr>\n<th>x</th>\n<th>x</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>x</td>\n<td></td>\n</tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0017()
    {
        var md = "This is [link](http://github.com/).";
        var expected = "<p>This is <a href=\"http://github.com/\">link</a>.</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveUrlAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0018()
    {
        var md = "This is [link](http://github.com/)X";
        var expected = "<p>This is <a href=\"http://github.com/\">link</a>X</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveUrlAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0019()
    {
        var md = "`";
        var expected = "<p>`</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0020()
    {
        var md = "~`foo`~";
        var expected = "<p><del><code>foo</code></del></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Strikethrough);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0021()
    {
        var md = "~*foo*~";
        var expected = "<p><del><em>foo</em></del></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Strikethrough);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0022()
    {
        var md = "*~foo~*";
        var expected = "<p><em><del>foo</del></em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Strikethrough);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0023()
    {
        var md = "[f]:\n-\n    xx\n-";
        var expected = "<pre><code>xx\n</code></pre>\n<ul>\n<li></li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0024()
    {
        var md = "*(http://example.com)*";
        var expected = "<p><em>(<a href=\"http://example.com\">http://example.com</a>)</em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveUrlAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0025()
    {
        var md = "[SS ẞ]: /url\n[ẞ SS]";
        var expected = "<p><a href=\"/url\">ẞ SS</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0026()
    {
        var md = "foo\n>";
        var expected = "<p>foo</p>\n<blockquote>\n</blockquote>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0027()
    {
        var md = ". foo";
        var expected = "<p>. foo</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0028()
    {
        var md = "[ab]: /foo\n[a] [ab] [abc]";
        var expected = "<p>[a] <a href=\"/foo\">ab</a> [abc]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0029()
    {
        var md = "[a b]: /foo\n[a   b]";
        var expected = "<p><a href=\"/foo\">a   b</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0030()
    {
        var md = "*a **b c* d**";
        var expected = "<p><em>a <em><em>b c</em> d</em></em></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0031()
    {
        var md = "<foo@123456789012345678901234567890123456789012345678901234567890123.123456789012345678901234567890123456789012345678901234567890123>";
        var expected = "<p><a href=\"mailto:foo@123456789012345678901234567890123456789012345678901234567890123.123456789012345678901234567890123456789012345678901234567890123\">foo@123456789012345678901234567890123456789012345678901234567890123.123456789012345678901234567890123456789012345678901234567890123</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0032()
    {
        var md = "<foo@123456789012345678901234567890123456789012345678901234567890123x.123456789012345678901234567890123456789012345678901234567890123>";
        var expected = "<p>&lt;foo@123456789012345678901234567890123456789012345678901234567890123x.123456789012345678901234567890123456789012345678901234567890123&gt;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0033()
    {
        var md = "A | B\n--- | ---\n[x](url)";
        var expected = "<table>\n<thead>\n<tr>\n<th>A</th>\n<th>B</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td><a href=\"url\">x</a></td>\n<td></td>\n</tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0034()
    {
        var md = "***foo *bar baz***";
        var expected = "<p>*<strong>foo <em>bar baz</em></strong></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0035()
    {
        var md = "~~~\n                x\n~~~\n\n~~~\n                 x\n~~~";
        var expected = "<pre><code>                x\n</code></pre>\n<pre><code>                 x\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0036()
    {
        var md = "[![alt][img]][link]\n\n[img]: img_url\n[link]: link_url";
        var expected = "<p><a href=\"link_url\"><img src=\"img_url\" alt=\"alt\"></a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0037()
    {
        var md = "| abc | def |\n| --- | --- |";
        var expected = "<table>\n<thead>\n<tr>\n<th>abc</th>\n<th>def</th>\n</tr>\n</thead>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0038()
    {
        var md = "[fooﬗ]: /url\n[fooﬕ]";
        var expected = "<p>[fooﬕ]</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0039()
    {
        var md = "- <script>\n- foo\nbar\n</script>";
        var expected = "<ul>\n<li><script>\n</li>\n<li>foo\nbar\n</script></li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0040()
    {
        var md = "[http://example.com](http://example.com)";
        var expected = "<p><a href=\"http://example.com\">http://example.com</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveUrlAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0041()
    {
        var md = "-\n\n    foo";
        var expected = "<ul>\n<li></li>\n</ul>\n<pre><code>foo\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0042()
    {
        var md = "<!-- foo -->\n    ```\n    bar\n    ```";
        var expected = "<!-- foo -->\n<pre><code>```\nbar\n```\n</code></pre>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0043()
    {
        var md = "foo\n    ```\n    bar\n    ```";
        var expected = "<p>foo\n<code>bar</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0044()
    {
        var md = "<textarea>\n\n*foo*\n\n_bar_\n\n</textarea>\n\nbaz";
        var expected = "<textarea>\n\n*foo*\n\n_bar_\n\n</textarea>\n<p>baz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0045()
    {
        var md = "![outer ![inner](img_inner \"inner title\")](img_outer \"outer title\")";
        var expected = "<p><img src=\"img_outer\" alt=\"outer inner\" title=\"outer title\"></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0046()
    {
        var md = "x\n|-\n|[*x*]()";
        var expected = "<table>\n<thead>\n<tr>\n<th>x</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td><a href=\"\"><em>x</em></a></td>\n</tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0047()
    {
        var md = "x\n|-\n[|\n\n[[ ]][[![|]()]]";
        var expected = "<table>\n<thead>\n<tr>\n<th>x</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>[</td>\n</tr>\n</tbody>\n</table>\n<p><x-wikilink data-target=\" \"> </x-wikilink><x-wikilink data-target=\"![|]()\"><img src=\"\" alt=\"|\"></x-wikilink></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables | MarkdownParserFlags.WikiLinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0048()
    {
        var md = "title\n--\t";
        var expected = "<h2>title</h2>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0049()
    {
        var md = "x <!A>";
        var expected = "<p>x <!A></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0050()
    {
        var md = "__!_!__\n\n__!x!__\n\n**!*!**\n\n---\n\n_*__*_*\n\n_*xx*_*\n\n_*__-_-\n\n_*xx-_-";
        var expected = "<p><strong>!_!</strong></p>\n<p><strong>!x!</strong></p>\n<p><strong>!*!</strong></p>\n<hr />\n<p><em><em>__</em></em>*</p>\n<p><em><em>xx</em></em>*</p>\n<p><em>*__-</em>-</p>\n<p><em>*xx-</em>-</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0051()
    {
        var md = "~foo ~bar baz~";
        var expected = "<p>~foo <del>bar baz</del></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Strikethrough);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0052()
    {
        var md = "`\nfoo`";
        var expected = "<p><code> foo</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0053()
    {
        var md = "`foo\n`";
        var expected = "<p><code>foo </code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0054()
    {
        var md = "`\nfoo\n`";
        var expected = "<p><code>foo</code></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0055()
    {
        var md = "https://example.com/\nhttps://example.com/dir/";
        var expected = "<p><a href=\"https://example.com/\">https://example.com/</a>\n<a href=\"https://example.com/dir/\">https://example.com/dir/</a></p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.PermissiveUrlAutolinks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0056()
    {
        var md = "copy ~user1/file to ~user2/file\n\ncopy \"~user1/file\" to \"~user2/file\"";
        var expected = "<p>copy ~user1/file to ~user2/file</p>\n<p>copy &quot;~user1/file&quot; to &quot;~user2/file&quot;</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Strikethrough);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0057()
    {
        var md = "#\tFoo";
        var expected = "<h1>Foo</h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0058()
    {
        var md = "  Foo *bar\nbaz*\t\n====";
        var expected = "<h1>Foo <em>bar\nbaz</em></h1>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0059()
    {
        var md = "foo  \t\nbar";
        var expected = "<p>foo\nbar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0060()
    {
        var md = "foo\t  \nbar";
        var expected = "<p>foo<br>\nbar</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.None);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

}
