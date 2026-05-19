namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Orchestrates the §11 tier-lint pipeline end-to-end without the
/// surrounding compile phases: discovery → snippet/screenshot prep →
/// per-template lint. Shared between <see cref="CheckTierCommand"/> and
/// (for now, not yet) <see cref="CompileCommand"/>. Spec 041 §5.1.
/// </summary>
internal static class TierLintOrchestrator
{
    public sealed record Result(int TemplatesScanned, IReadOnlyList<TierLintFinding> Findings);

    /// <summary>
    /// Run discovery + tier-lint against templates under
    /// <paramref name="templatesDir"/>. Source-file <c>source:&lt;path&gt;</c>
    /// snippet references are resolved from <paramref name="repoRoot"/>.
    /// Doc-app snippets and screenshot manifests are pulled from
    /// <paramref name="appsDir"/>. The cross-link analyzer is intentionally
    /// not run — that is compile-time scope, not tier-lint scope.
    /// </summary>
    public static Result Run(
        string repoRoot,
        string appsDir,
        string templatesDir,
        string? topic = null,
        DocTier? tierFilter = null)
    {
        var apps = CompileCommand.DiscoverApps(appsDir, topic);
        List<(string topicId, DocTemplate template)> templates;
        try
        {
            templates = CompileCommand.DiscoverTemplates(templatesDir, topic);
        }
        catch (DocPipelineException ex)
        {
            return new Result(0, new[]
            {
                new TierLintFinding(
                    ex.Code ?? "REACTOR_DOC_TEMPLATE_001",
                    ex.Message,
                    templatesDir,
                    1,
                    TierLintSeverity.Error)
            });
        }

        if (tierFilter is { } filter)
        {
            templates = templates
                .Where(t => t.template.TierDeclared && t.template.Tier == filter)
                .ToList();
        }

        if (templates.Count == 0)
        {
            return new Result(0, Array.Empty<TierLintFinding>());
        }

        var allSnippets = new Dictionary<string, SnippetExtractor.Snippet>(StringComparer.OrdinalIgnoreCase);
        foreach (var (topicId, appDir) in apps)
        {
            foreach (var (key, value) in SnippetExtractor.ExtractFromApp(appDir, topicId))
                allSnippets[key] = value;
        }

        var allScreenshots = new Dictionary<string, ScreenshotInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (topicId, appDir) in apps)
        {
            var manifestPath = Path.Combine(appDir, "doc-manifest.yaml");
            if (!File.Exists(manifestPath)) continue;
            var manifest = ManifestParser.Parse(manifestPath);
            foreach (var ss in manifest.Screenshots)
            {
                var fullId = $"{topicId}/{ss.Id}";
                allScreenshots[fullId] = new ScreenshotInfo(ss.Id, topicId, ss.Description, ss.Format, ss.Kind);
            }
        }

        // Resolve source:<path>#<region> references so the tier-lint
        // resolvedSnippetCount stays accurate for pages that pull snippets
        // directly from `src/`. Unresolvable source refs are silently
        // skipped — `compile --validate-only` surfaces those errors; the
        // narrower check-tier surface should not.
        foreach (var (_, template) in templates)
        {
            foreach (var snippetRef in CompileCommand.ExtractSnippetRefs(template.Body))
            {
                if (allSnippets.ContainsKey(snippetRef)) continue;
                if (!SnippetExtractor.TryParseSourceReference(snippetRef, out var srcPath, out var region))
                    continue;
                try
                {
                    allSnippets[snippetRef] = SnippetExtractor.ExtractFromSource(repoRoot, srcPath, region);
                }
                catch (DocPipelineException)
                {
                    // Unresolvable source ref — leave out of the registry;
                    // tier-lint will not count it as resolved.
                }
            }
        }

        var findings = new List<TierLintFinding>();
        foreach (var (_, template) in templates)
        {
            var (assembled, snipRes, ssRes) = CompileCommand.AssembleForLint(template, allSnippets, allScreenshots);
            findings.AddRange(TierLint.Lint(template, assembled, snipRes, ssRes));
        }

        return new Result(templates.Count, findings);
    }
}
