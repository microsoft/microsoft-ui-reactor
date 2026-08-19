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
        IReadOnlySet<string>? screenshotFilter;
        try
        {
            screenshotFilter = ParseScreenshotFilter(args, topic);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
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

        // Resolve the single source of truth for the public package version so
        // the {{reactorVersion}} token in guide templates renders a real, pinned
        // version. Deterministic (reads committed Directory.Build.props, never a
        // live NuGet lookup) so the docs freshness gate can't false-fail when a
        // new version publishes. Threaded into every DocAssembler.Assemble call
        // (emit + lint) below.
        string reactorVersion;
        try
        {
            reactorVersion = VersionSource.ReadPublicVersion(repoRoot);
        }
        catch (DocPipelineException ex)
        {
            Console.Error.WriteLine(ex.Message);
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
        var screenshotTopics = screenshotFilter is null
            ? null
            : screenshotFilter
                .Select(id => id[..id.IndexOf('/')])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (screenshotTopics is not null && topic is null)
        {
            apps = apps.Where(app => screenshotTopics.Contains(app.topicId)).ToList();
        }
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
        if (screenshotTopics is not null && topic is null)
        {
            templates = templates
                .Where(template => screenshotTopics.Contains(template.topicId))
                .ToList();
            Console.WriteLine($"  --screenshots filter: {string.Join(", ", screenshotFilter!.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))}");
        }
        else if (screenshotFilter is not null)
        {
            Console.WriteLine($"  --screenshots filter: {string.Join(", ", screenshotFilter.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))}");
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
        var reservedSuffixIds = new List<(string Id, string FullId, string ManifestPath)>();
        foreach (var (topicId, appDir) in apps)
        {
            var manifestPath = Path.Combine(appDir, "doc-manifest.yaml");
            if (!File.Exists(manifestPath)) continue;
            var manifest = ManifestParser.Parse(manifestPath);
            foreach (var ss in manifest.Screenshots)
            {
                var fullId = $"{topicId}/{ss.Id}";
                // The `-thumb` suffix is how ImageProcessor.ContentRegionFor tells a
                // chrome-free catalog thumbnail from a full-size capture that has a
                // border and drop shadow. A full-size screenshot named `<x>-thumb`
                // would be scored whole, and its own chrome would then mask a blank
                // capture from the REACTOR_DOC_IMAGE_002 gate. Reserve the suffix so
                // that collision cannot be authored in the first place.
                if (ImageProcessor.IdHasThumbSuffix(ss.Id)
                    && !string.Equals(ss.Kind, "catalog-thumb", StringComparison.OrdinalIgnoreCase))
                {
                    reservedSuffixIds.Add((ss.Id, fullId, manifestPath));
                }
                allScreenshots[fullId] = new ScreenshotInfo(ss.Id, topicId, ss.Description, ss.Format, ss.Kind);
            }
        }
        if (reservedSuffixIds.Count > 0)
        {
            // The id is quoted on its own, not folded into the location, because
            // it is the string an author greps their manifest for. Emitting
            // "screenshot id 'topic/x-thumb (path/to/screenshots.yml)'" labels a
            // composed locator as an id and finds nothing when pasted into a
            // search — the diagnostic naming a value that does not exist in the
            // file it is pointing at.
            foreach (var (id, fullId, manifestPath) in reservedSuffixIds)
            {
                Console.Error.WriteLine(
                    $"  ✗ REACTOR_DOC_SHOT_002: screenshot id '{id}' ends in the reserved " +
                    $"'{ImageProcessor.ThumbSuffix}' suffix, which is only valid for " +
                    $"'kind: catalog-thumb'. Rename it, or set the kind. ({fullId} in {manifestPath})");
            }
            return 1;
        }
        Console.WriteLine($"  Screenshot definitions: {allScreenshots.Count}");
        if (screenshotFilter is not null)
        {
            var missingScreenshots = screenshotFilter
                .Where(id => !allScreenshots.ContainsKey(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (missingScreenshots.Count > 0)
            {
                foreach (var id in missingScreenshots)
                    Console.Error.WriteLine($"  ✗ --screenshots requested unknown screenshot '{id}'");
                return 1;
            }
        }

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
            var (assembled, snipRes, ssRes) = AssembleForLint(template, allSnippets, allScreenshots, topicId, reactorVersion);
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
                AssembleForLint(t.template, allSnippets, allScreenshots, t.topicId, reactorVersion).body,
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
            // In CI, build the doc apps in Release so they go through the same
            // TreatWarningsAsErrors gate as the rest of the repo (Directory.Build.props
            // scopes it to Release). Doc apps aren't in Reactor.slnx, so this is the
            // only place a warning in exemplar snippet code — an obsolete API, a stray
            // duplicate using — is caught. Locally we build Debug: faster, and it's the
            // config the capture phase (dotnet run) launches.
            var buildConfiguration = ci ? "Release" : "Debug";
            foreach (var (topicId, appDir) in apps)
            {
                Console.Write($"  Building {topicId} ({buildConfiguration})...");
                var exitCode = BuildApp(appDir, buildConfiguration);
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
        int captureFailed = 0;
        if (noScreenshots)
        {
            // Explicit about the guarantee, and about its edge: this phase is
            // the only thing in the pipeline that writes a *screenshot* — the
            // only binary writer — so skipping it leaves every committed
            // screenshot exactly as it was (issue #989).
            //
            // It is not the only writer under docs/guide/images/. Phase 5.5
            // (Diagrams) also targets that tree: DiagramProcessor.Process is
            // called with imagesDir and writes three kinds of file into it —
            // copied docs/_pipeline/diagrams/<topic>/*.svg, mmdc-rendered
            // <name>.svg, and .<name>.mmd.sha256 cache sidecars. All are text
            // with filenames disjoint from any captured .png, so they cannot
            // collide — but "--no-screenshots means nothing writes here" is
            // false, and stating it that way would invite someone to weaken the
            // CI gate to match. The gate is deliberately broader than this
            // guarantee: it watches the whole directory, so a diagram write on
            // the skip path is caught too rather than assumed impossible.
            //
            // The mmdc render is the easy one to miss: it happens in a separate
            // process, so no File.Write*/File.Copy search of this repo finds it,
            // and the first version of this comment listed only the two managed
            // writes. Its .svg destination is hard-coded at the DiagramProcessor
            // call site rather than being a property of mmdc — which renders PNG
            // on request — and DiagramTests pins the full written set so a
            // fourth writer breaks a test instead of outdating this quietly.
            Console.WriteLine("═══ Phase 3: Capture (skipped — existing screenshots left untouched) ═══");
        }
        else
        {
            Console.WriteLine("═══ Phase 3: Capture ═══");
            int captureWritten = 0, captureRequested = 0;
            foreach (var (topicId, appDir) in apps)
            {
                var manifestPath = Path.Combine(appDir, "doc-manifest.yaml");
                if (!File.Exists(manifestPath)) continue;
                var manifest = ManifestParser.Parse(manifestPath);
                if (manifest.Screenshots.Count == 0) continue;
                if (screenshotFilter is not null &&
                    !manifest.Screenshots.Any(s => screenshotFilter.Contains($"{topicId}/{s.Id}")))
                {
                    continue;
                }

                Console.WriteLine($"  Capturing for {topicId}...");
                var result = ScreenshotCapture
                    .CaptureAsync(appDir, topicId, manifest, imagesDir, screenshotFilter)
                    .GetAwaiter().GetResult();
                captureWritten += result.Written;
                captureRequested += result.Requested;
            }

            Console.WriteLine($"  Captured {captureWritten}/{captureRequested} screenshot(s).");
            captureFailed = captureRequested - captureWritten;
            if (captureFailed > 0)
            {
                // A capture pass that produced nothing used to exit 0 with a
                // few lines of stderr nobody read. Surface it as an error so a
                // headless run fails loudly instead of quietly shipping a
                // half-updated screenshot corpus.
                Console.Error.WriteLine(
                    $"  ✗ {captureFailed} screenshot(s) failed to capture; their existing files were left unchanged.");
                hasErrors = true;
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

        // Shared across every page so the screenshot corpus is decoded once per
        // compile rather than once per referencing page. Keyed by resolved file
        // path, so it uses the platform's path comparison rather than a fixed
        // case-insensitive one: on a case-sensitive filesystem `a.png` and
        // `A.png` are two files, and collapsing them would serve one's verdict
        // for the other — a blank image inheriting a clean neighbour's Ok and
        // never reaching REACTOR_DOC_IMAGE_002.
        var blankImageCache = new Dictionary<string, DiagramProcessor.RasterVerdict>(DocPaths.PathComparer);

        foreach (var (topicId, template) in templates)
        {
            Console.Write($"  Assembling {topicId}...");

            var assembled = DocAssembler.Assemble(
                template.Body, allSnippets, allScreenshots,
                out var errors, out var warnings, topicId, reactorVersion);

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

            // Path.Join, not Path.Combine: topicId is derived from
            // GetRelativePath, which yields a rooted path when the template
            // lives on another volume and a ../-prefixed one when it resolves
            // outside templatesDir (a junction or symlink is enough). Combine
            // silently discards outputDir for the rooted case, so the base
            // would come entirely from the discovered path. Join keeps the
            // base; the containment check then covers the traversal case,
            // which a rooted-only guard would miss.
            //
            // Containment is necessary and not sufficient, and the sufficiency
            // gap is narrower and stranger than the earlier version of this
            // comment claimed. It said a drive-rooted id "fails later at the
            // write", and that this was "a worse error message, not an escape".
            // Measured, both halves and the boundary between them:
            //
            //   Join(root, "D:/other/topic.md") -> root\D:\other\topic.md
            //       IsUnder=True, write THROWS  (a stream name cannot contain
            //       a separator) — so for THIS shape the old claim held.
            //   Join(root, "D:foo")             -> root\D:foo
            //       IsUnder=True, write SUCCEEDS into the alternate data stream
            //       "foo" on a file named D. The directory then lists a single
            //       zero-byte D and the real bytes are invisible to a listing,
            //       to git, and to any size check.
            //
            // So the old comment generalised from the one colon shape that is
            // loud to the one that is silent, and the difference is whether the
            // tail happens to contain a separator. GetRelativePath across
            // volumes usually produces the loud shape, which is exactly why the
            // quiet one would have gone unnoticed. Rather than depend on that —
            // a coupling nobody would think to preserve — the shared helper
            // rejects any colon up front, before the join, with a message that
            // names the cause.
            //
            // Using the helper rather than spelling the pair inline is the
            // point: the colon rule lives with the containment rule, so a call
            // site cannot end up with one and not the other. This one had.
            var outputPath = DocPaths.ResolveContained(
                outputDir, $"{topicId}.md", $"Topic '{topicId}'",
                "Templates must live under the templates root.");

            // Image-ref validation per spec §10.3: every ![..](images/...)
            // path in the compiled output must resolve — and, since issue #989,
            // must not be a blank stub left behind by a failed capture.
            // Resolved against the page's own directory, so a nested topic's
            // ../ run is validated rather than assumed correct.
            foreach (var f in DiagramProcessor.ValidateImageRefs(
                         template.FilePath, assembled, imagesDir,
                         Path.GetDirectoryName(outputPath)!, blankImageCache))
            {
                if (IsBuildBreaking(f))
                {
                    Console.Error.WriteLine(f.Format());
                    hasErrors = true;
                }
                else
                {
                    // REACTOR_DOC_IMAGE_004 is the only non-error finding this
                    // gate emits: the decoder was unavailable, so the blank scan
                    // could not run. That is "not checked", not "checked and
                    // bad" — and it is raised precisely on the platform that
                    // cannot decode, so breaking --ci on it would fail a docs
                    // build over a missing codec while nothing is wrong with the
                    // docs. Routed like every other warning in this file:
                    // visible on stdout, not fatal.
                    Console.WriteLine($"  ⚠ {f.Format()}");
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var normalized = NormalizeLineEndings(assembled);
            File.WriteAllText(outputPath, normalized);
            Console.WriteLine($" ✓ → {Path.GetRelativePath(repoRoot, outputPath)}");

            // The site-root template (topicId "index") also writes README.md
            // alongside so GitHub's directory browser renders the landing page
            // when readers browse docs/guide/ on the source repo. The MkDocs
            // site uses index.md; GitHub's web UI uses README.md.
            if (string.Equals(topicId, "index", StringComparison.Ordinal))
            {
                var readmePath = Path.Combine(Path.GetDirectoryName(outputPath)!, "README.md");
                File.WriteAllText(readmePath, normalized);
                Console.WriteLine($"   → {Path.GetRelativePath(repoRoot, readmePath)} (GitHub directory browser)");
            }
        }

        // A failed capture reports on an action this invocation just took: the
        // run was asked to refresh N screenshots and refreshed fewer. That is
        // wrong regardless of --ci, and printing "compiled successfully" after
        // it is how a half-updated corpus reaches `git add -A` unnoticed
        // (issue #989). Validation findings keep the existing --ci-gated
        // behaviour because they report on pre-existing tree state, which an
        // author may legitimately be part-way through fixing.
        if (captureFailed > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                $"Compile finished with {captureFailed} failed screenshot capture(s). " +
                "Existing images were left untouched; re-run capture on an interactive desktop, " +
                "or pass --no-screenshots to skip Phase 3 entirely.");
            return 1;
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

    private static int BuildApp(string appDir, string configuration)
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
            Arguments = $"build \"{csproj}\" -c {configuration} -v q --nologo -nowarn:MSB3277 -p:Platform={platform}",
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
        Dictionary<string, ScreenshotInfo> allScreenshots,
        string? topicId,
        string? reactorVersion)
    {
        var snippetRefs = ExtractSnippetRefs(template.Body);
        var resolvedSnippets = snippetRefs.Count(r => allSnippets.ContainsKey(r));
        var screenshotRefs = ExtractScreenshotRefs(template.Body);
        var resolvedScreenshots = screenshotRefs.Count(r => allScreenshots.ContainsKey(r));
        var assembled = DocAssembler.Assemble(template.Body, allSnippets, allScreenshots, out _, out _, topicId, reactorVersion);
        return (assembled, resolvedSnippets, resolvedScreenshots);
    }

    /// <summary>
    /// Locate the most recently built <c>Reactor.xml</c> (see
    /// <see cref="FindReactorXml"/>) and run the reference generator restricted
    /// to the Hooks category. Returns <c>null</c> when the XML doc file isn't on
    /// disk yet — typical on first compile before <c>dotnet build src/Reactor</c>
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

        var choice = FindReactorXml(repoRoot);
        if (choice is null)
        {
            Console.WriteLine("  (Reactor.xml not found — run `dotnet build src/Reactor` first)");
            return null;
        }

        // Issue #1068: name the input. Selection used to be by configuration
        // order, so a stale Debug build silently won over a fresh Release one
        // and the regenerated pages looked like a legitimate diff. Newest-wins
        // fixes the choice; printing it is what makes a future recurrence
        // diagnosable instead of invisible.
        var xmlPath = choice.Value.Path;
        Console.WriteLine(
            $"  XML: {Rel(repoRoot, xmlPath)} " +
            $"({Stamp(choice.Value.WriteUtc)}, newest of {choice.Value.CandidateCount} candidate(s))");

        var staleFinding = BuildStaleXmlFinding(repoRoot, xmlPath);

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

        // Merge findings; the injector findings join the generator's, and the
        // stale-input warning (issue #1068) joins both. It is a warning, so the
        // caller prints it and `--ci` does not fail on it: it reports that the
        // input *may* predate source, which is a local-loop hazard rather than
        // a defect in the emitted pages.
        var combined = new ReferenceGen.ReferenceGenResult(
            injectedPages,
            result.Findings
                .Concat(injectionFindings)
                .Concat(staleFinding is null
                    ? Array.Empty<ReferenceGen.RefGenFinding>()
                    : new[] { staleFinding })
                .ToList());

        // Write pages to disk so authors and lints can see the output.
        generator.WriteToDisk(combined, outputDir);
        return combined;
    }

    /// <summary>
    /// The <c>Reactor.xml</c> the reference generator reads, together with the
    /// facts the operator needs to judge it: when it was written, and how many
    /// candidates it beat. <c>null</c> when <c>bin</c> is absent or holds no
    /// such file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Issue #1068. This used to sweep <c>Debug</c> then <c>Release</c> and
    /// return the first hit. A leftover Debug build therefore shadowed a
    /// freshly-built Release one, and the generator rewrote
    /// <c>docs/guide/reference/**</c> from stale input while exiting 0 — the
    /// observed symptom being a generated page reintroducing a sentence the
    /// commit had just deleted from source. Configuration order carries no
    /// information about freshness, so it is not used at all now.
    /// </para>
    /// <para>
    /// Returning the timestamp and count rather than just the path is what lets
    /// the caller print the choice without walking <c>bin</c> a second time.
    /// </para>
    /// </remarks>
    internal static (string Path, DateTime WriteUtc, int CandidateCount)? FindReactorXml(string repoRoot)
    {
        var candidates = EnumerateReactorXmlCandidates(repoRoot).ToList();
        var chosen = SelectNewest(candidates);
        return chosen is null
            ? null
            : (chosen, File.GetLastWriteTimeUtc(chosen), candidates.Count);
    }

    /// <summary>
    /// The freshness rule itself, split from discovery so it can be measured:
    /// the newest candidate by last-write time, ties broken on the ordinal
    /// path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ties break on the path so two candidates stamped the same instant still
    /// resolve to the same file on every run. Last-write ordering alone would
    /// leave that choice to enumeration order, which the filesystem does not
    /// promise to keep stable.
    /// </para>
    /// <para>
    /// Taking the sequence as a parameter is what makes that tie-break testable
    /// at all. Fused with the directory walk it is not: a caller cannot choose
    /// the enumeration order, so a fixture cannot tell "sorted deterministically"
    /// apart from "arrived in that order" — a test written that way stayed green
    /// with the <c>ThenBy</c> deleted. Here both orderings are the caller's to
    /// pick.
    /// </para>
    /// </remarks>
    internal static string? SelectNewest(IEnumerable<string> candidates) =>
        candidates
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ThenBy(p => p, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>
    /// What <see cref="EnumerationOptions"/> skips by default. Named so the two
    /// recursive walks below can add <see cref="FileAttributes.ReparsePoint"/>
    /// to it without silently dropping the default.
    /// </summary>
    private const FileAttributes DefaultSkip = FileAttributes.Hidden | FileAttributes.System;

    /// <summary>
    /// Every <c>Reactor.xml</c> under <c>src/Reactor/bin</c>, at any depth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recursive by design. The previous enumeration hard-coded two shapes
    /// (<c>bin/&lt;config&gt;/&lt;tfm&gt;/</c> and
    /// <c>bin/&lt;arch&gt;/&lt;config&gt;/&lt;tfm&gt;/</c>) and an arch
    /// allow-list of <c>x64</c>/<c>ARM64</c>, so any layout outside that set —
    /// a RID-nested publish output, a future architecture — was invisible.
    /// That is the same failure mode as the bug this fixes: an incomplete
    /// enumeration producing a confident answer.
    /// </para>
    /// <para>
    /// Widening to every depth means a copy inside a publish or packaging
    /// output is now a candidate. That is safe rather than merely tolerable:
    /// both MSBuild's <c>Copy</c> task and <see cref="File.Copy(string,string)"/>
    /// preserve the source's last-write time, so a copy carries its origin
    /// build's timestamp and can only win when that build would have won.
    /// </para>
    /// </remarks>
    internal static IEnumerable<string> EnumerateReactorXmlCandidates(string repoRoot)
    {
        var binDir = Path.Combine(repoRoot, "src", "Reactor", "bin");
        if (!Directory.Exists(binDir)) return Array.Empty<string>();

        return Directory.EnumerateFiles(binDir, "Reactor.xml", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            // A locked or permission-denied subtree is not this command's
            // business; skipping it beats aborting reference generation.
            IgnoreInaccessible = true,
            // Don't descend through junctions/symlinks. Two reasons: a nested
            // reparse point can point anywhere, so a candidate found through
            // one isn't this build's output at all; and a loop (bin/x -> bin)
            // has no cycle detection in the runtime's walker, so it recurses
            // until the path length gives out. Skipping applies to entries the
            // walk discovers, not to the root — a `bin` that is itself a
            // junction still enumerates normally.
            AttributesToSkip = DefaultSkip | FileAttributes.ReparsePoint,
        });
    }

    /// <summary>
    /// Warn when the selected <c>Reactor.xml</c> predates the newest C# source
    /// under <c>src/Reactor</c>. Returns <c>null</c> when the XML is at least
    /// as new as every source file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Newest-wins fixes the *choice* between builds but not the case where
    /// every build is stale — source edited, nothing rebuilt — which produces
    /// exactly the same wrong output: pages regenerated from an XML that no
    /// longer reflects the summaries in source. Comparing against source is
    /// what closes that residue; comparing candidates against each other
    /// cannot, because the winner is the newest candidate by construction.
    /// </para>
    /// <para>
    /// Warning severity, deliberately. The pages this produces are internally
    /// consistent with the build they came from — the claim is "your input may
    /// predate your source", which an author can act on and CI cannot.
    /// </para>
    /// <para>
    /// It stays quiet in CI, but not for the reason it might appear: the docs
    /// job does build <c>src/Reactor</c>. It runs
    /// <c>docs compile --no-screenshots --ci</c> without <c>--no-build</c>
    /// (<c>.github/workflows/ci.yml</c>, <c>docs-build</c>), so Phase 2 builds
    /// every doc app, and each one <c>ProjectReference</c>s
    /// <c>src/Reactor/Reactor.csproj</c> — which sets
    /// <c>GenerateDocumentationFile</c>. Phase 5.7 therefore finds a real
    /// <c>Reactor.xml</c> and generates pages. What makes the warning quiet is
    /// the ordering: <c>actions/checkout</c> writes the sources before that
    /// build runs, so the emitted XML always postdates every <c>.cs</c>.
    /// </para>
    /// <para>
    /// Strict comparison, no grace window: a build writes its XML after
    /// compiling its inputs, so a source file stamped after the XML genuinely
    /// postdates the build.
    /// </para>
    /// </remarks>
    internal static ReferenceGen.RefGenFinding? BuildStaleXmlFinding(string repoRoot, string xmlPath)
    {
        var newest = FindNewestReactorSource(repoRoot);
        if (newest is null) return null;

        var xmlUtc = File.GetLastWriteTimeUtc(xmlPath);
        var (sourcePath, sourceUtc) = newest.Value;
        if (sourceUtc <= xmlUtc) return null;

        return new ReferenceGen.RefGenFinding(
            "REACTOR_DOC_REFGEN_W002",
            $"Reactor.xml ({Stamp(xmlUtc)}) predates {Rel(repoRoot, sourcePath)} ({Stamp(sourceUtc)}) — " +
            "the reference pages under docs/guide/reference/ are being generated from a build that " +
            "is older than the source it documents, so an edited <summary> will not appear (and a " +
            "deleted one will come back). Run `dotnet build src/Reactor` and re-run this command.",
            Rel(repoRoot, xmlPath),
            TierLintSeverity.Warning);
    }

    /// <summary>
    /// The newest C# file under <c>src/Reactor</c>, ignoring build output.
    /// Returns <c>null</c> when the directory is absent or holds no sources.
    /// </summary>
    /// <remarks>
    /// Shares <see cref="SelectNewest"/> with candidate selection rather than
    /// carrying its own max loop, so both get the same ordinal tie-break. That
    /// matters more here than it looks: a fresh <c>git clone</c> or
    /// <c>actions/checkout</c> stamps every file it writes at essentially the
    /// same instant, so ties are the normal case rather than the exotic one,
    /// and a plain "strictly newer wins" scan would let enumeration order pick
    /// which file <c>REACTOR_DOC_REFGEN_W002</c> names.
    /// </remarks>
    internal static (string Path, DateTime WriteUtc)? FindNewestReactorSource(string repoRoot)
    {
        var chosen = SelectNewest(EnumerateReactorSources(repoRoot));
        return chosen is null ? null : (chosen, File.GetLastWriteTimeUtc(chosen));
    }

    /// <summary>
    /// Every <c>.cs</c> file under <c>src/Reactor</c> that is actually source.
    /// </summary>
    /// <remarks>
    /// <c>bin</c> and <c>obj</c> are excluded because they are *written by* the
    /// build whose age is being questioned: a generated <c>.g.cs</c> under
    /// <c>obj</c> is always newer than the XML emitted moments later in the
    /// same build, so including them would fire the warning after every
    /// successful build and train readers to ignore it.
    /// </remarks>
    internal static IEnumerable<string> EnumerateReactorSources(string repoRoot)
    {
        var srcDir = Path.Combine(repoRoot, "src", "Reactor");
        if (!Directory.Exists(srcDir)) return Array.Empty<string>();

        return Directory.EnumerateFiles(srcDir, "*.cs", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            // Same reasoning as the candidate walk: don't follow nested
            // junctions/symlinks out of the tree, and don't risk a cycle.
            AttributesToSkip = DefaultSkip | FileAttributes.ReparsePoint,
        }).Where(f => !IsUnderBuildOutput(srcDir, f));
    }

    /// <summary>
    /// True when <paramref name="file"/> sits under a <c>bin</c> or <c>obj</c>
    /// directory somewhere below <paramref name="root"/>.
    /// </summary>
    /// <remarks>
    /// Matched on path *segments* below the root rather than with a substring
    /// test, so a source directory whose name merely contains "bin" (or a repo
    /// checked out under one) is not swept up with the build output.
    /// </remarks>
    private static bool IsUnderBuildOutput(string root, string file)
    {
        var rel = Path.GetRelativePath(root, file);
        foreach (var segment in rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("obj", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Repo-relative, forward-slashed path for operator-facing output.</summary>
    private static string Rel(string repoRoot, string path) =>
        Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

    /// <summary>Fixed-width UTC stamp so two of these can be compared by eye.</summary>
    private static string Stamp(DateTime utc) =>
        utc.ToString("yyyy-MM-ddTHH:mm:ssZ", global::System.Globalization.CultureInfo.InvariantCulture);

    // Normalize emitted Markdown to the host's native line endings. The
    // assembler concatenates template + snippet text with `\n` regardless of
    // platform; writing those bytes directly leaves a CRLF-checkout (Windows
    // default) with a flapping working tree where every generated file shows
    // as modified after a compile. Match git's expected working-tree shape
    // by writing `Environment.NewLine`.
    internal static string NormalizeLineEndings(string text)
    {
        // Collapse CRLF and bare CR down to LF, then re-expand to the host
        // newline. Without the bare-CR pass, a stray `\r` in a snippet or
        // template would survive into the output and produce mixed line
        // endings — which still trips git's autocrlf detection.
        var lf = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return Environment.NewLine == "\n" ? lf : lf.Replace("\n", Environment.NewLine);
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

    /// <summary>
    /// Whether an image-gate finding should fail the compile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The severity a finding declares is the severity that gets honoured. This
    /// exists as a named predicate rather than an inline comparison so the
    /// decision is reachable from a test: the image-ref loop previously set
    /// <c>hasErrors</c> for <em>every</em> finding, which silently promoted
    /// <c>REACTOR_DOC_IMAGE_004</c> — declared <see cref="TierLintSeverity.Warning"/>
    /// — to a build break. Nothing could observe the disagreement, because the
    /// only thing that read the severity was the code that ignored it.
    /// </para>
    /// <para>
    /// Deliberately <c>== Error</c> rather than <c>!= Warning</c>: a severity
    /// added later defaults to non-fatal, which is the recoverable direction.
    /// </para>
    /// </remarks>
    internal static bool IsBuildBreaking(TierLintFinding finding) =>
        finding.Severity == TierLintSeverity.Error;

    internal static IReadOnlySet<string>? ParseScreenshotFilter(string[] args, string? topic)
    {
        var values = GetOptions(args, "--screenshots")
            .Concat(GetOptions(args, "--screenshot"))
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();

        if (values.Count == 0)
            return null;

        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var normalized = value.Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            string fullId;
            if (normalized.Contains('/'))
            {
                fullId = normalized;
            }
            else if (!string.IsNullOrWhiteSpace(topic))
            {
                fullId = $"{topic}/{normalized}";
            }
            else
            {
                throw new ArgumentException(
                    $"Screenshot ref '{value}' must include a topic prefix (for example, 'docking/{value}') when --topic is not set.");
            }

            var slash = fullId.IndexOf('/');
            if (slash <= 0 || slash == fullId.Length - 1)
                throw new ArgumentException($"Screenshot ref '{value}' must use '<topic>/<screenshot-id>'.");
            if (!string.IsNullOrWhiteSpace(topic) &&
                !fullId[..slash].Equals(topic, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Screenshot ref '{value}' does not match --topic {topic}. Use a screenshot from that topic or omit --topic.");
            }

            refs.Add(fullId);
        }

        return refs.Count == 0 ? null : refs;
    }

    private static IEnumerable<string> GetOptions(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                yield return args[i + 1];
        }
    }
}
