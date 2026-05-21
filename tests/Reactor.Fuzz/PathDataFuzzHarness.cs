using System.Text;
using Microsoft.UI.Reactor.Charting;
using SharpFuzz;

namespace Microsoft.UI.Reactor.Fuzz;

/// <summary>
/// libFuzzer-driven harness over <see cref="PathDataParser.ParseTokens"/>,
/// the WinUI-free parse loop that mirrors <see cref="PathDataParser.Parse"/>
/// minus the PathGeometry / PathFigure / segment construction. The production
/// Parse cannot run in a console process because PathGeometry requires XAML
/// activation, so the harness exercises the equivalent token walker. Both
/// methods share the same number / whitespace / command-dispatch code path,
/// so a crash here implies a crash in production Parse.
/// </summary>
internal static class PathDataFuzzHarness
{
    public static void Run()
    {
        Fuzzer.OutOfProcess.Run(stream =>
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            // Path data is ASCII per the SVG spec, but the parser walks UTF-16
            // chars and only matches against ASCII command letters / digits /
            // punctuation. Decoding the raw bytes as UTF-8 (with replacement)
            // lets the fuzzer drive every byte sequence into the loop.
            string pathData = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);

            PathDataParser.ParseTokens(pathData);
        });
    }
}
