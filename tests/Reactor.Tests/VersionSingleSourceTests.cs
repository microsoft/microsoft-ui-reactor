// Repository-content guards for the "single source of truth for the docs
// version" feature (PR: Drive docs version numbers from a single source).
//
// The problem these lock in: the public package version (e.g. 0.1.0-preview.11)
// used to be hand-copied into ~8 prose spots across README.md and the guide
// templates, and drifted every release. The fix pins it ONCE in
// <ReactorPublicVersion> (root Directory.Build.props) and substitutes it into
// the guide via the {{reactorVersion}} token at `mur docs compile` time; README
// is made version-agnostic. These tests fail the instant any leg of that wiring
// is removed or a raw version literal creeps back into docs prose.
//
// Namespace note: in Microsoft.UI.Reactor.Tests, `Microsoft.UI.System` shadows
// `System`, so any `System.`-qualified path must be written `global::System.`.
// Bare type names (Path, File, Directory) come in via ImplicitUsings and are fine.

using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public sealed class VersionSingleSourceTests
{
    // Matches a concrete published prerelease literal like "0.1.0-preview.11".
    // Deliberately narrow: the local sentinel "0.0.0-local" does NOT match.
    const string PreviewLiteralPattern = @"\d+\.\d+\.\d+-preview\.\d+";

    const string VersionToken = "{{reactorVersion}}";

    // ── Guard 1: no raw version literal in docs prose ──────────────────────
    //
    // Scanning ONLY the guide templates (excluding _skeletons/, which use a
    // different {{REPLACE_ME}} placeholder mechanism) and README.md naturally
    // excludes docs/specs/** (historical version strings) and the
    // PackLocalFrameworkVersionTests fixtures (version-sort inputs), which
    // legitimately contain preview.N literals.

    [Fact]
    public void No_raw_version_literal_in_guide_templates()
    {
        var repoRoot = FindRepoRoot();
        var templatesDir = Path.Join(repoRoot, "docs", "_pipeline", "templates");
        Assert.True(Directory.Exists(templatesDir), $"Expected '{templatesDir}' to exist.");

        // _skeletons/ are authoring scaffolds, not compiled — different token scheme.
        var templateFiles = Directory
            .EnumerateFiles(templatesDir, "*.md.dt", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/_skeletons/", global::System.StringComparison.Ordinal));

        var offenders = new List<string>();
        foreach (var file in templateFiles)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (global::System.Text.RegularExpressions.Regex.IsMatch(lines[i], PreviewLiteralPattern))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Guide templates must reference the version via the {{reactorVersion}} token, not a hardcoded " +
            $"'<major>.<minor>.<patch>-preview.<n>' literal (single source: <ReactorPublicVersion> in " +
            $"Directory.Build.props). Offending lines:\n  {string.Join("\n  ", offenders)}");
    }

    [Fact]
    public void No_raw_version_literal_in_readme()
    {
        var (path, text) = ReadRepoFile("README.md");
        var match = global::System.Text.RegularExpressions.Regex.Match(text, PreviewLiteralPattern);
        Assert.False(
            match.Success,
            $"'{path}' must stay version-agnostic (name no package version; link to NuGet / Releases instead). " +
            $"Found the literal '{match.Value}'. `mur docs compile` does not touch README, so a version here " +
            "would silently drift every release.");
    }

    [Fact]
    public void Version_token_is_present_in_the_versioned_guide_templates()
    {
        // Positive counterpart to the negative guards: proves the token wiring
        // is actually in place (not that the literals were merely deleted).
        var repoRoot = FindRepoRoot();
        var templatesDir = Path.Join(repoRoot, "docs", "_pipeline", "templates");
        foreach (var rel in new[] { "getting-started.md.dt", "packaging.md.dt" })
        {
            var path = Path.Join(templatesDir, rel);
            var text = File.ReadAllText(path);
            Assert.True(
                text.Contains(VersionToken, global::System.StringComparison.Ordinal),
                $"'{rel}' must reference the version via the {VersionToken} token — the single-source " +
                "substitution point. If it is gone, the page no longer tracks <ReactorPublicVersion>.");
        }
    }

    // ── Guard 2: the single source + the collapsed csproj fallback ─────────

    [Fact]
    public void DirectoryBuildProps_defines_ReactorPublicVersion()
    {
        var (path, text) = ReadRepoFile("Directory.Build.props");
        var match = global::System.Text.RegularExpressions.Regex.Match(
            text, @"<ReactorPublicVersion>\s*([^<]+?)\s*</ReactorPublicVersion>");
        Assert.True(match.Success, $"'{path}' must define <ReactorPublicVersion> — the single source of the docs version.");
        Assert.Matches(@"^\d+\.\d+\.\d+", match.Groups[1].Value.Trim());
    }

    [Fact]
    public void TemplatesCsproj_fallback_derives_from_ReactorPublicVersion()
    {
        // The templates csproj's MicrosoftUIReactorVersion fallback default must
        // derive from $(ReactorPublicVersion), NOT carry its own literal — that
        // is what collapses the repo to ONE framework-version literal.
        var (path, text) = ReadRepoFile(Path.Join(
            "tools", "Templates", "Microsoft.UI.Reactor.Templates.csproj"));

        Assert.Matches(
            @"<MicrosoftUIReactorVersion\b[^>]*>\s*\$\(ReactorPublicVersion\)\s*</MicrosoftUIReactorVersion>",
            text);

        var literalFallback = global::System.Text.RegularExpressions.Regex.IsMatch(
            text, @"<MicrosoftUIReactorVersion\b[^>]*>\s*" + PreviewLiteralPattern);
        Assert.False(
            literalFallback,
            $"'{path}' hardcodes a preview version in <MicrosoftUIReactorVersion>. Derive the fallback from " +
            "$(ReactorPublicVersion) instead so the repo has exactly one framework-version literal.");
    }

    // ── Guard 3: CI compiled-docs freshness gate is wired ─────────────────
    //
    // Widened for issue #1052. This guard used to pin a two-file
    // `git diff --exit-code`, which was the whole defect: the `docs-build` job
    // recompiles ~190 generated files under docs/guide and then judged two of
    // them. Version freshness is now a *subset* of what the gate covers — a
    // ReactorPublicVersion bump that skipped a recompile shows up as drift in
    // getting-started.md / packaging.md like any other staleness — so this test
    // pins the general gate rather than the version-specific one it replaced.

    [Fact]
    public void CiWorkflow_gates_compiled_docs_freshness()
    {
        var (path, text) = ReadRepoFile(Path.Join(".github", "workflows", "ci.yml"));
        var step = ExtractYamlStep(text, "Verify compiled docs are committed");
        Assert.False(step is null,
            $"'{path}' has no compiled-docs freshness step — the gate that fails a PR whose committed " +
            "docs/guide output differs from a fresh compile (a moved snippet=\"source:...\" region, an " +
            "edited template, or a ReactorPublicVersion bump that was never recompiled).");

        // The trailing newline is load-bearing: it pins the pathspec to the whole
        // tree. Narrowing it back to a hand-picked subset (`-- docs/guide/foo.md`)
        // is the exact regression this guard exists to catch, and it would still
        // satisfy a substring match without the newline.
        Assert.Contains(
            "git status --porcelain --untracked-files=all -- docs/guide\n",
            step!, global::System.StringComparison.Ordinal);

        // Non-vacuous: also require the failure path. Leaving the status command
        // in place while making the step succeed (exit 0) would defeat the gate
        // but keep the substring above — these assertions catch that.
        Assert.Contains("if ($dirty)", step!, global::System.StringComparison.Ordinal);
        Assert.Contains("exit 1", step!, global::System.StringComparison.Ordinal);

        // A clean tree only means "fresh" if the compile actually rewrote the
        // tree, so the gate's own precondition must stay.
        Assert.Contains("Documentation compiled successfully.", step!, global::System.StringComparison.Ordinal);

        // The skipped-phase detector, and the allow-list that keeps it from
        // firing on the skips this invocation legitimately produces (Phase 3
        // capture, via --no-screenshots) plus the unimplemented Phase 5 and the
        // build phase. Assert the allow-list contents, not just that a regex
        // exists: widening it to include 5.5 (diagrams) or 5.7 (reference)
        // would silently stop the gate covering those pages.
        //
        // The other way a compile can exit 0 without regenerating — Phase 5.7
        // bailing on a missing Reactor.xml / reference-map.yaml — is deliberately
        // NOT pinned here. It moved into `docs compile --ci` itself, where the
        // state lives, and is covered by ReferenceStalenessWiringTests. Grepping
        // the log for those messages failed open on a reword; the exit code does
        // not.
        Assert.Contains(@"Phase (?<n>[0-9.]+):", step!, global::System.StringComparison.Ordinal);
        Assert.Contains(@"-notin @('2', '3', '5')", step!, global::System.StringComparison.Ordinal);
        Assert.Contains("$unexpected", step!, global::System.StringComparison.Ordinal);
    }

    [Fact]
    public void CiWorkflow_compile_step_writes_the_log_the_freshness_gate_reads()
    {
        // The gate above reads its precondition out of a log the compile step
        // tees. If the two ever name different paths the gate fails closed
        // ("No compile log at ..."), which is safe but reports a missing file
        // rather than the decoupling that caused it — so bind them here.
        var (path, text) = ReadRepoFile(Path.Join(".github", "workflows", "ci.yml"));
        var compile = ExtractYamlStep(text, "Compile docs");
        var gate = ExtractYamlStep(text, "Verify compiled docs are committed");

        Assert.False(compile is null, $"'{path}' has no 'Compile docs' step.");
        Assert.False(gate is null, $"'{path}' has no 'Verify compiled docs are committed' step.");

        const string logPath = "$env:RUNNER_TEMP/docs-compile.log";
        Assert.Contains("Tee-Object", compile!, global::System.StringComparison.Ordinal);
        Assert.Contains(logPath, compile!, global::System.StringComparison.Ordinal);
        Assert.Contains(logPath, gate!, global::System.StringComparison.Ordinal);
    }

    [Fact]
    public void CiWorkflow_arms_docs_build_for_a_docs_guide_only_edit()
    {
        // The freshness gate lives in `docs-build`, which is armed by `non-md`.
        // Hand-editing a generated page under docs/guide — the likeliest way to
        // introduce the drift the gate catches — is a pure-.md change, so
        // non-md is false and the job would skip the one check that change type
        // exists for. A dedicated `compiled-docs` filter re-arms it.
        var (path, text) = ReadRepoFile(Path.Join(".github", "workflows", "ci.yml"));

        Assert.Contains(
            "compiled-docs: ${{ steps.filter.outputs.compiled-docs }}",
            text, global::System.StringComparison.Ordinal);
        Assert.Contains(
            @"grep -qE '^docs/guide/'",
            text, global::System.StringComparison.Ordinal);

        var job = ExtractYamlJob(text, "docs-build");
        Assert.False(job is null, $"'{path}' no longer defines a `docs-build` job.");
        Assert.Contains(
            "needs.changes.outputs.compiled-docs == 'true'",
            job!, global::System.StringComparison.Ordinal);
    }

    // ── Guard 4: release-time tag-vs-property consistency guard ────────────

    [Fact]
    public void ReleaseWorkflow_guards_tag_matches_ReactorPublicVersion()
    {
        var (path, text) = ReadRepoFile(Path.Join(".github", "workflows", "release.yml"));
        var step = ExtractYamlStep(text, "Verify docs version matches release tag");
        Assert.False(step is null,
            $"'{path}' has no tag-vs-ReactorPublicVersion guard — the check that a release tag can't diverge " +
            "from the docs' single source of truth.");
        Assert.Contains("steps.version.outputs.is_tag == 'true'", step!, global::System.StringComparison.Ordinal);
        Assert.Contains("ReactorPublicVersion", step!, global::System.StringComparison.Ordinal);
        Assert.Contains("steps.version.outputs.version", step!, global::System.StringComparison.Ordinal);
        // Non-vacuous: the guard must be gated on tag pushes via `if:` AND
        // actually compare + fail on mismatch. Merely mentioning the token names
        // in a Write-Host/comment (with the compare/exit removed) would satisfy
        // the three Contains above but not these.
        Assert.Contains("if: steps.version.outputs.is_tag == 'true'", step!, global::System.StringComparison.Ordinal);
        Assert.Contains("-ne $tagVersion", step!, global::System.StringComparison.Ordinal);
        Assert.Contains("exit 1", step!, global::System.StringComparison.Ordinal);
    }

    // ── helpers (modeled on TemplateMetadataTests) ─────────────────────────

    static string? ExtractYamlStep(string yaml, string stepName)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith("- name:", global::System.StringComparison.Ordinal) &&
                t.Substring("- name:".Length).Trim() == stepName)
            {
                start = i;
                break;
            }
        }
        if (start < 0) return null;

        int end = lines.Length;
        for (int i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("- name:", global::System.StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }
        return string.Join("\n", lines[start..end]);
    }

    /// <summary>
    /// Returns the lines of the named job (a two-space-indented
    /// <c>&lt;name&gt;:</c> key under <c>jobs:</c>) up to the next job at the
    /// same indent, or null.
    /// </summary>
    static string? ExtractYamlJob(string yaml, string jobName)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i] == $"  {jobName}:")
            {
                start = i;
                break;
            }
        }
        if (start < 0) return null;

        int end = lines.Length;
        for (int i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            // A sibling job: exactly two leading spaces, then a key.
            if (line.Length > 2 && line[0] == ' ' && line[1] == ' ' && line[2] != ' ' &&
                line.TrimEnd().EndsWith(":", global::System.StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }
        return string.Join("\n", lines[start..end]);
    }

    static (string path, string text) ReadRepoFile(string repoRelativePath)
    {
        var path = Path.Join(FindRepoRoot(), repoRelativePath);
        Assert.True(File.Exists(path), $"Expected '{path}' to exist; file moved or removed?");
        return (path, File.ReadAllText(path));
    }

    static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Join(dir, "Reactor.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        return dir!;
    }
}
