// Command-line surface for the search-index generator, factored out of Program.cs so the
// exit-code / arg-validation / error-handling contract is unit-testable (SearchIndexCliTests
// passes a StringWriter, so the tests never touch the real console).

using System.Text;
using System.Text.Json;

namespace Microsoft.UI.Reactor.SearchIndex;

/// <summary>
/// Usage: <c>reactor-search-index [--check] [galleryDir] [editorialPath] [outPath]</c>.
/// With no args it regenerates the committed index off the repo root; <c>--check</c> compares
/// instead of writing. Exit codes: 0 = success / up-to-date, 1 = stale (<c>--check</c> only),
/// 2 = usage error or generation failure.
/// </summary>
public static class SearchIndexCli
{
    // The index is a byte-stable artifact fetched raw by the winui-search CLI, so write it as
    // UTF-8 without a BOM and compare bytes (not decoded text, which would hide a stray BOM).
    static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static int Run(string[] args) => Run(args, Console.Error);

    public static int Run(string[] args, TextWriter log)
    {
        try
        {
            var check = false;
            var positional = new List<string>();
            foreach (var arg in args)
            {
                if (arg == "--check") check = true;
                else if (arg.StartsWith("--", StringComparison.Ordinal)) return Usage(log, $"unknown option '{arg}'");
                else positional.Add(arg);
            }
            if (positional.Count > 3)
                return Usage(log, $"too many arguments ({positional.Count}); expected at most [galleryDir] [editorialPath] [outPath]");

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

            var result = SearchIndexGenerator.Generate(galleryDir, editorialPath);
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
            or UnauthorizedAccessException or ArgumentException or DirectoryNotFoundException)
        {
            log.WriteLine("[search-index] ERROR: " + ex.Message);
            return 2;
        }
    }

    static int Usage(TextWriter log, string problem)
    {
        log.WriteLine("[search-index] ERROR: " + problem);
        log.WriteLine("usage: reactor-search-index [--check] [galleryDir] [editorialPath] [outPath]");
        return 2;
    }

    static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Join(dir, "Reactor.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Could not locate repo root (Reactor.slnx) from " + AppContext.BaseDirectory);
    }
}
