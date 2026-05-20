#nullable enable

using Microsoft.UI.Reactor.Cli.Find;
using Xunit;

namespace Reactor.Tests.Find;

public class SearchEngineTests
{
    private static ScenarioCatalogue CreateTestCatalogue() => new(
        [
            new Scenario("use-state-basic", "hooks", "Counter with UseState", "increment a primitive value on click",
                ["state", "counter", "hook"], ["UseState", "Button", "VStack"], "UseState", [], "P0", "// code", "// raw"),
            new Scenario("button-label", "buttons", "Button with label and onClick", "basic button with click handler",
                ["button", "click", "handler"], ["Button"], null, [], "P0", "// code", "// raw"),
            new Scenario("sidebar-nav", "navigation", "Sidebar with NavigationView", "sidebar pane with two items",
                ["sidebar", "navigation", "nav"], ["NavigationView"], null, [], "P0", "// code", "// raw"),
            new Scenario("anti-pattern-1", "hooks", "Bad useState with list", "mutating list in place",
                ["state", "list", "anti-pattern"], ["UseState"], null, ["use-state-basic"], "anti-pattern", "// code", "// raw"),
        ],
        "2026-01-01T00:00:00Z"
    );

    [Fact]
    public void Search_ExactFactoryName_ReturnsMatch()
    {
        var engine = new SearchEngine(CreateTestCatalogue());

        var results = engine.Search("UseState", 5, null, includeAntiPatterns: false);

        Assert.NotEmpty(results);
        Assert.Equal("use-state-basic", results[0].Scenario.Id);
    }

    [Fact]
    public void Search_SynonymExpansion_FindsMatch()
    {
        var engine = new SearchEngine(CreateTestCatalogue());

        var results = engine.Search("nav", 5, null, includeAntiPatterns: false);

        Assert.Contains(results, result => result.Scenario.Id == "sidebar-nav");
    }

    [Fact]
    public void Search_CategoryFilter_RestrictsResults()
    {
        var engine = new SearchEngine(CreateTestCatalogue());

        var results = engine.Search("button state sidebar", 10, "hooks", includeAntiPatterns: true);

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.Equal("hooks", result.Scenario.Category));
        Assert.DoesNotContain(results, result => result.Scenario.Id == "button-label");
        Assert.DoesNotContain(results, result => result.Scenario.Id == "sidebar-nav");
    }

    [Fact]
    public void Search_AntiPatternsExcludedByDefault()
    {
        var engine = new SearchEngine(CreateTestCatalogue());

        var results = engine.Search("mutating list usestate", 10, null, includeAntiPatterns: false);

        Assert.DoesNotContain(results, result => result.Scenario.Id == "anti-pattern-1");
    }

    [Fact]
    public void Search_AntiPatternsIncludedWhenRequested()
    {
        var engine = new SearchEngine(CreateTestCatalogue());

        var results = engine.Search("mutating list usestate", 10, null, includeAntiPatterns: true);

        Assert.Contains(results, result => result.Scenario.Id == "anti-pattern-1");
    }

    [Fact]
    public void Search_MaxResults_Respected()
    {
        var engine = new SearchEngine(CreateTestCatalogue());

        var results = engine.Search("button state sidebar", 1, null, includeAntiPatterns: true);

        Assert.Single(results);
    }
}
