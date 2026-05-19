using System.Linq;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

public class TierLintTests
{
    private static DocTemplate Template(DocTier tier, string body = "", string title = "Sample", string winUiRef = "")
    {
        return new DocTemplate
        {
            FilePath = "fake.md.dt",
            Title = title,
            Tier = tier,
            TierDeclared = true,
            WinUiRef = winUiRef,
            Body = body,
        };
    }

    [Fact]
    public void Stub_missing_title_emits_001()
    {
        var t = Template(DocTier.Stub, body: "Body paragraph.", title: "");
        var f = TierLint.Lint(t, t.Body, 0, 0);
        Assert.Contains(f, x => x.Code == "REACTOR_DOC_TIER_001");
    }

    [Fact]
    public void Stub_missing_paragraph_emits_002()
    {
        var t = Template(DocTier.Stub, body: "# Just a heading\n\n```\nfence\n```");
        var f = TierLint.Lint(t, t.Body, 0, 0);
        Assert.Contains(f, x => x.Code == "REACTOR_DOC_TIER_002");
    }

    [Fact]
    public void Stub_with_title_and_paragraph_is_clean()
    {
        var t = Template(DocTier.Stub, body: "A paragraph of body text.");
        var f = TierLint.Lint(t, t.Body, 0, 0);
        Assert.DoesNotContain(f, x => x.Severity == TierLintSeverity.Error);
    }

    [Fact]
    public void Solid_low_snippet_count_emits_003()
    {
        var t = Template(DocTier.Solid, body: SolidBodyTemplate());
        var f = TierLint.Lint(t, t.Body, resolvedSnippetCount: 1, resolvedScreenshotCount: 1);
        Assert.Contains(f, x => x.Code == "REACTOR_DOC_TIER_003");
    }

    [Fact]
    public void Solid_no_screenshot_emits_004()
    {
        var t = Template(DocTier.Solid, body: SolidBodyTemplate());
        var f = TierLint.Lint(t, t.Body, resolvedSnippetCount: 3, resolvedScreenshotCount: 0);
        Assert.Contains(f, x => x.Code == "REACTOR_DOC_TIER_004");
    }

    [Fact]
    public void Solid_no_reference_table_emits_005()
    {
        var body = """
            Lead paragraph.

            ## Tips

            Some tips.

            ## Next Steps

            - [a](a.md)
            - [b](b.md)
            - [c](c.md)
            """;
        var t = Template(DocTier.Solid, body: body);
        var f = TierLint.Lint(t, t.Body, 3, 1);
        Assert.Contains(f, x => x.Code == "REACTOR_DOC_TIER_005");
    }

    [Fact]
    public void Solid_missing_tips_emits_006()
    {
        var body = SolidBodyTemplate().Replace("## Tips", "## NotTips");
        var t = Template(DocTier.Solid, body: body);
        var f = TierLint.Lint(t, t.Body, 3, 1);
        Assert.Contains(f, x => x.Code == "REACTOR_DOC_TIER_006");
    }

    [Fact]
    public void Solid_next_steps_with_too_few_links_emits_007()
    {
        var body = """
            Lead paragraph.

            | Col | Val |
            |-----|-----|
            | a   | 1   |

            ## Tips

            Some tips.

            ## Next Steps

            Just [one link](one.md).
            """;
        var t = Template(DocTier.Solid, body: body);
        var f = TierLint.Lint(t, t.Body, 3, 1);
        Assert.Contains(f, x => x.Code == "REACTOR_DOC_TIER_007");
    }

    [Fact]
    public void Comprehensive_missing_mental_model_emits_008()
    {
        var body = """
            # Short.

            | Col | Val |
            |-----|-----|
            | a   | 1   |

            ## Tips

            Tips.

            ## Patterns

            Patterns.

            ## Common Mistakes

            Mistakes.

            ## Next Steps

            - [a](a.md)
            - [b](b.md)
            - [c](c.md)
            """;
        var t = Template(DocTier.Comprehensive, body: body, winUiRef: "https://example.com/x");
        var f = TierLint.Lint(t, t.Body, 3, 1);
        Assert.Contains(f, x => x.Code == "REACTOR_DOC_TIER_008");
    }

