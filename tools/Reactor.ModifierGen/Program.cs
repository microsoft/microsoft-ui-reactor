using Microsoft.UI.Reactor.ModifierGen;

// Regenerates every file ModifierEmitter owns. `--check` reports stale files without writing.
if (args.Any(a => a != "--check"))
{
    Console.Error.WriteLine($"unknown option '{args.First(a => a != "--check")}'. Usage: dotnet run --project tools/Reactor.ModifierGen [-- --check]");
    return 2;
}
var check = args.Contains("--check");
var root = FindRepoRoot(Directory.GetCurrentDirectory())
    ?? FindRepoRoot(AppContext.BaseDirectory)
    ?? throw new InvalidOperationException("Run from inside the microsoft-ui-reactor checkout (no Reactor.slnx found).");

var stale = 0;
foreach (var (relativePath, content) in ModifierEmitter.EmitAll())
{
    var path = Path.Join(root, relativePath);
    var current = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;
    if (current == content)
        continue;

    stale++;
    if (check)
    {
        Console.Error.WriteLine($"stale: {relativePath} (run: dotnet run --project tools/Reactor.ModifierGen)");
        continue;
    }
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
    Console.WriteLine($"wrote: {relativePath}");
}

return check && stale > 0 ? 1 : 0;

static string? FindRepoRoot(string start)
{
    for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Join(dir.FullName, "Reactor.slnx")))
            return dir.FullName;
    return null;
}
