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
        // Coverage pass: exercises every command arm in one walk — M, Z, L, relative
        // h/v, the unknown-char default skip ('W'), A, Q, C — plus signed, decimal and
        // scientific (1e+2) number reads and each completed command's post-read
        // current-point update. The ASSERTION is a real oracle for the final step only:
        // a correct walk reaches the trailing malformed C operand and throws, whereas a
        // no-op parser (or a deleted final C arm) would char-skip to the end and never
        // throw. Per-command arity is verified independently by
        // ParseTokens_MalformedTrailingOperand below; this test does not claim to verify
        // the intermediate arms it merely exercises.
        const string path =
            "M -1.5 +2 Z L 3.0 4 h 5 v -6 W " +
            "A 1 1 0 1 0 7 8 " +
            "Q 1e+2 2 3 4 " +
            "C 1 2 3 4 5 1.2.3";

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
    public void ParseTokens_SkipsUnknownCommandsAndReachesLaterCommand()
    {
        // The parser must advance past every unknown char via the default skip arm and
        // still reach the trailing M command, whose malformed operand throws. An
        // implementation that stopped or looped on the first unknown ('R') would never
        // reach it — so this is a real oracle for "unknown chars are skipped, not fatal".
        Assert.Throws<FormatException>(() => PathDataParser.ParseTokens("R T P G K X Y M 1.2.3"));
    }
}
