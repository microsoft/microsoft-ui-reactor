// SharpFuzz harness entry point. The argv[0] target selector lets a single
// Reactor.Fuzz.exe binary host multiple libFuzzer targets, matching the
// OneFuzzConfig.json "CliArgs" wiring.
//
// `smoke` is a fuzz-free fast path used by the CI gate: walk the seed corpus
// once through both parsers and exit non-zero on any uncaught exception. This
// guards against harness rot (parser API changes, seed-corpus regressions)
// without needing libfuzzer-dotnet installed in CI.

using Microsoft.UI.Reactor.Fuzz;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: Reactor.Fuzz <markdown|pathdata|smoke>");
    return 2;
}

switch (args[0])
{
    case "markdown":
        MarkdownFuzzHarness.Run();
        return 0;
    case "pathdata":
        PathDataFuzzHarness.Run();
        return 0;
    case "smoke":
        return SmokeRunner.Run();
    default:
        Console.Error.WriteLine($"Unknown fuzz target: {args[0]}");
        return 2;
}
