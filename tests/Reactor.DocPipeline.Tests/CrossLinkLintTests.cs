using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Behavioural tests for <see cref="CrossLinkLint"/> — the spec 041 §4.5
/// missed-cross-link analyzer (code <c>REACTOR_DOC_XLINK_001</c>).
/// </summary>
public class CrossLinkLintTests
{
    private static CrossLinkTemplate MakeTemplate(string topicId, string body, string title = "Sample", IReadOnlyList<string>? aliases = null)
        => new(topicId, topicId + ".md.dt", body, title, aliases ?? Array.Empty<string>());

    [Fact]
    public void Linked_concept_does_not_emit_a_finding()
    {
        // The page links UseFocusTrap on first mention. Nothing to flag.
        var page = MakeTemplate("accessibility",
            "Use [`UseFocusTrap`](reference/hooks/UseFocusTrap.md) to trap focus in dialogs.\n");
        var refs = new[] { new CrossLinkConcept("UseFocusTrap", "reference/hooks/UseFocusTrap.md", "reference/hooks/UseFocusTrap.md") };

        var findings = CrossLinkLint.Run(new[] { page }, refs);

        Assert.Empty(findings);
    }

    [Fact]
    public void Missed_link_emits_exactly_one_finding()
    {
        var page = MakeTemplate("accessibility",
            "Reach for UseFocusTrap when a dialog needs to trap keyboard focus.\n");
        var refs = new[] { new CrossLinkConcept("UseFocusTrap", "reference/hooks/UseFocusTrap.md", "reference/hooks/UseFocusTrap.md") };

        var findings = CrossLinkLint.Run(new[] { page }, refs);

        var only = Assert.Single(findings);
        Assert.Equal("REACTOR_DOC_XLINK_001", only.Code);
        Assert.Contains("UseFocusTrap", only.Message);
    }

    [Fact]
    public void Self_reference_is_not_flagged()
    {
        // Page titled "Hooks" with topic id "hooks" — mentioning "Hooks" in
        // body prose is not a missed link to itself.
        var page = MakeTemplate("hooks", "Hooks are functions you call from Render().\n", title: "Hooks");
        // Force the registry to include "Hooks" as a multi-word-equivalent
        // alias so we exercise self-skip even when filtering would normally
        // drop a single-capital-word title.
        page = page with { ConceptAliases = new[] { "Hooks" } };

        var findings = CrossLinkLint.Run(new[] { page }, Array.Empty<CrossLinkConcept>());

        Assert.Empty(findings);
    }

    [Fact]
    public void First_mention_link_satisfies_subsequent_mentions()
    {
        // Author links the FIRST mention but not the second — analyzer should
        // not double-fire. The rule is concept-per-page, not occurrence.
        var body = """
            See [`UseFocusTrap`](reference/hooks/UseFocusTrap.md) for the helper.

            Calling UseFocusTrap inside a modal does the right thing.
            """;
        var page = MakeTemplate("dialogs", body);
        var refs = new[] { new CrossLinkConcept("UseFocusTrap", "reference/hooks/UseFocusTrap.md", "reference/hooks/UseFocusTrap.md") };

        var findings = CrossLinkLint.Run(new[] { page }, refs);

        Assert.Empty(findings);
    }

    [Fact]
    public void XlinkSkip_paragraph_marker_silences_all_concepts_in_paragraph()
    {
        var body = """
            <!-- xlink:skip -->
            UseFocusTrap and UseMutation appear here on purpose without links.

            But the next paragraph still gets enforced — UseFocusTrap missing here.
            """;
        var page = MakeTemplate("comparison", body);
        var refs = new[]
        {
            new CrossLinkConcept("UseFocusTrap", "reference/hooks/UseFocusTrap.md", "reference/hooks/UseFocusTrap.md"),
            new CrossLinkConcept("UseMutation",  "reference/hooks/UseMutation.md",  "reference/hooks/UseMutation.md"),
        };

        var findings = CrossLinkLint.Run(new[] { page }, refs);

        // Only the second paragraph's UseFocusTrap should fire.
        var only = Assert.Single(findings);
        Assert.Contains("UseFocusTrap", only.Message);
    }

    [Fact]
    public void XlinkSkip_named_marker_only_silences_named_phrase()
    {
        var body = """
            <!-- xlink:skip "UseFocusTrap" -->
            UseFocusTrap and UseMutation appear here.
            """;
        var page = MakeTemplate("comparison", body);
        var refs = new[]
        {
            new CrossLinkConcept("UseFocusTrap", "reference/hooks/UseFocusTrap.md", "reference/hooks/UseFocusTrap.md"),
            new CrossLinkConcept("UseMutation",  "reference/hooks/UseMutation.md",  "reference/hooks/UseMutation.md"),
        };

        var findings = CrossLinkLint.Run(new[] { page }, refs);

        // UseFocusTrap silenced, UseMutation not.
        var only = Assert.Single(findings);
        Assert.Contains("UseMutation", only.Message);
    }

    [Fact]
    public void Concept_inside_inline_code_is_ignored()
    {
        var page = MakeTemplate("hooks-internals",
            "The `UseFocusTrap` API name shouldn't trigger; it's a code span.\n");
        var refs = new[] { new CrossLinkConcept("UseFocusTrap", "reference/hooks/UseFocusTrap.md", "reference/hooks/UseFocusTrap.md") };

        var findings = CrossLinkLint.Run(new[] { page }, refs);

        Assert.Empty(findings);
    }

