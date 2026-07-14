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
    // ApiIndexGenerator.Generate (annotated [RequiresUnreferencedCode]/[RequiresDynamicCode])
    // at build time. It is never trimmed or AOT-published — the build only runs it as a plain
    // apphost whose arch matches the host — so the IL2026/IL3050 from that call are expected and
    // acknowledged here rather than blanket-disabling the analyzer for the project.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Build-time-only apphost that intentionally drives ApiIndexGenerator's full-surface reflection over Reactor.dll; never trimmed or AOT-published.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Build-time-only apphost that intentionally drives ApiIndexGenerator's full-surface reflection over Reactor.dll; never trimmed or AOT-published.")]
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
