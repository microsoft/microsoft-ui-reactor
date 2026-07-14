// Regeneration / staleness CLI for the ReactorGallery search index.
//
//   dotnet run --project tools/Reactor.SearchIndex            # rewrite the committed file
//   dotnet run --project tools/Reactor.SearchIndex -- --check # exit 1 if it is stale
//
// Paths default off the repo root (located by walking up for Reactor.slnx); the gate
// test drives SearchIndexGenerator.Generate(...) in-process instead of shelling out.

using Microsoft.UI.Reactor.SearchIndex;

var check = args.Contains("--check");
var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);

var galleryDir = positional.Length > 0
    ? positional[0]
    : Path.Combine(repoRoot, "samples", "ReactorGallery");
var editorialPath = positional.Length > 1
    ? positional[1]
    : Path.Combine(repoRoot, "tools", "Reactor.SearchIndex", "editorial.json");
var outPath = positional.Length > 2
    ? positional[2]
    : Path.Combine(galleryDir, "reactor-search-index.json");

SearchIndexResult result;
try
{
    result = SearchIndexGenerator.Generate(galleryDir, editorialPath);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine("[search-index] ERROR: " + ex.Message);
    return 2;
}

Console.Error.WriteLine($"[search-index] {result.ControlCount} controls, {result.Skipped.Count} skipped.");
foreach (var s in result.Skipped)
    Console.Error.WriteLine($"[search-index]   skip {s.Id} — {s.Reason}");

if (check)
{
    var current = File.Exists(outPath) ? File.ReadAllText(outPath) : "";
    if (current == result.Json)
    {
        Console.Error.WriteLine($"[search-index] up to date: {outPath}");
        return 0;
    }
    Console.Error.WriteLine($"[search-index] STALE: {outPath} — run `dotnet run --project tools/Reactor.SearchIndex` to regenerate.");
    return 1;
}

File.WriteAllText(outPath, result.Json);
Console.Error.WriteLine($"[search-index] wrote {outPath}");
return 0;

static string FindRepoRoot(string start)
{
    var dir = start;
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir, "Reactor.slnx")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    throw new DirectoryNotFoundException("Could not locate repo root (Reactor.slnx) from " + start);
}
