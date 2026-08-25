using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Behavioural tests for <see cref="TierLintOrchestrator"/> — the spec
/// 041 §5.1 standalone tier-lint surface that backs <c>mur docs
/// check-tier</c>. Each test stands up a temp directory with the
/// minimum file shape the orchestrator expects (an apps dir + a
/// templates dir) and asserts on the lint findings.
/// </summary>
public class TierLintOrchestratorTests : IDisposable
{
    private readonly string _root;
    private readonly string _appsDir;
    private readonly string _templatesDir;

    public TierLintOrchestratorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "reactor-check-tier-tests-" + Guid.NewGuid().ToString("N"));
        _appsDir = Path.Combine(_root, "docs", "_pipeline", "apps");
        _templatesDir = Path.Combine(_root, "docs", "_pipeline", "templates");
        Directory.CreateDirectory(_appsDir);
        Directory.CreateDirectory(_templatesDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void WriteTemplate(string topicId, string content)
    {
        var path = Path.Combine(_templatesDir, topicId + ".md.dt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Returns_zero_findings_when_no_templates_present()
    {
        var result = TierLintOrchestrator.Run(_root, _appsDir, _templatesDir);

        Assert.Equal(0, result.TemplatesScanned);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Stub_template_with_title_and_paragraph_is_clean()
    {
        WriteTemplate("intro", """
            ---
            title: Intro
            order: 1
            tier: stub
            ---

            # Intro

            A paragraph of body text.
            """);

        var result = TierLintOrchestrator.Run(_root, _appsDir, _templatesDir);

        Assert.Equal(1, result.TemplatesScanned);
        Assert.DoesNotContain(result.Findings, f => f.Severity == TierLintSeverity.Error);
    }

    [Fact]
    public void Solid_template_missing_tips_emits_006()
    {
        // Solid bar: needs >=3 snippets (we count source: ones if resolvable;
        // none here so TIER_003 will also fire) + reference table + Tips +
        // Next Steps. The point of this test is the `## Tips` omission.
        WriteTemplate("missing-tips", """
            ---
            title: Missing Tips
            order: 1
            tier: solid
            ---

            # Missing Tips

            Lead paragraph one. Lead paragraph two.

            | Col | Val |
            |-----|-----|
            | a   | 1   |

            ## NotTips

            Some tips.

            ## Next Steps

            - [a](a.md)
            - [b](b.md)
            - [c](c.md)
            """);

        var result = TierLintOrchestrator.Run(_root, _appsDir, _templatesDir);

        Assert.Equal(1, result.TemplatesScanned);
        Assert.Contains(result.Findings, f => f.Code == "REACTOR_DOC_TIER_006");
    }

    [Fact]
    public void Topic_filter_restricts_scanned_templates()
    {
        WriteTemplate("alpha", """
            ---
            title: Alpha
            order: 1
            tier: stub
            ---

            # Alpha

            Body.
            """);
        WriteTemplate("beta", """
            ---
            title: Beta
            order: 2
            tier: stub
            ---

            # Beta

            Body.
            """);

        var result = TierLintOrchestrator.Run(_root, _appsDir, _templatesDir, topic: "alpha");

        Assert.Equal(1, result.TemplatesScanned);
    }

    // ── Topic → doc-app binding ───────────────────────────────────────────

    private void WriteApp(string appId, params string[] snippetIds)
    {
        var dir = Path.Combine(_appsDir, appId);
        Directory.CreateDirectory(dir);
        var body = string.Join("\n\n", snippetIds.Select(id => $"""
            // <snippet:{id}>
            var {id.Replace('-', '_')} = 1;
            // </snippet:{id}>
            """));
        File.WriteAllText(Path.Combine(dir, "App.cs"), body);
    }

    private const string SolidTemplateShell = """
        ---
        title: Async Resources
        order: 1
        tier: solid
        app: {APP}
        ---

        # Async Resources

        Lead paragraph one. Lead paragraph two.

        ```csharp snippet="{APP}/one"
        ```

        ```csharp snippet="{APP}/two"
        ```

        ```csharp snippet="{APP}/three"
        ```

        | Col | Val |
        |-----|-----|
        | a   | 1   |

        ## Tips

        A tip.

        ## Next Steps

        - [a](a.md)
        - [b](b.md)
        - [c](c.md)
        """;

    /// <summary>
    /// The regression oracle for the fabricated <c>REACTOR_DOC_TIER_003</c>.
    /// App discovery used to filter by directory name against the topic id;
    /// for a topic whose app is named differently — <c>async-resources</c> →
    /// <c>async-resources-cookbook</c>, and every <c>recipes/&lt;x&gt;</c> →
    /// <c>recipe-&lt;x&gt;</c> — that discovered no app, so every
    /// <c>snippet=</c> failed to resolve and the lint reported "found 0"
    /// against a page that was entirely correct.
    ///
    /// Differential: byte-identical page content must lint identically
    /// whether or not the app directory happens to share the topic's name.
    /// The matching-name arm is the positive control — it passed even with
    /// the bug present, so it pins that any difference comes from the name
    /// mismatch rather than from a malformed fixture.
    /// </summary>
    [Fact]
    public void Topic_lints_identically_whether_or_not_its_app_dir_shares_its_name()
    {
        WriteApp("matching", "one", "two", "three");
        WriteTemplate("matching", SolidTemplateShell.Replace("{APP}", "matching"));

        WriteApp("async-resources-cookbook", "one", "two", "three");
        WriteTemplate("async-resources", SolidTemplateShell.Replace("{APP}", "async-resources-cookbook"));

        var control = TierLintOrchestrator.Run(_root, _appsDir, _templatesDir, topic: "matching");
        var subject = TierLintOrchestrator.Run(_root, _appsDir, _templatesDir, topic: "async-resources");

        Assert.Equal(1, control.TemplatesScanned);
        Assert.Equal(1, subject.TemplatesScanned);

        // The control resolves its snippets, so TIER_003 must be absent from
        // it — if it were present, the equality below would be satisfied by
        // both arms being broken.
        Assert.DoesNotContain(control.Findings, f => f.Code == "REACTOR_DOC_TIER_003");
        Assert.DoesNotContain(subject.Findings, f => f.Code == "REACTOR_DOC_TIER_003");

        Assert.Equal(
            control.Findings.Select(f => f.Code).OrderBy(c => c, StringComparer.Ordinal),
            subject.Findings.Select(f => f.Code).OrderBy(c => c, StringComparer.Ordinal));
    }

    /// <summary>
    /// A nested topic id (<c>recipes/login</c>) is never a directory name
    /// under <c>apps/</c>, so this shape was un-lintable via <c>--topic</c>
    /// for all ten shipping recipes.
    /// </summary>
    [Fact]
    public void Nested_topic_id_resolves_its_flat_app_directory()
    {
        WriteApp("recipe-login", "one", "two", "three");
        WriteTemplate("recipes/login", SolidTemplateShell.Replace("{APP}", "recipe-login"));

        var result = TierLintOrchestrator.Run(_root, _appsDir, _templatesDir, topic: "recipes/login");

        Assert.Equal(1, result.TemplatesScanned);
        Assert.DoesNotContain(result.Findings, f => f.Code == "REACTOR_DOC_TIER_003");
    }

    /// <summary>
    /// The app set is still narrowed to what the topic needs — the fix must
    /// not silently degrade <c>--topic</c> into "discover and extract every
    /// app", which would undo the point of the fast inner loop.
    /// </summary>
    [Fact]
    public void Topic_filter_does_not_pull_in_unrelated_apps()
    {
        WriteApp("async-resources-cookbook", "one", "two", "three");
        WriteApp("unrelated-app", "one", "two", "three");
        WriteTemplate("async-resources", SolidTemplateShell.Replace("{APP}", "async-resources-cookbook"));

        var ids = CompileCommand.ResolveAppIds(
            CompileCommand.DiscoverTemplates(_templatesDir, "async-resources"));

        Assert.Contains("async-resources-cookbook", ids);
        Assert.DoesNotContain("unrelated-app", ids);
        Assert.Single(CompileCommand.DiscoverApps(_appsDir, ids));
    }

    /// <summary>
    /// A page may borrow a snippet from another topic's app; the app id is
    /// the reference's leading segment, so that app has to be discovered too.
    /// </summary>
    [Fact]
    public void Cross_app_snippet_reference_pulls_in_the_other_app()
    {
        WriteApp("recipe-login", "one", "two", "three");
        WriteApp("shared-helpers", "helper");
        WriteTemplate("recipes/login",
            SolidTemplateShell.Replace("{APP}", "recipe-login")
                .Replace("snippet=\"recipe-login/three\"", "snippet=\"shared-helpers/helper\""));

        var ids = CompileCommand.ResolveAppIds(
            CompileCommand.DiscoverTemplates(_templatesDir, "recipes/login"));

        Assert.Contains("recipe-login", ids);
        Assert.Contains("shared-helpers", ids);

        var result = TierLintOrchestrator.Run(_root, _appsDir, _templatesDir, topic: "recipes/login");
        Assert.DoesNotContain(result.Findings, f => f.Code == "REACTOR_DOC_TIER_003");
    }

    [Fact]
    public void Tier_filter_excludes_non_matching_tiers()
    {
        WriteTemplate("a-stub", """
            ---
            title: A Stub
            order: 1
            tier: stub
            ---

            # A Stub

            Body.
            """);
        WriteTemplate("a-solid", """
            ---
            title: A Solid
            order: 2
            tier: solid
            ---

            # A Solid

            Body.
            """);

        var result = TierLintOrchestrator.Run(_root, _appsDir, _templatesDir, tierFilter: DocTier.Stub);

        Assert.Equal(1, result.TemplatesScanned);
    }

    [Fact]
    public void Undeclared_tier_emits_info_severity_only()
    {
        // No `tier:` field — orchestrator treats this as info-only per
        // TierLint's existing behaviour.
        WriteTemplate("no-tier", """
            ---
            title: ""
            order: 1
            ---

            (no body paragraph here, only a fence)

            ```
            fence
            ```
            """);

        var result = TierLintOrchestrator.Run(_root, _appsDir, _templatesDir);

        Assert.Equal(1, result.TemplatesScanned);
        Assert.NotEmpty(result.Findings);
        Assert.All(result.Findings, f =>
            Assert.True(f.Severity != TierLintSeverity.Error,
                $"Expected info severity for undeclared tier, got {f.Severity} on {f.Code}."));
    }

    [Fact]
    public void Skeletons_directory_is_excluded_from_scan()
    {
        // Templates under `_skeletons/` are author scaffolds and must be
        // skipped by discovery — they intentionally fail tier-lint.
        var skeletonDir = Path.Combine(_templatesDir, "_skeletons");
        Directory.CreateDirectory(skeletonDir);
        File.WriteAllText(Path.Combine(skeletonDir, "scaffold.md.dt"), """
            ---
            title: ""
            tier: stub
            ---
            """);
        WriteTemplate("real", """
            ---
            title: Real
            order: 1
            tier: stub
            ---

            # Real

            Body.
            """);

        var result = TierLintOrchestrator.Run(_root, _appsDir, _templatesDir);

        Assert.Equal(1, result.TemplatesScanned);
    }
}
