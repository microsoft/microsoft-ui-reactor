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
        var templatesDir = Path.Combine(repoRoot, "docs", "_pipeline", "templates");
        Assert.True(Directory.Exists(templatesDir), $"Expected '{templatesDir}' to exist.");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(templatesDir, "*.md.dt", SearchOption.AllDirectories))
        {
            // _skeletons/ are authoring scaffolds, not compiled — different token scheme.
            if (file.Replace('\\', '/').Contains("/_skeletons/", global::System.StringComparison.Ordinal))
                continue;

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
        foreach (var rel in new[] { "getting-started.md.dt", "packaging.md.dt" })
        {
            var path = Path.Combine(repoRoot, "docs", "_pipeline", "templates", rel);
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
        var (path, text) = ReadRepoFile(Path.Combine(
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

    // ── Guard 3: CI docs-freshness gate is wired ──────────────────────────

    [Fact]
    public void CiWorkflow_gates_docs_version_freshness()
    {
        var (path, text) = ReadRepoFile(Path.Combine(".github", "workflows", "ci.yml"));
        var step = ExtractYamlStep(text, "Verify docs version substitution is committed");
        Assert.False(step is null,
            $"'{path}' has no docs version freshness step — the gate that fails a PR which bumps " +
            "ReactorPublicVersion / edits a template without recompiling the guide.");
        Assert.Contains(
            "git diff --exit-code -- docs/guide/getting-started.md docs/guide/packaging.md",
            step!, global::System.StringComparison.Ordinal);
    }

    // ── Guard 4: release-time tag-vs-property consistency guard ────────────

    [Fact]
    public void ReleaseWorkflow_guards_tag_matches_ReactorPublicVersion()
    {
        var (path, text) = ReadRepoFile(Path.Combine(".github", "workflows", "release.yml"));
        var step = ExtractYamlStep(text, "Verify docs version matches release tag");
        Assert.False(step is null,
            $"'{path}' has no tag-vs-ReactorPublicVersion guard — the check that a release tag can't diverge " +
            "from the docs' single source of truth.");
        Assert.Contains("steps.version.outputs.is_tag == 'true'", step!, global::System.StringComparison.Ordinal);
        Assert.Contains("ReactorPublicVersion", step!, global::System.StringComparison.Ordinal);
        Assert.Contains("steps.version.outputs.version", step!, global::System.StringComparison.Ordinal);
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

    static (string path, string text) ReadRepoFile(string repoRelativePath)
    {
        var path = Path.Combine(FindRepoRoot(), repoRelativePath);
        Assert.True(File.Exists(path), $"Expected '{path}' to exist; file moved or removed?");
        return (path, File.ReadAllText(path));
    }

    static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Reactor.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        return dir!;
    }
}
