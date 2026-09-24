// Command-line surface for the search-index generator, factored out of Program.cs so the
// exit-code / arg-validation / error-handling contract is unit-testable (SearchIndexCliTests
// passes a StringWriter, so the tests never touch the real console).

using System.Text;
using System.Text.Json;

namespace Microsoft.UI.Reactor.SearchIndex;

/// <summary>
/// Usage: <c>reactor-search-index [--check] [--agent-kit=&lt;dir&gt;] [galleryDir] [editorialPath] [outPath]</c>.
/// With no args it regenerates the committed index off the repo root; <c>--check</c> compares
/// instead of writing. Exit codes: 0 = success / up-to-date, 1 = stale (<c>--check</c> only),
/// 2 = usage error or generation failure.
/// </summary>
/// <remarks>
/// <c>--agent-kit</c> points at the directory holding <c>SKILL.md</c> / <c>plugins/</c> /
/// <c>skills/</c>, whose <c>&lt;!-- index:id --&gt;</c> blocks become each control's
/// <c>details</c>. It defaults to the repo root **that contains <c>galleryDir</c>**, so passing
/// the real paths explicitly produces the same index as passing none. A synthetic gallery in a
/// temp directory has no <c>Reactor.slnx</c> above it and therefore contributes no details;
/// <c>--no-agent-kit</c> forces that off for a real gallery.
/// </remarks>
public static class SearchIndexCli
{
    // The index is a byte-stable artifact fetched raw by the winui-search CLI, so write it as
    // UTF-8 without a BOM and compare bytes (not decoded text, which would hide a stray BOM).
    static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    const string AgentKitOption = "--agent-kit=";
    const string NoAgentKitOption = "--no-agent-kit";

    public static int Run(string[] args) => Run(args, Console.Error);

    public static int Run(string[] args, TextWriter log)
    {
        try
        {
            var check = false;
            var noAgentKit = false;
            string? agentKitArg = null;
            var positional = new List<string>();
            foreach (var arg in args)
            {
                if (arg == "--check") check = true;
                else if (arg == NoAgentKitOption) noAgentKit = true;
                else if (arg.StartsWith(AgentKitOption, StringComparison.Ordinal))
                {
                    var value = arg[AgentKitOption.Length..];
                    if (value.Length == 0) return Usage(log, "--agent-kit= requires a directory");
                    agentKitArg = Path.GetFullPath(value);
                    // An INFERRED root is allowed to be absent — that is how a synthetic gallery
                    // opts out. An EXPLICIT one that is not a directory is a typo, and Generate
                    // would quietly treat it as "no markers" and write a details-free index.
                    if (File.Exists(agentKitArg))
                        return Usage(log, $"--agent-kit must be a directory, not a file: {agentKitArg}");
                    if (!Directory.Exists(agentKitArg))
                        return Usage(log, $"--agent-kit directory does not exist: {agentKitArg}");
                }
                else if (arg.StartsWith("--", StringComparison.Ordinal)) return Usage(log, $"unknown option '{arg}'");
                else positional.Add(arg);
            }
            if (positional.Count > 3)
                return Usage(log, $"too many arguments ({positional.Count}); expected at most [galleryDir] [editorialPath] [outPath]");
            if (noAgentKit && agentKitArg is not null)
                return Usage(log, "--no-agent-kit and --agent-kit= are mutually exclusive");

            // Canonicalize explicit paths (resolves '..'); defaults derive from the repo root.
            string? repoRoot = null;
            string RepoRoot() => repoRoot ??= FindRepoRoot();

            var galleryDir = positional.Count >= 1
                ? Path.GetFullPath(positional[0])
                : Path.Join(RepoRoot(), "samples", "ReactorGallery");
            var editorialPath = positional.Count >= 2
                ? Path.GetFullPath(positional[1])
                : Path.Join(RepoRoot(), "tools", "Reactor.SearchIndex", "editorial.json");
            var outPath = positional.Count >= 3
                ? Path.GetFullPath(positional[2])
                : Path.Join(galleryDir, "reactor-search-index.json");

            // Resolve the agent kit from the GALLERY, not from the argument count: passing the
            // real paths explicitly must produce the same index as passing none, or a contributor
            // silently regenerates a details-free file. A synthetic gallery in a temp directory
            // has no Reactor.slnx above it, so it still opts out — without needing a flag.
            var agentKitRoot = noAgentKit ? null : agentKitArg ?? TryFindRepoRootFrom(galleryDir);

            var result = SearchIndexGenerator.Generate(galleryDir, editorialPath, agentKitRoot);
            log.WriteLine($"[search-index] {result.ControlCount} controls, {result.Skipped.Count} skipped.");
            foreach (var s in result.Skipped)
                log.WriteLine($"[search-index]   skip {s.Id} — {s.Reason}");

            var generatedBytes = Utf8NoBom.GetBytes(result.Json);
            if (check)
            {
                var currentBytes = File.Exists(outPath) ? File.ReadAllBytes(outPath) : Array.Empty<byte>();
                if (generatedBytes.AsSpan().SequenceEqual(currentBytes))
                {
                    log.WriteLine($"[search-index] up to date: {outPath}");
                    return 0;
                }
                log.WriteLine($"[search-index] STALE: {outPath} — run `dotnet run --project tools/Reactor.SearchIndex` to regenerate.");
                return 1;
            }

            File.WriteAllText(outPath, result.Json, Utf8NoBom);
            log.WriteLine($"[search-index] wrote {outPath}");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException
            or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException)
        {
            log.WriteLine("[search-index] ERROR: " + ex.Message);
            return 2;
        }
    }

    static int Usage(TextWriter log, string problem)
    {
        log.WriteLine("[search-index] ERROR: " + problem);
        log.WriteLine("usage: reactor-search-index [--check] [--agent-kit=<dir>|--no-agent-kit] [galleryDir] [editorialPath] [outPath]");
        log.WriteLine("  --check          compare instead of writing; exit 1 when the committed index is stale");
        log.WriteLine("  --agent-kit=<d>  directory holding SKILL.md / plugins/ / skills/, whose <!-- index:id -->");
        log.WriteLine("                   blocks become each entry's `details`. Defaults to the repo root that");
        log.WriteLine("                   contains galleryDir; a gallery outside a repo contributes no details.");
        log.WriteLine("  --no-agent-kit   skip marker extraction entirely");
        return 2;
    }

    static string FindRepoRoot() =>
        TryFindRepoRootFrom(AppContext.BaseDirectory)
        ?? throw new DirectoryNotFoundException("Could not locate repo root (Reactor.slnx) from " + AppContext.BaseDirectory);

    /// <summary>Nearest ancestor of <paramref name="start"/> containing Reactor.slnx, or null.</summary>
    static string? TryFindRepoRootFrom(string start)
    {
        var dir = start;
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir, "Reactor.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
