using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

public class CaveatBlockTests
{
    [Fact]
    public void Renders_caveat_block_as_styled_blockquote()
    {
        var content = """
            ---
            title: "Sample"
            ---

            Body lead.

            <!-- ai:caveat -->
            UseEffect runs after commit, not synchronously with render.
            <!-- /ai:caveat -->

            Trailing paragraph.
            """;

        var template = TemplateParser.ParseContent(content);

        Assert.Single(template.Caveats);
        Assert.Contains("UseEffect runs after commit", template.Caveats[0].Content);

        Assert.Contains("> **Caveat:** UseEffect runs after commit", template.Body);
        Assert.Contains("Trailing paragraph.", template.Body);
        // Tag markers must be stripped from the rendered output.
        Assert.DoesNotContain("<!-- ai:caveat -->", template.Body);
        Assert.DoesNotContain("<!-- /ai:caveat -->", template.Body);
    }

    [Fact]
    public void Multiple_caveats_are_all_extracted()
    {
        var content = """
            ---
            title: "Sample"
            ---

            <!-- ai:caveat -->First<!-- /ai:caveat -->

            Middle.

            <!-- ai:caveat -->Second<!-- /ai:caveat -->
            """;

        var template = TemplateParser.ParseContent(content);

        Assert.Equal(2, template.Caveats.Count);
        Assert.Equal("First", template.Caveats[0].Content);
        Assert.Equal("Second", template.Caveats[1].Content);
    }

    [Fact]
    public void Unclosed_caveat_throws()
    {
        var content = """
            ---
            title: "Sample"
            ---

            <!-- ai:caveat -->
            This block forgot its closer.

            Done.
            """;

        var ex = Assert.Throws<DocPipelineException>(() => TemplateParser.ParseContent(content));
        Assert.Equal("REACTOR_DOC_CAVEAT_001", ex.Code);
    }
}
