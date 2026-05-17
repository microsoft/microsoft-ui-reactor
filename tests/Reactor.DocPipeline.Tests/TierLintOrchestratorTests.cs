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
