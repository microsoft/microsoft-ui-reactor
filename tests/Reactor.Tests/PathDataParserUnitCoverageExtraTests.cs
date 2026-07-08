using System;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Unit coverage for the pure-managed <see cref="PathDataParser.ParseTokens"/> walk
/// (the geometry-free path the SharpFuzz harness drives). The public
/// <see cref="PathDataParser.Parse"/> constructs WinUI geometry and therefore can't
/// run in the headless unit runner (COMException), so these exercise the shared
/// <c>ParseCore</c> tokenizer via the internal <c>ParseTokens</c> seam.
/// </summary>
public class PathDataParserUnitCoverageExtraTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void ParseTokens_BlankInput_ReturnsWithoutWork(string? input)
    {
        // Whitespace/null/empty short-circuits before the parse loop.
        Assert.Null(Record.Exception(() => PathDataParser.ParseTokens(input!)));
    }

    [Fact]
    public void ParseTokens_AllCommands_WalksWithoutThrowing()
    {
        // Every command the D3 PathBuilder can emit: M L h v A Q C Z, plus an
        // unknown char ('W') that must fall through the default skip arm, and a
        // trailing token after Z. Numbers cover sign, decimal, and both e/E
        // scientific-notation forms with signed exponents.
        const string path =
            "M -1.5 +2 L 3.0 4 h 5 v -6 " +
            "A 1 1 0 1 0 7 8 " +
            "Q 1e2 2 3 4 " +
            "C 1 2 3 4 5 6 Z W 9";

        Assert.Null(Record.Exception(() => PathDataParser.ParseTokens(path)));
    }

    [Theory]
    [InlineData("M1,2L3,4Z")]                 // comma separators
    [InlineData("  ,, M 0 0 , L 1 1")]        // leading + interspersed whitespace/commas
    [InlineData("M0 0 h10 v10 h-10 v-10 Z")]  // relative h/v accumulate the current point
    [InlineData("M0 0 C1.5E1 2 3 4 5 6")]     // uppercase E exponent
    [InlineData("M0 0 L1e+1 2e-1")]           // signed exponents
    public void ParseTokens_WellFormedVariants_DoNotThrow(string path)
    {
        Assert.Null(Record.Exception(() => PathDataParser.ParseTokens(path)));
    }

    [Theory]
    [InlineData("M 1.2.3 0")]   // two decimal points -> double.Parse FormatException
    [InlineData("M 0 0 L 5e 0")] // dangling exponent -> FormatException
    public void ParseTokens_MalformedNumber_ThrowsFormatException(string path)
    {
        Assert.Throws<FormatException>(() => PathDataParser.ParseTokens(path));
    }

    [Theory]
    // A malformed token in a command's LATER operand slot must throw — which proves
    // the command consumed the correct number of preceding operands and advanced to
    // that slot. If arity were wrong, the bad token would instead be re-dispatched as
    // an unknown command char and silently skipped (no throw). ParseTokens is void, so
    // this throw-position check is the strongest headless assertion of command semantics.
    [InlineData("M 0 0 L 10 1.2.3")]           // LineTo consumes 2 operands (x y)
    [InlineData("M 0 0 Q 1 2 3 1.2.3")]        // Quadratic consumes 4 (x1 y1 x y)
    [InlineData("M 0 0 C 1 2 3 4 5 1.2.3")]    // Cubic consumes 6 (x1 y1 x2 y2 x y)
    [InlineData("M 0 0 A 1 1 0 1 0 10 1.2.3")] // Arc consumes 7 (rx ry rot large sweep x y)
    public void ParseTokens_MalformedTrailingOperand_ThrowsProvingCommandArity(string path)
    {
        Assert.Throws<FormatException>(() => PathDataParser.ParseTokens(path));
    }

    [Fact]
    public void ParseTokens_UnknownCommandsOnly_AreSkipped()
    {
        // None of these are recognised commands (M/L/h/v/A/Q/C/Z), so every char
        // hits the default skip arm; the loop still terminates and nothing is
        // parsed. Interspersed spaces also exercise the whitespace-skip guard.
        Assert.Null(Record.Exception(() => PathDataParser.ParseTokens("R T P G K X Y N")));
    }

    [Fact]
    public void ParseTokens_ArcFlagsAndRotation_AreConsumed()
    {
        // Arc has the widest token list (rx ry rotation large-arc sweep x y);
        // exercise both flag values so the large-arc/sweep reads run.
        Assert.Null(Record.Exception(() => PathDataParser.ParseTokens("M0 0 A 5 5 45 1 0 10 10")));
        Assert.Null(Record.Exception(() => PathDataParser.ParseTokens("M0 0 A 5 5 0 0 1 10 10")));
    }
}
