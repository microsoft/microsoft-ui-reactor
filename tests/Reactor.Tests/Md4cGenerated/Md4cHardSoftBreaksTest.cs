using Microsoft.UI.Reactor.Markdown;
using System.Text;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Md4cGenerated;

/// <summary>
/// Ported from md4c/test/spec-hard-soft-breaks.txt
/// </summary>
public class Md4cHardSoftBreaksTest
{
    [Fact]
    public void Example_0001()
    {
        var md = "foo\nbaz";
        var expected = "<p>foo<br>\nbaz</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.HardSoftBreaks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0002()
    {
        var md = "A quote from the CommonMark Spec below:\n\nA renderer may also provide an option to \nrender soft line breaks as hard line breaks.";
        var expected = "<p>A quote from the CommonMark Spec below:</p>\n<p>A renderer may also provide an option to<br>\nrender soft line breaks as hard line breaks.</p>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.HardSoftBreaks);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

}
