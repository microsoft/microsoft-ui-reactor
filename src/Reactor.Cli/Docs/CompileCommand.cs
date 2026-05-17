using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Orchestrates the doc compile pipeline: validate → build → capture → extract → (AI) → assemble.
/// </summary>
internal static partial class CompileCommand
{
    public static int Run(string[] args)
    {
        var topic = GetOption(args, "--topic");
        // --no-screenshots is the legacy name; --skip-screenshots is the spec-§10.3
        // name. Both map to the same behavior so authors can use whichever the
        // help / docs they consulted shows.
        var noScreenshots = HasFlag(args, "--no-screenshots") || HasFlag(args, "--skip-screenshots");
        var noAi = HasFlag(args, "--no-ai");
        var noBuild = HasFlag(args, "--no-build");
        var skipDiagrams = HasFlag(args, "--skip-diagrams");
        // Reference generation (spec 041 §10.4) defaults to ON so the
        // compile step is uniform — `--skip-reference` is the inner-loop
        // escape hatch and `--reference` is a no-op alias for explicit
        // callers. Phase 1B restricts generation to the `hooks` category;
        // later phases lift the gate as more categories come online.
        var skipReference = HasFlag(args, "--skip-reference");
        // --reference is accepted but a no-op — present so authors can
        // call it out explicitly in CI scripts. Discarded so we don't shadow
        // the variable.
        _ = HasFlag(args, "--reference");
        var validateOnly = HasFlag(args, "--validate-only");
        var ci = HasFlag(args, "--ci");
        var tierFilterRaw = GetOption(args, "--tier");
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

        var repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Console.Error.WriteLine("Error: Could not find repository root (looking for Reactor.slnx or .git).");
            return 1;
        }

        var docsRoot = Path.Combine(repoRoot, "docs");
        var appsDir = Path.Combine(docsRoot, "_pipeline", "apps");
        var templatesDir = Path.Combine(docsRoot, "_pipeline", "templates");
        var diagramsDir = Path.Combine(docsRoot, "_pipeline", "diagrams");
        var outputDir = Path.Combine(docsRoot, "guide");
        var imagesDir = Path.Combine(outputDir, "images");

        // ── Phase 1: Validate ─────────────────────────────────────────────
        Console.WriteLine("═══ Phase 1: Validate ═══");

        var apps = DiscoverApps(appsDir, topic);
        Console.WriteLine($"  Found {apps.Count} doc app(s)");
        foreach (var (id, dir) in apps)
            Console.WriteLine($"    • {id} → {Path.GetRelativePath(repoRoot, dir)}");

