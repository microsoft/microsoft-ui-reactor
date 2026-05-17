namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// <c>mur docs render-diagrams [--topic &lt;id&gt;] [--watch]</c> — fast
/// inner-loop diagram render without running the whole compile pipeline.
/// The <c>--watch</c> flag is reserved (TODO: FileSystemWatcher plumbing).
/// </summary>
internal static class RenderDiagramsCommand
{
    public static int Run(string[] args)
    {
        var topic = GetOption(args, "--topic");
        var watch = HasFlag(args, "--watch");

        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("Error: Could not find repository root (looking for Reactor.slnx or .git).");
            return 1;
        }

        var diagramsRoot = Path.Combine(repoRoot, "docs", "_pipeline", "diagrams");
        var imagesRoot = Path.Combine(repoRoot, "docs", "guide", "images");

        if (watch)
        {
            // TODO(spec-041 phase 1.5): wire FileSystemWatcher so authors can
            // edit a .mmd and see the SVG update without rerunning the
            // command. For now print a clear notice and fall through to a
            // single pass so the flag isn't a silent no-op.
            Console.WriteLine("⚠ --watch is reserved (single-pass render for now; see phase-1-retro).");
        }

        IMermaidRunner mermaid = new MmdcRunner();
        var result = DiagramProcessor.Process(diagramsRoot, imagesRoot, mermaid, topic);

        Console.WriteLine($"Diagrams: {result.CopiedSvgs.Count} copied, {result.SkippedSvgs.Count} skipped (identical), {result.RenderedMermaid.Count} rendered, {result.CachedMermaid.Count} cached.");
        foreach (var f in result.Findings)
            Console.Error.WriteLine(f.Format());

        return result.Findings.Any(f => f.Severity == TierLintSeverity.Error) ? 1 : 0;
    }

    private static string? FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Reactor.slnx")) || Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string? GetOption(string[] args, string name)
    {
        var idx = Array.IndexOf(args, name);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Contains(name, StringComparer.OrdinalIgnoreCase);
}
