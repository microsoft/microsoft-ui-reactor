using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

public class TierFrontMatterTests // public so xUnit can discover; method params can still see internal DocTier via InternalsVisibleTo
{
    [Theory]
    [InlineData("stub", DocTier.Stub)]
    [InlineData("solid", DocTier.Solid)]
    [InlineData("comprehensive", DocTier.Comprehensive)]
    [InlineData("Stub", DocTier.Stub)]            // case-insensitive
    [InlineData("COMPREHENSIVE", DocTier.Comprehensive)]
    public void Parses_each_tier_value(string raw, DocTier expected)
    {
        var content = $"""
            ---
            title: "Sample"
            tier: {raw}
            ---

            Body paragraph.
            """;

        var template = TemplateParser.ParseContent(content);

        Assert.True(template.TierDeclared);
        Assert.Equal(expected, template.Tier);
    }

    [Fact]
    public void Missing_tier_defaults_to_solid_and_is_not_declared()
    {
        var content = """
            ---
            title: "Sample"
            ---

            Body paragraph.
            """;

        var template = TemplateParser.ParseContent(content);

        Assert.False(template.TierDeclared);
        Assert.Equal(DocTier.Solid, template.Tier);
    }

    [Fact]
    public void Unknown_tier_throws()
    {
        var content = """
            ---
            title: "Sample"
            tier: "platinum"
            ---

            Body.
            """;

        var ex = Assert.Throws<DocPipelineException>(() => TemplateParser.ParseContent(content));
        Assert.Contains("platinum", ex.Message);
    }
}