    [Fact]
    public void Concept_inside_fenced_code_block_is_ignored()
    {
        var body = """
            Lead paragraph.

            ```csharp
            // UseFocusTrap here is part of a code sample, not prose
            UseFocusTrap(scope);
            ```

            After the fence, prose continues.
            """;
        var page = MakeTemplate("hooks-internals", body);
        var refs = new[] { new CrossLinkConcept("UseFocusTrap", "reference/hooks/UseFocusTrap.md", "reference/hooks/UseFocusTrap.md") };

        var findings = CrossLinkLint.Run(new[] { page }, refs);

        Assert.Empty(findings);
    }

    [Fact]
    public void Concept_inside_heading_is_ignored()
    {
        var body = "## UseFocusTrap\n\nIntro for the section.\n";
        var page = MakeTemplate("hooks-internals", body);
        var refs = new[] { new CrossLinkConcept("UseFocusTrap", "reference/hooks/UseFocusTrap.md", "reference/hooks/UseFocusTrap.md") };

        var findings = CrossLinkLint.Run(new[] { page }, refs);

        Assert.Empty(findings);
    }

    [Fact]
    public void Concept_inside_ai_caveat_block_is_ignored()
    {
        // ai:caveat blocks are styled callouts; the rule applies to body
        // prose, not callouts. Verify mentions inside the block are skipped.
        var body = """
            <!-- ai:caveat -->
            UseFocusTrap can fight with WinUI's own focus restoration.
            <!-- /ai:caveat -->
            """;
        var page = MakeTemplate("accessibility", body);
        var refs = new[] { new CrossLinkConcept("UseFocusTrap", "reference/hooks/UseFocusTrap.md", "reference/hooks/UseFocusTrap.md") };

        var findings = CrossLinkLint.Run(new[] { page }, refs);

        Assert.Empty(findings);
    }

    [Fact]
    public void Single_capital_word_concept_is_dropped_from_registry()
    {
        // Reference page name "Focus" is a common English noun — IsConceptShape
        // should filter it out and the analyzer should not flag prose that
        // mentions the word "focus" (or "Focus").
        var page = MakeTemplate("input", "Press Tab to move focus to the next field.\n");
        var refs = new[] { new CrossLinkConcept("Focus", "reference/hooks/Focus.md", "reference/hooks/Focus.md") };

        var findings = CrossLinkLint.Run(new[] { page }, refs);

        Assert.Empty(findings);
    }

    [Fact]
    public void IsConceptShape_rejects_single_capital_words_and_accepts_internal_caps_or_multi_word()
    {
        Assert.False(CrossLinkLint.IsConceptShape("Reactor"));
        Assert.False(CrossLinkLint.IsConceptShape("Focus"));
        Assert.True(CrossLinkLint.IsConceptShape("UseFocusTrap"));
        Assert.True(CrossLinkLint.IsConceptShape("DataGrid"));
        Assert.True(CrossLinkLint.IsConceptShape("Element Pool"));
        Assert.True(CrossLinkLint.IsConceptShape("Reactor vs XAML"));
        Assert.False(CrossLinkLint.IsConceptShape(""));
        Assert.False(CrossLinkLint.IsConceptShape("   "));
    }

    [Fact]
    public void Multi_word_title_in_prose_emits_finding()
    {
        // Title "Element Pool" mentioned in a page that isn't element-pool.
        var owner = MakeTemplate("element-pool", "Element Pool deep dive.\n", title: "Element Pool");
        var consumer = MakeTemplate("reconciliation",
            "Reconciliation works with the Element Pool to recycle controls.\n", title: "Reconciliation");

        var findings = CrossLinkLint.Run(new[] { owner, consumer }, Array.Empty<CrossLinkConcept>());

        var only = Assert.Single(findings);
        Assert.Contains("Element Pool", only.Message);
        Assert.Equal("reconciliation.md.dt", only.FilePath);
    }

    [Fact]
    public void ConceptAlias_opts_in_a_single_word_concept()
    {
        // "Windows" — single-capital noun, normally filtered out — but if the
        // owning template declares it as a concept-alias, the registry keeps
        // it and prose mentions on other pages get flagged.
        var owner = MakeTemplate("windows", "Top-level windows and tray icons.\n",
            title: "Windows", aliases: new[] { "Windows" });
        var consumer = MakeTemplate("packaging",
            "Reactor apps target Windows for desktop deployment.\n", title: "Packaging");

        var findings = CrossLinkLint.Run(new[] { owner, consumer }, Array.Empty<CrossLinkConcept>());

        var only = Assert.Single(findings);
        Assert.Contains("Windows", only.Message);
    }

    [Fact]
    public void Concept_inside_link_text_marks_concept_as_already_linked_globally()
    {
        // Author linked UseFocusTrap once; subsequent unlinked mentions are
        // acceptable per spec ("Once linked on this page, no relink needed").
        var body = """
            See [`UseFocusTrap`](reference/hooks/UseFocusTrap.md) for the API.

            ## Pattern

            Inside a modal, call UseFocusTrap with the dialog root.
            """;
        var page = MakeTemplate("dialogs", body);
        var refs = new[] { new CrossLinkConcept("UseFocusTrap", "reference/hooks/UseFocusTrap.md", "reference/hooks/UseFocusTrap.md") };

        var findings = CrossLinkLint.Run(new[] { page }, refs);

        Assert.Empty(findings);
    }
}
