using Microsoft.UI.Reactor.Markdown;
using System.Text;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Md4cGenerated;

/// <summary>
/// Ported from md4c/test/spec-latex-math.txt
/// </summary>
public class Md4cLatexMathTest
{
    [Fact]
    public void Example_0001()
    {
        var md = "$a+b=c$ Hello, world!";
        var expected = "<p><x-equation>a+b=c</x-equation> Hello, world!</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.LatexMathSpans);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0002()
    {
        var md = "$$foo $bar$ baz$$";
        var expected = "<p>$$foo <x-equation>bar</x-equation> baz$$</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.LatexMathSpans);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0003()
    {
        var md = "x$a+b=c$";
        var expected = "<p>x$a+b=c$</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.LatexMathSpans);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0004()
    {
        var md = "$a+b=c$x";
        var expected = "<p>$a+b=c$x</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.LatexMathSpans);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0005()
    {
        var md = "This is a display equation: $$\\int_a^b x dx$$.";
        var expected = "<p>This is a display equation: <x-equation type=\"display\">\\int_a^b x dx</x-equation>.</p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.LatexMathSpans);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0006()
    {
        var md = "$$\n\\int_a^b\nf(x) dx\n$$";
        var expected = "<p><x-equation type=\"display\"> \\int_a^b f(x) dx </x-equation></p>\n";
        var actual = Md4cTestHelper.SpecToHtml(md, MarkdownParserFlags.LatexMathSpans);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

}
