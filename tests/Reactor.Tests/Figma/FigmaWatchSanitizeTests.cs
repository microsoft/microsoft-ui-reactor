using Microsoft.UI.Reactor.Cli.Figma;
using Xunit;

namespace Reactor.Tests.Figma;

public class FigmaWatchSanitizeTests
{
    [Theory]
    [InlineData("Normal File Name", "Normal File Name")]
    [InlineData("Has\ttab", "Has\ttab")] // tabs preserved
    [InlineData("Has\nnewline", "Has\nnewline")] // newlines preserved
    [InlineData("Clean", "Clean")]
    [InlineData("", "")]
    public void SanitizeForStderr_StripsControlChars(string input, string expected)
    {
        var result = FigmaWatchCommand.SanitizeForStderr(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeForStderr_StripsEscSequence()
    {
        // ESC (U+001B) should be stripped
        var input = "Has" + "\u001b" + "[31mANSI" + "\u001b" + "[0m";
        var result = FigmaWatchCommand.SanitizeForStderr(input);
        Assert.Equal("Has[31mANSI[0m", result);
    }

    [Fact]
    public void SanitizeForStderr_StripsNullAndBell()
    {
        Assert.Equal("Nullchar", FigmaWatchCommand.SanitizeForStderr("Null\0char"));
        Assert.Equal("Bellchar", FigmaWatchCommand.SanitizeForStderr("Bell\achar"));
    }

    [Fact]
    public void SanitizeForStderr_NullInput_ReturnsNull()
    {
        Assert.Null(FigmaWatchCommand.SanitizeForStderr(null!));
    }
}
