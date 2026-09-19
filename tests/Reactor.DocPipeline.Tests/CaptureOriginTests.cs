using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Covers <see cref="ScreenshotCapture.BuildCaptureOriginArgs"/>, which turns the
/// <c>REACTOR_DOCS_CAPTURE_ORIGIN</c> environment variable into <c>--x</c>/<c>--y</c>
/// arguments for a doc app's capture window.
/// </summary>
/// <remarks>
/// This exists so contributors whose primary display is not at the repo's documented
/// capture scale can still regenerate screenshots: capture is <c>PrintWindow</c> over
/// the live window in physical pixels, so the captured size follows the DPI of
/// whichever monitor the window lands on.
/// </remarks>
public class CaptureOriginTests
{
    [Fact]
    public void Unset_ProducesNoArguments()
    {
        Assert.Equal(string.Empty, ScreenshotCapture.BuildCaptureOriginArgs(null));
        Assert.Equal(string.Empty, ScreenshotCapture.BuildCaptureOriginArgs(""));
        Assert.Equal(string.Empty, ScreenshotCapture.BuildCaptureOriginArgs("   "));
    }

    [Fact]
    public void WellFormedOrigin_ProducesBothFlags()
    {
        Assert.Equal(" --x 2600 --y 0", ScreenshotCapture.BuildCaptureOriginArgs("2600,0"));
    }

    [Fact]
    public void SurroundingWhitespace_IsTolerated()
    {
        Assert.Equal(" --x 2600 --y 120", ScreenshotCapture.BuildCaptureOriginArgs(" 2600 , 120 "));
    }

    // A monitor left of or above the primary has a negative origin, and 0 is the
    // primary's own origin. Rejecting either would make the whole feature useless
    // for exactly the multi-monitor layouts it exists to serve.
    [Theory]
    [InlineData("-2560,0", " --x -2560 --y 0")]
    [InlineData("0,-1080", " --x 0 --y -1080")]
    [InlineData("0,0", " --x 0 --y 0")]
    public void NegativeAndZeroCoordinates_AreAccepted(string raw, string expected)
    {
        Assert.Equal(expected, ScreenshotCapture.BuildCaptureOriginArgs(raw));
    }

    // Degrading to OS placement beats aborting a 200-screenshot run over a typo.
    // The failure is self-announcing anyway: the images come out at the wrong scale.
    [Theory]
    [InlineData("2600")]
    [InlineData("2600,0,5")]
    [InlineData("left,0")]
    [InlineData("2600,down")]
    [InlineData("NaN,0")]
    [InlineData("0,Infinity")]
    [InlineData(",")]
    public void MalformedOrigin_FallsBackToOsPlacement(string raw)
    {
        Assert.Equal(string.Empty, ScreenshotCapture.BuildCaptureOriginArgs(raw));
    }

    // The flags are machine-facing. Under a comma-decimal culture a naive
    // double.ToString() would emit "2600,5", which the receiving parser would then
    // read as a different number — or, worse, split the pair on the wrong comma.
    [CulturedFact(new[] { "nl-NL" })]
    public void Formatting_IsInvariantOfCurrentCulture()
    {
        Assert.Equal(" --x 2600.5 --y 120.25", ScreenshotCapture.BuildCaptureOriginArgs("2600.5,120.25"));
    }
}
