using Microsoft.UI.Reactor.Markdown;
using System.Text;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Md4cGenerated;

/// <summary>
/// Ported from md4c/test/spec-tables.txt
/// </summary>
public class Md4cTablesTest
{
    [Fact]
    public void Example_0001()
    {
        var md = "| Column 1 | Column 2 |\n|----------|----------|\n| foo      | bar      |\n| baz      | qux      |\n| quux     | quuz     |";
        var expected = "<table>\n<thead>\n<tr><th>Column 1</th><th>Column 2</th></tr>\n</thead>\n<tbody>\n<tr><td>foo</td><td>bar</td></tr>\n<tr><td>baz</td><td>qux</td></tr>\n<tr><td>quux</td><td>quuz</td></tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0002()
    {
        var md = "Column 1 | Column 2 |\n---------|--------- |\nfoo      | bar      |\nbaz      | qux      |\nquux     | quuz     |";
        var expected = "<table>\n<thead>\n<tr><th>Column 1</th><th>Column 2</th></tr>\n</thead>\n<tbody>\n<tr><td>foo</td><td>bar</td></tr>\n<tr><td>baz</td><td>qux</td></tr>\n<tr><td>quux</td><td>quuz</td></tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0003()
    {
        var md = "| Column 1 | Column 2\n|----------|---------\n| foo      | bar\n| baz      | qux\n| quux     | quuz";
        var expected = "<table>\n<thead>\n<tr><th>Column 1</th><th>Column 2</th></tr>\n</thead>\n<tbody>\n<tr><td>foo</td><td>bar</td></tr>\n<tr><td>baz</td><td>qux</td></tr>\n<tr><td>quux</td><td>quuz</td></tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0004()
    {
        var md = "Column 1 | Column 2\n---------|---------\nfoo      | bar\nbaz      | qux\nquux     | quuz";
        var expected = "<table>\n<thead>\n<tr><th>Column 1</th><th>Column 2</th></tr>\n</thead>\n<tbody>\n<tr><td>foo</td><td>bar</td></tr>\n<tr><td>baz</td><td>qux</td></tr>\n<tr><td>quux</td><td>quuz</td></tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0005()
    {
        var md = "Column 1\n--------\nfoo\nbaz\nquux";
        var expected = "<h2>Column 1</h2>\n<p>foo\nbaz\nquux</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0006()
    {
        var md = "Column 1 |Column 2\n---|---\nfoo | bar\nbaz| qux\nquux|quuz";
        var expected = "<table>\n<thead>\n<tr><th>Column 1</th><th>Column 2</th></tr>\n</thead>\n<tbody>\n<tr><td>foo</td><td>bar</td></tr>\n<tr><td>baz</td><td>qux</td></tr>\n<tr><td>quux</td><td>quuz</td></tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0007()
    {
        var md = "Lorem ipsum dolor sit amet.\n| Column 1 | Column 2\n| ---------|---------\n| foo      | bar\n| baz      | qux\n| quux     | quuz";
        var expected = "<p>Lorem ipsum dolor sit amet.\n| Column 1 | Column 2\n| ---------|---------\n| foo      | bar\n| baz      | qux\n| quux     | quuz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0008()
    {
        var md = "Column 1 | Column 2\n---------|---------\nfoo      | bar\nbaz      | qux\nquux     | quuz\nLorem ipsum dolor sit amet.";
        var expected = "<table>\n<thead>\n<tr><th>Column 1</th><th>Column 2</th></tr>\n</thead>\n<tbody>\n<tr><td>foo</td><td>bar</td></tr>\n<tr><td>baz</td><td>qux</td></tr>\n<tr><td>quux</td><td>quuz</td></tr>\n<tr><td>Lorem ipsum dolor sit amet.</td><td></td></tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0009()
    {
        var md = "| Column 1 | Column 2 | Column 3 | Column 4 |\n|----------|:---------|:--------:|---------:|\n| default  | left     | center   | right    |";
        var expected = "<table>\n<thead>\n<tr><th>Column 1</th><th align=\"left\">Column 2</th><th align=\"center\">Column 3</th><th align=\"right\">Column 4</th></tr>\n</thead>\n<tbody>\n<tr><td>default</td><td align=\"left\">left</td><td align=\"center\">center</td><td align=\"right\">right</td></tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0010()
    {
        var md = "Column 1 | Column 2\n---------|---------\nfoo      | bar\nbaz      | qux \\| xyzzy\nquux     | quuz";
        var expected = "<table>\n<thead>\n<tr><th>Column 1</th><th>Column 2</th></tr>\n</thead>\n<tbody>\n<tr><td>foo</td><td>bar</td></tr>\n<tr><td>baz</td><td>qux | xyzzy</td></tr>\n<tr><td>quux</td><td>quuz</td></tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0011()
    {
        var md = "Column 1 | Column 2\n---------|---------\n*foo*    | bar\n**baz**  | [qux]\nquux     | [quuz](/url2)\n\n[qux]: /url";
        var expected = "<table>\n<thead>\n<tr><th>Column 1</th><th>Column 2</th></tr>\n</thead>\n<tbody>\n<tr><td><em>foo</em></td><td>bar</td></tr>\n<tr><td><strong>baz</strong></td><td><a href=\"/url\">qux</a></td></tr>\n<tr><td>quux</td><td><a href=\"/url2\">quuz</a></td></tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0012()
    {
        var md = "Column 1 | Column 2\n---------|---------\n`foo     | bar`\nbaz      | qux\nquux     | quuz";
        var expected = "<table>\n<thead>\n<tr><th>Column 1</th><th>Column 2</th></tr>\n</thead>\n<tbody>\n<tr><td><code>foo     | bar</code></td><td></td></tr>\n<tr><td>baz</td><td>qux</td></tr>\n<tr><td>quux</td><td>quuz</td></tr>\n</tbody>\n</table>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.Tables);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

}
