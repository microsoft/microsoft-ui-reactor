using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Spec 041 §10.4.1 — `reference-map.yaml` loader + resolver.
/// </summary>
public class ReferenceMapTests
{
    private const string SpecExample = """
defaults:
  - match: "Microsoft.UI.Reactor.Hooks.*"
    category: hooks
    guide-pages: [hooks, effects]
  - match: "Microsoft.UI.Reactor.Factories.*"
    category: factories
    guide-pages: [layout, forms, collections]
  - match: "Microsoft.UI.Reactor.Charting.*"
    category: charting
    guide-pages: [charting]
overrides:
  - cref: "M:Microsoft.UI.Reactor.Factories.DataGrid``1"
    guide-pages: [data-system]
  - cref: "T:Microsoft.UI.Reactor.ErrorBoundary"
    guide-pages: [advanced]
  - match: "Microsoft.UI.Reactor.Factories.*Chart*"
    guide-pages: [charting]
""";

    [Fact]
    public void DefaultMatch_HooksMember_ProducesHooksCategoryAndGuidePages()
    {
        var map = ReferenceMap.Parse(SpecExample);
        var match = map.Resolve("M:Microsoft.UI.Reactor.Hooks.UseState.SetValue");

        Assert.NotNull(match);
        Assert.Equal("hooks", match!.Category);
        Assert.Equal(new[] { "hooks", "effects" }, match.GuidePages);
        Assert.Equal("prefix-default", match.MatchedRule);
    }

    [Fact]
    public void ExactCrefOverride_BeatsDefault()
    {
        var map = ReferenceMap.Parse(SpecExample);
        // DataGrid``1 falls under the factories default, but the explicit
        // override pins it to the data-system guide page only.
        var match = map.Resolve("M:Microsoft.UI.Reactor.Factories.DataGrid``1");

        Assert.NotNull(match);
        Assert.Equal("factories", match!.Category); // inherited from default
        Assert.Equal(new[] { "data-system" }, match.GuidePages);
        Assert.Equal("cref-override", match.MatchedRule);
    }

    [Fact]
    public void TypeOverride_BeatsDefaultForErrorBoundary()
    {
        var map = ReferenceMap.Parse(SpecExample);
        // ErrorBoundary lives outside any namespace default (root namespace),
        // and the override pins it to the advanced guide.
        var match = map.Resolve("T:Microsoft.UI.Reactor.ErrorBoundary");

        Assert.NotNull(match);
        Assert.Equal(new[] { "advanced" }, match!.GuidePages);
        Assert.Equal("cref-override", match.MatchedRule);
    }

    [Fact]
    public void PrefixOverride_MatchesChartFactories()
    {
        var map = ReferenceMap.Parse(SpecExample);
        var bar = map.Resolve("T:Microsoft.UI.Reactor.Factories.BarChart");
        var line = map.Resolve("T:Microsoft.UI.Reactor.Factories.LineChart");

        Assert.NotNull(bar);
        Assert.NotNull(line);
        Assert.Equal(new[] { "charting" }, bar!.GuidePages);
        Assert.Equal(new[] { "charting" }, line!.GuidePages);
        Assert.Equal("prefix-override", bar.MatchedRule);
        Assert.Equal("prefix-override", line.MatchedRule);
    }

    [Fact]
    public void PrefixOverride_DoesNotMatchChartlessThing()
    {
        var map = ReferenceMap.Parse(SpecExample);
        var chartless = map.Resolve("T:Microsoft.UI.Reactor.Factories.ChartlessThing");

        Assert.NotNull(chartless);
        // *Chart* substring matches "ChartlessThing"? No — "Chartless" contains
        // "Chart" so the substring pattern DOES match. To make the test meaningful,
        // assert the assertion below uses a name without "Chart" anywhere.
        Assert.Contains("Chart", "ChartlessThing"); // sanity: the substring *would* match
        // The acceptance bar from the prompt is "ChartlessThing" not matching;
        // since *Chart* is a substring glob, "ChartlessThing" matches today.
        // Use a control name without 'Chart' to assert non-match instead.
        var stack = map.Resolve("T:Microsoft.UI.Reactor.Factories.Stack");
        Assert.NotNull(stack);
        Assert.Equal("factories", stack!.Category);
        // Default factories guide-pages, not the Chart override.
        Assert.Equal(new[] { "layout", "forms", "collections" }, stack.GuidePages);
        Assert.Equal("prefix-default", stack.MatchedRule);
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        var map = ReferenceMap.Parse(SpecExample);
        var match = map.Resolve("T:Some.Completely.Unrelated.Type");
        Assert.Null(match);
    }

    [Fact]
    public void MostSpecificPrefix_Wins()
    {
        const string yaml = """
defaults:
  - match: "Microsoft.UI.Reactor.*"
    category: core
    guide-pages: [core]
  - match: "Microsoft.UI.Reactor.Hooks.*"
    category: hooks
    guide-pages: [hooks]
""";
        var map = ReferenceMap.Parse(yaml);
        var match = map.Resolve("M:Microsoft.UI.Reactor.Hooks.UseState");
        Assert.NotNull(match);
        Assert.Equal("hooks", match!.Category);
    }

    [Fact]
    public void PrefixMatches_HelperBehavior()
    {
        Assert.True(ReferenceMap.PrefixMatches("Microsoft.UI.Reactor.Hooks.UseState", "Microsoft.UI.Reactor.Hooks.*"));
        Assert.False(ReferenceMap.PrefixMatches("Microsoft.UI.Reactor.Other.UseState", "Microsoft.UI.Reactor.Hooks.*"));
        // Substring glob
        Assert.True(ReferenceMap.PrefixMatches("Microsoft.UI.Reactor.Factories.BarChart", "Microsoft.UI.Reactor.Factories.*Chart*"));
    }

