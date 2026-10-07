namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Spec 041 §5.1 — standalone tier-lint entry point. Runs only the §11
/// per-tier structural checks (no cross-link analyzer, no reference
/// generation, no build/capture/emit). Authors call this for the fast
/// inner loop while iterating on a template's tier upgrade.
/// </summary>
internal static class CheckTierCommand
{
    public static int Run(string[] args)
    {
        var topic = CompileCommand.GetOption(args, "--topic");
        var tierFilterRaw = CompileCommand.GetOption(args, "--tier");
        DocTier? tierFilter = null;
        if (tierFilterRaw is not null)
        {
            try { tierFilter = TemplateParser.ParseTier(tierFilterRaw); }
            catch (DocPipelineException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
        var ci = CompileCommand.HasFlag(args, "--ci");
        var quiet = CompileCommand.HasFlag(args, "--quiet");

        var repoRoot = CompileCommand.FindRepoRoot();
        if (repoRoot == null)
        {
            Console.Error.WriteLine("Error: Could not find repository root (looking for Reactor.slnx or .git).");
            return 1;
        }

        var docsRoot = Path.Combine(repoRoot, "docs");
        var appsDir = Path.Combine(docsRoot, "_pipeline", "apps");
        var templatesDir = Path.Combine(docsRoot, "_pipeline", "templates");

        var result = TierLintOrchestrator.Run(repoRoot, appsDir, templatesDir, topic, tierFilter);

        if (!quiet)
        {
            Console.WriteLine($"  Templates scanned: {result.TemplatesScanned}");
        }

        var errorCount = 0;
        var warningCount = 0;
        foreach (var f in result.Findings)
        {
            if (f.Severity == TierLintSeverity.Error)
            {
                Console.Error.WriteLine(f.Format());
                errorCount++;
            }
            else if (f.Severity == TierLintSeverity.Warning)
            {
                Console.WriteLine($"  ⚠ {f.Format()}");
                warningCount++;
            }
            else if (!quiet)
            {
                Console.WriteLine($"  ℹ {f.Format()}");
            }
        }

        if (!quiet)
        {
            Console.WriteLine();
            Console.WriteLine($"Tier-lint: {errorCount} error(s), {warningCount} warning(s) across {result.TemplatesScanned} template(s).");
        }

        if (errorCount > 0) return 1;
        if (ci && warningCount > 0) return 1;
        return 0;
    }
}