    [Fact]
    public void Comprehensive_missing_caveat_emits_009()
    {
        var body = ComprehensiveBodyTemplate();
        var t = Template(DocTier.Comprehensive, body: body, winUiRef: "https://example.com/x");
        // No Caveats added to template.
        var f = TierLint.Lint(t, t.Body, 3, 1);
        Assert.Contains(f, x => x.Code == "REACTOR_DOC_TIER_009");
    }

    [Fact]
    public void Comprehensive_missing_patterns_emits_010()
    {
        var body = ComprehensiveBodyTemplate().Replace("## Patterns", "## NotPatterns");
        var t = Template(DocTier.Comprehensive, body: body, winUiRef: "https://example.com/x");
        t.Caveats.Add(new CaveatSection { Content = "x" });
        var f = TierLint.Lint(t, t.Body, 3, 1);
        Assert.Contains(f, x => x.Code == "REACTOR_DOC_TIER_010");
    }

    [Fact]
    public void Comprehensive_missing_common_mistakes_emits_011()
    {
        var body = ComprehensiveBodyTemplate().Replace("## Common Mistakes", "## NotMistakes");
        var t = Template(DocTier.Comprehensive, body: body, winUiRef: "https://example.com/x");
        t.Caveats.Add(new CaveatSection { Content = "x" });
        var f = TierLint.Lint(t, t.Body, 3, 1);
        Assert.Contains(f, x => x.Code == "REACTOR_DOC_TIER_011");
    }

    [Fact]
    public void Comprehensive_low_cross_link_count_emits_012()
    {
        // ComprehensiveBodyTemplate has only 3 .md cross-links → below the 5 threshold.
        var body = ComprehensiveBodyTemplate();
        var t = Template(DocTier.Comprehensive, body: body, winUiRef: "https://example.com/x");
        t.Caveats.Add(new CaveatSection { Content = "x" });
        var f = TierLint.Lint(t, t.Body, 3, 1);
        Assert.Contains(f, x => x.Code == "REACTOR_DOC_TIER_012");
    }

    [Fact]
    public void Comprehensive_no_winui_ref_emits_w001_warning()
    {
        var body = ComprehensiveBodyTemplate();
        var t = Template(DocTier.Comprehensive, body: body, winUiRef: "");
        t.Caveats.Add(new CaveatSection { Content = "x" });
        var f = TierLint.Lint(t, t.Body, 3, 1);
        var w001 = f.SingleOrDefault(x => x.Code == "REACTOR_DOC_TIER_W001");
        Assert.NotNull(w001);
        Assert.Equal(TierLintSeverity.Warning, w001!.Severity);
    }

    [Fact]
    public void Pages_without_declared_tier_emit_info_level_only()
    {
        var t = Template(DocTier.Solid, body: "Body paragraph.", title: "");
        t.TierDeclared = false;
        var f = TierLint.Lint(t, t.Body, 0, 0);
        Assert.NotEmpty(f);
        Assert.All(f, x =>
            Assert.True(x.Severity != TierLintSeverity.Error,
                $"Expected info severity for undeclared tier, got {x.Severity} on {x.Code}."));
    }

    // ── Fixture builders ──────────────────────────────────────────────────

    private static string SolidBodyTemplate() => """
        Lead paragraph one. Lead paragraph two.

        | Col | Val |
        |-----|-----|
        | a   | 1   |

        ## Tips

        Some tips.

        ## Next Steps

        - [a](a.md)
        - [b](b.md)
        - [c](c.md)
        """;

    private static string ComprehensiveBodyTemplate()
    {
        // Force a long mental-model lead (>=80 words) before the first heading.
        var lead = string.Join(' ', Enumerable.Repeat("Reactor uses a render-from-state model where state setters drive a reconciliation pass that updates the WinUI tree.", 5));
        return lead + """


            | Col | Val |
            |-----|-----|
            | a   | 1   |

            ## Tips

            Tips.

            ## Patterns

            Patterns.

            ## Common Mistakes

            Mistakes.

            ## Next Steps

            - [a](a.md)
            - [b](b.md)
            - [c](c.md)
            """;
    }
}
