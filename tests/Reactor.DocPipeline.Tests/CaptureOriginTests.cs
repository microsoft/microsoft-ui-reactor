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
    // Finite but unrepresentable: the downstream DIP→physical conversion overflows
    // and the window is flung to the edge of the coordinate space. The env-var path
    // must reject these exactly as the CLI parser does, or the two disagree about
    // what a valid origin is.
    [InlineData("1e308,0")]
    [InlineData("0,-1e308")]
    [InlineData("70000,0")]
    public void MalformedOrigin_FallsBackToOsPlacement(string raw)
    {
        Assert.Equal(string.Empty, ScreenshotCapture.BuildCaptureOriginArgs(raw));
    }

    [Theory]
    [InlineData("65536,0", " --x 65536 --y 0")]
    [InlineData("7680,-1440", " --x 7680 --y -1440")]
    public void OriginsWithinRepresentableRange_AreAccepted(string raw, string expected)
    {
        Assert.Equal(expected, ScreenshotCapture.BuildCaptureOriginArgs(raw));
    }

    // The flags are machine-facing. Under a comma-decimal culture a naive
    // double.ToString() would emit "2600,5", which the receiving parser would then
    // read as a different number — or, worse, split the pair on the wrong comma.
    [CulturedFact(new[] { "nl-NL" })]
    public void Formatting_IsInvariantOfCurrentCulture()
    {
        Assert.Equal(" --x 2600.5 --y 120.25", ScreenshotCapture.BuildCaptureOriginArgs("2600.5,120.25"));
    }

    // The assembled command line is the single carrier of every doc screenshot's
    // size: the doc apps no longer declare one, so if --width/--height stopped
    // being appended here every image in the repo would silently resize with
    // nothing failing. Asserting on the pieces is not enough — this pins the seam.
    [Fact]
    public void PreviewArguments_ForwardBothManifestDimensions()
    {
        var args = ScreenshotCapture.BuildPreviewArguments(
            @"C:\docs\_pipeline\apps\getting-started\getting-started.csproj",
            "x64", width: 600, height: 400, captureOrigin: null);

        Assert.Contains("--width 600", args);
        Assert.Contains("--height 400", args);
        // The devtools verb must survive too, or the app starts as a normal run
        // with no capture server and the harness times out waiting for a port.
        Assert.Contains("--preview", args);
        Assert.Contains("--vscode", args);
        Assert.Contains(@"--project ""C:\docs\_pipeline\apps\getting-started\getting-started.csproj""", args);
        Assert.Contains("-p:Platform=x64", args);
        // No origin requested: the window must be left to OS placement.
        Assert.DoesNotContain("--x ", args);
        Assert.DoesNotContain("--y ", args);
    }

    [Fact]
    public void PreviewArguments_AppendOriginWhenRequested()
    {
        var args = ScreenshotCapture.BuildPreviewArguments(
            @"C:\app\app.csproj", "ARM64", width: 520, height: 360, captureOrigin: "2600,0");

        Assert.Contains("--width 520", args);
        Assert.Contains("--height 360", args);
        Assert.Contains("--x 2600", args);
        Assert.Contains("--y 0", args);
        Assert.Contains("-p:Platform=ARM64", args);
    }

    // A malformed origin must not take the size arguments down with it — the
    // capture should still be correctly sized, just OS-placed.
    [Fact]
    public void PreviewArguments_MalformedOriginStillForwardsSize()
    {
        var args = ScreenshotCapture.BuildPreviewArguments(
            @"C:\app\app.csproj", "x64", width: 800, height: 600, captureOrigin: "garbage");

        Assert.Contains("--width 800", args);
        Assert.Contains("--height 600", args);
        Assert.DoesNotContain("--x ", args);
    }

    // Same invariant-culture hazard as the origin, on the path that carries every
    // screenshot's size.
    [CulturedFact(new[] { "nl-NL" })]
    public void PreviewArguments_FormatSizeInvariantly()
    {
        var args = ScreenshotCapture.BuildPreviewArguments(
            @"C:\app\app.csproj", "x64", width: 1200, height: 900, captureOrigin: null);

        Assert.Contains("--width 1200", args);
        Assert.Contains("--height 900", args);
        // A culture-sensitive int.ToString("N0")-style path would emit "1.200".
        Assert.DoesNotContain("1.200", args);
    }
}