        List<(string topicId, DocTemplate template)> templates;
        try
        {
            templates = DiscoverTemplates(templatesDir, topic);
        }
        catch (DocPipelineException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        // --tier <stub|solid|comprehensive> subsets templates to those that
        // explicitly declared the matching tier — for fast iteration on one
        // band without re-linting the full set.
        if (tierFilter is { } filter)
        {
            var before = templates.Count;
            templates = templates
                .Where(t => t.template.TierDeclared && t.template.Tier == filter)
                .ToList();
            Console.WriteLine($"  --tier={filter.ToString().ToLowerInvariant()} filter: {templates.Count}/{before} template(s)");
        }
        Console.WriteLine($"  Found {templates.Count} template(s)");
        foreach (var (id, t) in templates)
            Console.WriteLine($"    • {id} → {Path.GetRelativePath(repoRoot, t.FilePath)} [tier={t.Tier.ToString().ToLowerInvariant()}{(t.TierDeclared ? "" : " default")}]");

        if (apps.Count == 0 && templates.Count == 0)
        {
            Console.Error.WriteLine("  No doc apps or templates found.");
            return 1;
        }

        // ── Phase 4 (early): Extract snippets ─────────────────────────────
        Console.WriteLine();
        Console.WriteLine("═══ Phase 4: Extract Snippets ═══");

        var allSnippets = new Dictionary<string, SnippetExtractor.Snippet>(StringComparer.OrdinalIgnoreCase);
        foreach (var (topicId, appDir) in apps)
        {
            var snippets = SnippetExtractor.ExtractFromApp(appDir, topicId);
            foreach (var (key, value) in snippets)
            {
                allSnippets[key] = value;
                var lineCount = value.Code.Split('\n').Length;
                Console.WriteLine($"  {key} ({lineCount} lines from {Path.GetFileName(value.SourceFile)}:{value.StartLine})");
            }
        }
        Console.WriteLine($"  Total: {allSnippets.Count} snippet(s)");

        // Build screenshot registry from manifests
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
        Console.WriteLine($"  Screenshot definitions: {allScreenshots.Count}");

        // Validate references
        var hasErrors = false;
        foreach (var (topicId, template) in templates)
        {
            foreach (var snippetRef in ExtractSnippetRefs(template.Body))
            {
                // Spec §10.2: source:<path>#<region> reads directly from the
                // repository tree rather than the doc-app's captured snippets.
                if (SnippetExtractor.TryParseSourceReference(snippetRef, out var srcPath, out var region))
                {
                    try
                    {
                        var snip = SnippetExtractor.ExtractFromSource(repoRoot, srcPath, region);
                        allSnippets[snippetRef] = snip;
                        Console.WriteLine($"  ✓ snippet \"{snippetRef}\" resolved ({snip.Code.Split('\n').Length} lines)");
                    }
                    catch (DocPipelineException ex)
                    {
                        Console.Error.WriteLine($"  ✗ Template '{topicId}': {ex.Code}: {ex.Message}");
                        hasErrors = true;
                    }
                    continue;
                }

                if (!allSnippets.ContainsKey(snippetRef))
                {
                    Console.Error.WriteLine($"  ✗ Template '{topicId}': missing snippet '{snippetRef}'");
                    hasErrors = true;
                }
                else
                {
                    Console.WriteLine($"  ✓ snippet \"{snippetRef}\" resolved");
                }
            }

            foreach (var ssRef in ExtractScreenshotRefs(template.Body))
            {
                if (!allScreenshots.ContainsKey(ssRef))
                    Console.WriteLine($"  ⚠ Template '{topicId}': no screenshot definition for '{ssRef}'");
                else
                    Console.WriteLine($"  ✓ screenshot \"{ssRef}\" resolved");
            }
        }

        if (hasErrors && ci)
        {
            Console.Error.WriteLine("Validation failed.");
            return 1;
        }

        // ── Tier-lint (spec §11) ──────────────────────────────────────────
        // Run per-tier structural checks against the assembled body so the
        // lint sees the same shape readers will see on GitHub. We assemble
        // here even in --validate-only mode (no file write).
        Console.WriteLine();
        Console.WriteLine("═══ Tier Lint ═══");
        var tierHasErrors = false;
        foreach (var (topicId, template) in templates)
        {
            var (assembled, snipRes, ssRes) = AssembleForLint(template, allSnippets, allScreenshots);
            var findings = TierLint.Lint(template, assembled, snipRes, ssRes);
            foreach (var f in findings)
            {
                if (f.Severity == TierLintSeverity.Error)
                {
                    Console.Error.WriteLine(f.Format());
                    tierHasErrors = true;
                }
                else if (f.Severity == TierLintSeverity.Warning)
                {
                    Console.WriteLine($"  ⚠ {f.Format()}");
                }
                else
                {
                    // Info-level: no declared tier, so the violation is informational.
                    Console.WriteLine($"  ℹ {f.Format()}");
                }
            }
        }

        // ── Cross-link analyzer (spec §4.5) ───────────────────────────────
        // Walk every template body checking that any prose mention of a
        // page-owned concept is linked to that page. Findings default to
        // Warning severity — false positives on first roll-out should not
        // break the docset. Elevate to Error once Phase 4.5 lands clean.
        Console.WriteLine();
        Console.WriteLine("═══ Cross-Link Lint ═══");
        var xlinkTemplates = templates
            .Select(t => new CrossLinkTemplate(
                t.topicId,
                t.template.FilePath,
                AssembleForLint(t.template, allSnippets, allScreenshots).body,
                t.template.Title,
                t.template.ConceptAliases))
            .ToList();
        var refConcepts = DiscoverReferenceConcepts(outputDir);
        var xlinkFindings = CrossLinkLint.Run(xlinkTemplates, refConcepts);
        var xlinkErrors = 0;
        foreach (var f in xlinkFindings)
        {
            if (f.Severity == TierLintSeverity.Error)
            {
                Console.Error.WriteLine(f.Format());
                xlinkErrors++;
            }
            else
            {
                Console.WriteLine($"  ⚠ {f.Format()}");
            }
        }
        Console.WriteLine($"  Cross-link findings: {xlinkFindings.Count} ({xlinkErrors} error, {xlinkFindings.Count - xlinkErrors} warning).");

        if (validateOnly)
        {
            Console.WriteLine();
            var combined = hasErrors || tierHasErrors || xlinkErrors > 0;
            Console.WriteLine(combined ? "Validation finished with errors." : "Validation passed.");
            return combined ? 1 : 0;
        }

        if (tierHasErrors && ci)
        {
            Console.Error.WriteLine("Tier lint failed in --ci mode.");
            return 1;
        }

        // ── Phase 2: Build ────────────────────────────────────────────────
        Console.WriteLine();
        if (noBuild)
        {
            Console.WriteLine("═══ Phase 2: Build (skipped) ═══");
        }
        else
        {
            Console.WriteLine("═══ Phase 2: Build ═══");
            foreach (var (topicId, appDir) in apps)
            {
                Console.Write($"  Building {topicId}...");
                var exitCode = BuildApp(appDir);
                if (exitCode != 0)
                {
                    Console.Error.WriteLine($" ✗ build failed (exit code {exitCode})");
                    return 1;
                }
                Console.WriteLine(" ✓");
            }
        }

        // ── Phase 3: Capture ──────────────────────────────────────────────
        Console.WriteLine();
        if (noScreenshots)
        {
            Console.WriteLine("═══ Phase 3: Capture (skipped) ═══");
        }
        else
        {
            Console.WriteLine("═══ Phase 3: Capture ═══");
            foreach (var (topicId, appDir) in apps)
            {
                var manifestPath = Path.Combine(appDir, "doc-manifest.yaml");
                if (!File.Exists(manifestPath)) continue;
                var manifest = ManifestParser.Parse(manifestPath);
                if (manifest.Screenshots.Count == 0) continue;

                Console.WriteLine($"  Capturing for {topicId}...");
                ScreenshotCapture.CaptureAsync(appDir, topicId, manifest, imagesDir)
                    .GetAwaiter().GetResult();
            }
        }

        // ── Phase 5: AI Author ────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine($"═══ Phase 5: AI Author {(noAi ? "(skipped)" : "(not yet implemented)")} ═══");

        // ── Phase 5.5: Diagrams (SVG passthrough + Mermaid) ───────────────
        Console.WriteLine();
        if (skipDiagrams)
        {
            Console.WriteLine("═══ Phase 5.5: Diagrams (skipped) ═══");
        }
        else
        {
            Console.WriteLine("═══ Phase 5.5: Diagrams ═══");
            IMermaidRunner mermaid = new MmdcRunner();
            var diag = DiagramProcessor.Process(diagramsDir, imagesDir, mermaid, topic);
            Console.WriteLine(
                $"  Diagrams: {diag.CopiedSvgs.Count} copied, {diag.SkippedSvgs.Count} skipped, " +
                $"{diag.RenderedMermaid.Count} rendered, {diag.CachedMermaid.Count} cached.");
            foreach (var f in diag.Findings)
            {
                if (f.Severity == TierLintSeverity.Error)
                {
                    Console.Error.WriteLine(f.Format());
                    hasErrors = true;
                }
                else
                {
                    Console.WriteLine($"  ⚠ {f.Format()}");
                }
            }
            if (hasErrors && ci)
            {
                Console.Error.WriteLine("Diagram processing failed.");
                return 1;
            }
        }

        // ── Phase 5.7: Reference generation (spec §10.4) ──────────────────
        ReferenceGen.ReferenceGenResult? phaseRefResult = null;
        Console.WriteLine();
        if (skipReference)
        {
            Console.WriteLine("═══ Phase 5.7: Reference (skipped) ═══");
        }
        else
        {
            Console.WriteLine("═══ Phase 5.7: Reference ═══");
            phaseRefResult = RunReferenceGeneration(repoRoot, outputDir);
            if (phaseRefResult is not null)
            {
                foreach (var f in phaseRefResult.Findings)
                {
                    if (f.Severity == TierLintSeverity.Error)
                    {
                        Console.Error.WriteLine(f.Format());
                        hasErrors = true;
                    }
                    else
                    {
                        Console.WriteLine($"  ⚠ {f.Format()}");
                    }
                }
                Console.WriteLine($"  Generated: {phaseRefResult.Pages.Count} page(s)");
            }
            if (hasErrors && ci)
            {
                Console.Error.WriteLine("Reference generation failed.");
                return 1;
            }
        }

        // ── Phase 6: Assemble ─────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("═══ Phase 6: Assemble ═══");
        Directory.CreateDirectory(outputDir);

        foreach (var (topicId, template) in templates)
        {
            Console.Write($"  Assembling {topicId}...");

            var assembled = DocAssembler.Assemble(
                template.Body, allSnippets, allScreenshots,
                out var errors, out var warnings);

            // Expand <!-- ref:Member --> markers in the assembled body so
            // hand-authored guide pages can cross-link into the generated
            // reference (spec §10.4.1).
            if (phaseRefResult is not null)
            {
                var markerFindings = new List<ReferenceGen.RefGenFinding>();
                assembled = ReferenceLinkInjector.ExpandMarkers(assembled, topicId, phaseRefResult, markerFindings);
                foreach (var f in markerFindings) Console.WriteLine($"  ⚠ {f.Format()}");
            }

            foreach (var e in errors) Console.Error.WriteLine($"\n    ✗ {e}");
            foreach (var w in warnings) Console.WriteLine($"\n    ⚠ {w}");

            // Image-ref validation per spec §10.3: every ![..](images/...)
            // path in the compiled output must resolve.
            foreach (var f in DiagramProcessor.ValidateImageRefs(template.FilePath, assembled, imagesDir))
            {
                Console.Error.WriteLine(f.Format());
                hasErrors = true;
            }

            var outputPath = Path.Combine(outputDir, $"{topicId}.md");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, assembled);
            Console.WriteLine($" ✓ → {Path.GetRelativePath(repoRoot, outputPath)}");
        }

