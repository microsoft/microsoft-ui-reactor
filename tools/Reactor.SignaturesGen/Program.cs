// Reactor.SignaturesGen — apphost that writes skills/reactor.api.txt by reflecting
// over the built Reactor.dll. The index text itself is built by
// Microsoft.UI.Reactor.ApiIndex.ApiIndexGenerator (in the sibling classlib), so the
// same generation logic can be driven in-process from xUnit on ARM64 where this
// apphost crashes. This shell just parses the repo root and writes the two copies.
//
// Usage:
//   dotnet run --project tools/Reactor.SignaturesGen -- <repo-root>
//   (build target in csproj passes the repo root automatically)

using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.ApiIndex;

namespace Microsoft.UI.Reactor.SignaturesGen;

internal static class Program
{
    // This apphost's whole reason to exist is to invoke the reflection-based
    // ApiIndexGenerator.Generate (annotated [RequiresUnreferencedCode]) at build time. The
    // normal build runs it as a plain, host-arch-matching apphost (not trimmed); it is only
    // AOT-published via the opt-in ReactorApiAot proof (see the csproj), which roots the whole
    // Reactor assembly. Either way the IL2026 from that call is expected and acknowledged here
    // rather than blanket-disabling the analyzer for the project. (No IL3050: Generate does
    // metadata reflection + reflection-invoke only, no runtime codegen.)
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Build-time-only apphost that intentionally drives ApiIndexGenerator's full-surface reflection over Reactor.dll; not trimmed, and only AOT-published via the opt-in ReactorApiAot proof that roots the assembly.")]
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: Reactor.SignaturesGen <repo-root>");
            return 1;
        }

        var repoRoot = Path.GetFullPath(args[0]);

        // Write to both the legacy path (consumed by `mur --api` embedding and the
        // `agentkit/` NuGet layout) and the plugin-format path (consumed by the
        // `reactor-dsl` skill's `references/`). One generation source of truth —
        // keeps the two committed copies from drifting.
        var outputPaths = new[]
        {
            Path.Combine(repoRoot, "skills", "reactor.api.txt"),
            Path.Combine(repoRoot, "plugins", "reactor", "skills", "reactor-dsl", "references", "reactor.api.txt"),
        };

        var content = ApiIndexGenerator.Generate(typeof(Microsoft.UI.Reactor.Factories).Assembly);

        foreach (var outputPath in outputPaths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            // Skip rewriting if unchanged — keeps file mtimes stable for incremental builds.
            if (File.Exists(outputPath) && File.ReadAllText(outputPath) == content)
            {
                Console.WriteLine($"reactor.api.txt unchanged ({outputPath})");
                continue;
            }

            File.WriteAllText(outputPath, content);
            Console.WriteLine($"wrote {outputPath} ({content.Length} bytes)");
        }

        return 0;
    }
}
