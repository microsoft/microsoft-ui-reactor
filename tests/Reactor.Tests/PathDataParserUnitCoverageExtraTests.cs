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
        // Whitespace/null/empty short-circuits before the parse loop. The null case
        // is the real oracle here: without the IsNullOrWhiteSpace guard, null would
        // dereference pathData.Length and throw NullReferenceException.
        Assert.Null(Record.Exception(() => PathDataParser.ParseTokens(input!)));
    }

    [Fact]
    public void ParseTokens_ValidChainReachesTrailingMalformed_Throws()
    {
        // Every command completes and updates the current point (covering the
        // post-read "cx=…; break;" arms), so the walk reaches the trailing malformed
        // operand of the final C command and throws. Commands exercised in one pass:
        // M, Z, L, relative h/v, the unknown-char default skip ('W'), A (widest token
        // list incl. flags), Q, C. Numbers cover sign, decimal, and e/E scientific
        // notation. This is a real oracle, not a smoke test: a no-op parser — or a
        // deleted final command arm — would never reach/throw on the trailing "1.2.3".
        const string path =
            "M -1.5 +2 Z L 3.0 4 h 5 v -6 W " +
            "A 1 1 0 1 0 7 8 " +
            "Q 1e2 2 3 4 " +
            "C 1 2 3 4 5 1.2.3";

        Assert.Throws<FormatException>(() => PathDataParser.ParseTokens(path));
    }

    [Theory]
    // Each input feeds a well-formed number (exercising separators and the ReadNumber
    // branches — comma skips, uppercase E, signed exponents, leading sign) followed by
    // a malformed final operand, so a correct read advances to it and throws. Broken
    // separator/exponent handling would misalign the walk and the trailing "1.2.3"
    // would not land as the throwing operand.
    [InlineData("M1,2 L3,1.2.3")]      // comma separators between operands
    [InlineData("M0 0 L1e+1 1.2.3")]   // positive signed exponent
    [InlineData("M0 0 L2e-1 1.2.3")]   // negative signed exponent
    [InlineData("M0 0 L1.5E1 1.2.3")]  // uppercase E exponent
    [InlineData("M0 0 L-5 1.2.3")]     // leading sign
    public void ParseTokens_ConsumesSeparatorsAndNumberFormats_ThenThrows(string path)
    {
        Assert.Throws<FormatException>(() => PathDataParser.ParseTokens(path));
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
    [InlineData("M 1 1.2.3")]                  // MoveTo consumes 2 operands (x y)
    [InlineData("M 0 0 L 10 1.2.3")]           // LineTo consumes 2 (x y)
    [InlineData("M 0 0 h 1.2.3")]              // horizontal-relative consumes 1 (dx)
    [InlineData("M 0 0 v 1.2.3")]              // vertical-relative consumes 1 (dy)
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
        // hits the default skip arm; the loop still terminates. This is a real oracle:
        // if the default arm stopped advancing the index the walk would hang, not pass.
        Assert.Null(Record.Exception(() => PathDataParser.ParseTokens("R T P G K X Y N")));
    }
}