        if (hasErrors && ci)
        {
            Console.Error.WriteLine("Compile finished with errors.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Documentation compiled successfully.");
        return 0;
    }

    // ── Discovery ─────────────────────────────────────────────────────────

    internal static List<(string topicId, string dir)> DiscoverApps(string appsDir, string? topic)
    {
        var result = new List<(string, string)>();
        if (!Directory.Exists(appsDir)) return result;

        foreach (var dir in Directory.GetDirectories(appsDir))
        {
            var topicId = Path.GetFileName(dir);
            if (topic != null && !topicId.Equals(topic, StringComparison.OrdinalIgnoreCase))
                continue;
            // Must have at least one .cs file
            if (Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly).Length > 0)
                result.Add((topicId, dir));
        }
        return result;
    }

    /// <summary>
    /// Discovers every <c>*.md.dt</c> template under <paramref name="templatesDir"/>,
    /// recursing into subfolders (e.g. <c>recipes/</c>) but excluding the
    /// <c>_skeletons/</c> directory — those files are author scaffolds with
    /// placeholder tokens, not real pages, and intentionally fail tier-lint.
    /// The topic id includes any subfolder path so a template at
    /// <c>recipes/login.md.dt</c> has id <c>recipes/login</c> and emits to
    /// <c>docs/guide/recipes/login.md</c>.
    /// </summary>
    internal static List<(string topicId, DocTemplate template)> DiscoverTemplates(string templatesDir, string? topic)
    {
        var result = new List<(string, DocTemplate)>();
        if (!Directory.Exists(templatesDir)) return result;

        foreach (var file in EnumerateTemplateFiles(templatesDir))
        {
            // Topic id = repo-relative path under templatesDir minus the .md.dt
            // extension, with forward slashes so it round-trips to a guide
            // output path on every OS.
            var rel = Path.GetRelativePath(templatesDir, file).Replace('\\', '/');
            var topicId = rel.EndsWith(".md.dt", StringComparison.Ordinal)
                ? rel[..^".md.dt".Length]
                : Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(rel));
            if (topic != null && !topicId.Equals(topic, StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add((topicId, TemplateParser.Parse(file)));
        }

        return result.OrderBy(t => t.Item2.Order).ToList();
    }

    /// <summary>
    /// Yields every <c>*.md.dt</c> under <paramref name="templatesDir"/>,
    /// recursing into subfolders but skipping the <c>_skeletons/</c>
    /// scaffold directory (spec 041 §9 Phase 1.11).
    /// </summary>
    internal static IEnumerable<string> EnumerateTemplateFiles(string templatesDir)
    {
        if (!Directory.Exists(templatesDir)) yield break;
        var skeletons = Path.Combine(templatesDir, "_skeletons");
        foreach (var file in Directory.EnumerateFiles(templatesDir, "*.md.dt", SearchOption.AllDirectories))
        {
            // Skip anything under _skeletons/ (or nested subfolders thereof).
            if (file.StartsWith(skeletons + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                file.StartsWith(skeletons + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            yield return file;
        }
    }

    // ── Build ─────────────────────────────────────────────────────────────

    private static int BuildApp(string appDir)
    {
        var csproj = Directory.GetFiles(appDir, "*.csproj").FirstOrDefault();
        if (csproj == null) return 1;

        // WindowsAppSDK self-contained builds reject the AnyCPU default and
        // require an explicit architecture. Match the host so x64 boxes get
        // x64 binaries and ARM64 boxes get ARM64 binaries.
        var platform = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
            _ => "x64",
        };

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{csproj}\" -v q --nologo -nowarn:MSB3277 -p:Platform={platform}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        // Read stdout and stderr in parallel to avoid deadlock
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = stderrTask.GetAwaiter().GetResult();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine();
            if (!string.IsNullOrWhiteSpace(stdout)) Console.Error.Write(stdout);
            if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.Write(stderr);
        }

        return process.ExitCode;
    }

    /// <summary>
    /// Build the cross-link concept registry from generated reference pages
    /// already on disk under <c>docs/guide/reference/&lt;category&gt;/</c>.
    /// Each reference filename (e.g. <c>UseFocusTrap.md</c>) becomes a
    /// concept whose href is the reference-relative path. The mapping lets
    /// guide prose like "…wraps the focus root via UseFocusTrap…" trip
    /// XLINK_001 unless the page actually links to the reference. Missing
    /// reference directories (early-phase compiles) just produce an empty
    /// list — the analyzer still runs against title-derived concepts.
    /// </summary>
    private static List<CrossLinkConcept> DiscoverReferenceConcepts(string outputDir)
    {
        var result = new List<CrossLinkConcept>();
        var refRoot = Path.Combine(outputDir, "reference");
        if (!Directory.Exists(refRoot)) return result;
        foreach (var file in Directory.EnumerateFiles(refRoot, "*.md", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(name) || name.Equals("index", StringComparison.OrdinalIgnoreCase))
                continue;
            // Skip extension classes — their `…Extensions` suffix isn't a
            // natural-prose concept name (authors write "UseFocus" not
            // "UseFocusExtensions"). The base type carries the concept.
            if (name.EndsWith("Extensions", StringComparison.Ordinal)) continue;
            var rel = Path.GetRelativePath(outputDir, file).Replace('\\', '/');
            // The topic id for a reference page is its rel path (used only
            // for self-ref exclusion; reference pages aren't templates so
            // this never collides).
            result.Add(new CrossLinkConcept(name, rel, rel));
        }
        return result;
    }

    /// <summary>
    /// Assemble a template's body for tier-lint inspection. Same call as the
    /// emit-time DocAssembler but discards errors/warnings (lint reports its
    /// own findings) and returns the counts of *resolved* snippet/screenshot
    /// references so the tier checklist can enforce the §11 minimums.
    /// </summary>
    internal static (string body, int resolvedSnippets, int resolvedScreenshots) AssembleForLint(
        DocTemplate template,
        Dictionary<string, SnippetExtractor.Snippet> allSnippets,
        Dictionary<string, ScreenshotInfo> allScreenshots)
    {
        var snippetRefs = ExtractSnippetRefs(template.Body);
        var resolvedSnippets = snippetRefs.Count(r => allSnippets.ContainsKey(r));
        var screenshotRefs = ExtractScreenshotRefs(template.Body);
        var resolvedScreenshots = screenshotRefs.Count(r => allScreenshots.ContainsKey(r));
        var assembled = DocAssembler.Assemble(template.Body, allSnippets, allScreenshots, out _, out _);
        return (assembled, resolvedSnippets, resolvedScreenshots);
    }

    /// <summary>
    /// Locate the freshly-built <c>Reactor.xml</c> (preferring Debug, then
    /// Release) and run the reference generator restricted to the Hooks
    /// category. Returns <c>null</c> when the XML doc file isn't on disk
    /// yet — typical on first compile before <c>dotnet build src/Reactor</c>
    /// has run. The caller can decide whether to surface that as a warning;
    /// for Phase 1B it's silent because the unit tests are the canonical
    /// surface.
    /// </summary>
    private static ReferenceGen.ReferenceGenResult? RunReferenceGeneration(string repoRoot, string outputDir)
    {
        var registryPath = Path.Combine(repoRoot, "docs", "_pipeline", "reference-map.yaml");
        if (!File.Exists(registryPath))
        {
            Console.WriteLine($"  (reference-map.yaml not found at {Path.GetRelativePath(repoRoot, registryPath)} — skipping)");
            return null;
        }

        ReferenceMap map;
        try { map = ReferenceMap.Load(registryPath); }
        catch (DocPipelineException ex)
        {
            Console.Error.WriteLine($"  {ex.Code}: {ex.Message}");
            return new ReferenceGen.ReferenceGenResult(
                Array.Empty<ReferenceGen.GeneratedPage>(),
                new[] { new ReferenceGen.RefGenFinding(
                    ex.Code ?? "REACTOR_DOC_REGISTRY_001",
                    ex.Message,
                    registryPath,
                    TierLintSeverity.Error) });
        }

        var xmlPath = FindReactorXml(repoRoot);
        if (xmlPath is null)
        {
            Console.WriteLine("  (Reactor.xml not found — run `dotnet build src/Reactor` first)");
            return null;
        }

        var generator = new ReferenceGen.ReferenceGenerator();
        var result = generator.Generate(
            xmlPath,
            map,
            referenceRoot: outputDir,
            categoryAllowList: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hooks" });

        // ── Spec §10.4.1 — conceptual-guide link injection ────────────────
        // Scan every template for <!-- ref:Member --> markers so each
        // generated reference page can grow a "Featured in" backlink. The
        // template bodies are already parsed by DiscoverTemplates higher
        // up, but ref-gen runs in its own helper and doesn't yet take the
        // template list as input — re-scan here from disk. Cheap enough
        // (small file count) for Phase 1B.
        var templateBodies = new List<(string topicId, string body)>();
        var templatesDir = Path.Combine(repoRoot, "docs", "_pipeline", "templates");
        foreach (var f in EnumerateTemplateFiles(templatesDir))
        {
            var rel = Path.GetRelativePath(templatesDir, f).Replace('\\', '/');
            var id = rel.EndsWith(".md.dt", StringComparison.Ordinal)
                ? rel[..^".md.dt".Length]
                : Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(rel));
            templateBodies.Add((id, File.ReadAllText(f)));
        }
        var reverseIndex = ReferenceLinkInjector.BuildReverseIndex(templateBodies);

        var injectionFindings = new List<ReferenceGen.RefGenFinding>();
        var injectedPages = new List<ReferenceGen.GeneratedPage>(result.Pages.Count);
        foreach (var page in result.Pages)
        {
            var newBody = ReferenceLinkInjector.Inject(page, result, reverseIndex, injectionFindings);
            injectedPages.Add(page with { Body = newBody });
        }

        // Lint W002: orphaned guide pages. Build the union of every
        // guide-page declared by either an override or a default rule, then
        // check which of those have no inbound marker.
        var declaredGuidePages = result.Pages.SelectMany(p => p.Route.GuidePages).ToList();
        var templateIds = templateBodies.Select(t => t.topicId).ToList();
        injectionFindings.AddRange(ReferenceLinkInjector.LintOrphanedGuidePages(
            declaredGuidePages, templateIds, reverseIndex));

        // Merge findings; the injector findings join the generator's.
        var combined = new ReferenceGen.ReferenceGenResult(
            injectedPages,
            result.Findings.Concat(injectionFindings).ToList());

        // Write pages to disk so authors and lints can see the output.
        generator.WriteToDisk(combined, outputDir);
        return combined;
    }

    private static string? FindReactorXml(string repoRoot)
    {
        var binDir = Path.Combine(repoRoot, "src", "Reactor", "bin");
        if (!Directory.Exists(binDir)) return null;

        foreach (var config in new[] { "Debug", "Release" })
        {
            // Walk the standard bin layout: bin/<config>/<tfm>/Reactor.xml
            // and the platform-stamped variants bin/<arch>/<config>/<tfm>/Reactor.xml.
            foreach (var candidate in EnumerateCandidates(binDir, config))
            {
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(string binDir, string config)
    {
        // Flat layout: bin/<config>/<tfm>/Reactor.xml
        var configRoot = Path.Combine(binDir, config);
        if (Directory.Exists(configRoot))
        {
            foreach (var tfm in Directory.GetDirectories(configRoot))
                yield return Path.Combine(tfm, "Reactor.xml");
        }
        // Platform-stamped: bin/<arch>/<config>/<tfm>/Reactor.xml
        foreach (var arch in new[] { "x64", "ARM64" })
        {
            var archConfigRoot = Path.Combine(binDir, arch, config);
            if (!Directory.Exists(archConfigRoot)) continue;
            foreach (var tfm in Directory.GetDirectories(archConfigRoot))
                yield return Path.Combine(tfm, "Reactor.xml");
        }
    }

    // ── Reference extraction (for validation) ─────────────────────────────

    [GeneratedRegex(@"snippet=""([^""]+)""")]
    private static partial Regex SnippetRefPattern();

    [GeneratedRegex("""screenshot://([^)]+)""")]
    private static partial Regex ScreenshotRefPattern();

    internal static List<string> ExtractSnippetRefs(string body) =>
        SnippetRefPattern().Matches(body).Select(m => m.Groups[1].Value).ToList();

    internal static List<string> ExtractScreenshotRefs(string body) =>
        ScreenshotRefPattern().Matches(body).Select(m => m.Groups[1].Value).ToList();

    // ── Arg parsing ───────────────────────────────────────────────────────

    internal static string? FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Reactor.slnx")) || Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    internal static string? GetOption(string[] args, string name)
    {
        var idx = Array.IndexOf(args, name);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }

    internal static bool HasFlag(string[] args, string name) =>
        args.Contains(name, StringComparer.OrdinalIgnoreCase);
}