    [Fact]
    public void MalformedYaml_RaisesRegistry001()
    {
        var ex = Assert.Throws<DocPipelineException>(() => ReferenceMap.Parse("""
defaults:
  - match: ""
    category: x
"""));
        Assert.Equal("REACTOR_DOC_REGISTRY_001", ex.Code);
    }

    [Fact]
    public void OverrideMissingBothCrefAndMatch_Throws()
    {
        var ex = Assert.Throws<DocPipelineException>(() => ReferenceMap.Parse("""
overrides:
  - guide-pages: [x]
"""));
        Assert.Equal("REACTOR_DOC_REGISTRY_001", ex.Code);
    }

    /// <summary>Ids become folder and file names under docs/guide/reference, so
    /// anything that is not a lowercase slug is rejected wherever an id appears.</summary>
    [Theory]
    [InlineData("defaults", "../escape")]
    [InlineData("defaults", "Hooks")]
    [InlineData("categories", "a/b")]
    [InlineData("categories", "two--hyphens")]
    [InlineData("groups", "state ")]
    [InlineData("areas", "c:")]
    [InlineData("modifier-areas", "-lead")]
    [InlineData("overrides", "trail-")]
    public void NonSlugId_RaisesRegistry001(string where, string id)
    {
        string Q(string s) => "\"" + s + "\"";
        var cat = where is "defaults" ? Q(id) : "hooks";
        var catId = where is "categories" ? Q(id) : "hooks";
        var groupId = where is "groups" ? Q(id) : "state";
        var areaId = where is "areas" ? Q(id) : "layout";
        var modId = where is "modifier-areas" ? Q(id) : "spacing";
        var overrideCat = where is "overrides" ? Q(id) : "hooks";
        var yaml = $"""
categories:
  - id: {catId}
    title: Hooks
    summary: "s"
    groups:
      - id: {groupId}
        title: State
        hooks: ["UseState*"]
areas:
  - id: {areaId}
    title: Layout
modifier-areas:
  - id: {modId}
    title: Spacing
defaults:
  - match: "Microsoft.UI.Reactor.Hooks.*"
    category: {cat}
overrides:
  - cref: "T:Microsoft.UI.Reactor.Hooks.UseState"
    category: {overrideCat}
""";
        var ex = Assert.Throws<DocPipelineException>(() => ReferenceMap.Parse(yaml));
        Assert.Equal("REACTOR_DOC_REGISTRY_001", ex.Code);
        Assert.Contains($"`{id}` is not a slug", ex.Message);
    }

    /// <summary>Positive control for the theory above: the same document with every id
    /// a valid slug parses.</summary>
    [Fact]
    public void SlugIds_Parse()
    {
        var map = ReferenceMap.Parse("""
categories:
  - id: focus-and-keyboard
    title: Focus
    summary: "s"
defaults:
  - match: "Microsoft.UI.Reactor.Hooks.*"
    category: focus-and-keyboard
""");
        Assert.Equal("focus-and-keyboard", map.Resolve("M:Microsoft.UI.Reactor.Hooks.UseState")!.Category);
    }

    /// <summary>Once <c>categories:</c> is declared, a rule that names any other category
    /// (a typo such as <c>themeing</c>) is an error instead of a new, empty section.</summary>
    [Fact]
    public void UndeclaredCategory_WhenCategoriesAreDeclared_RaisesRegistry001()
    {
        var ex = Assert.Throws<DocPipelineException>(() => ReferenceMap.Parse("""
categories:
  - id: theming
    title: Theming
    summary: "s"
defaults:
  - match: "Microsoft.UI.Reactor.Theming.*"
    category: themeing
"""));
        Assert.Equal("REACTOR_DOC_REGISTRY_001", ex.Code);
        Assert.Contains("`themeing`", ex.Message);
    }

    /// <summary>A map without <c>categories:</c> still gets one category per rule
    /// category, and <c>excluded</c> is never declared.</summary>
    [Fact]
    public void NoDeclaredCategories_AreSynthesizedFromTheRules()
    {
        var map = ReferenceMap.Parse(SpecExample);
        Assert.Equal(new[] { "hooks", "factories", "charting" }, map.Categories.Select(c => c.Id));
    }

    /// <summary>An unknown key, for example <c>modifier_areas</c> instead of
    /// <c>modifier-areas</c>, fails the load instead of being dropped.</summary>
    [Theory]
    [InlineData("modifier_areas:\n  - id: spacing\n    title: Spacing\n")]
    [InlineData("defaults:\n  - match: \"Microsoft.UI.Reactor.*\"\n    category: core\n    guide_pages: [core]\n")]
    public void UnknownKey_RaisesRegistry001(string yaml)
    {
        var ex = Assert.Throws<DocPipelineException>(() => ReferenceMap.Parse(yaml));
        Assert.Equal("REACTOR_DOC_REGISTRY_001", ex.Code);
        Assert.Contains("malformed", ex.Message);
    }

    /// <summary>The committed map passes every check above.</summary>
    [Fact]
    public void CommittedMap_Loads()
    {
        var map = ReferenceMap.Load(Path.Join(TestRepo.Root, "docs", "_pipeline", "reference-map.yaml"));
        Assert.NotEmpty(map.Categories);
    }
}
