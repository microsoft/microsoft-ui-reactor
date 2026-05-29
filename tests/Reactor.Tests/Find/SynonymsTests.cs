#nullable enable

using Microsoft.UI.Reactor.Cli.Find;
using Xunit;

namespace Reactor.Tests.Find;

public class SynonymsTests
{
    [Fact]
    public void CollapsePhrase_MultiWord_CollapsesToToken()
    {
        Assert.Equal("usestate", Synonyms.CollapsePhrase("use state"));
    }

    [Fact]
    public void Expand_KnownSynonym_ReturnsTargets()
    {
        Assert.Equal(["contentdialog", "dialog"], Synonyms.Expand("modal"));
    }

    [Fact]
    public void Expand_Unknown_ReturnsSelf()
    {
        Assert.Equal(["foobar"], Synonyms.Expand("foobar"));
    }

    [Fact]
    public void ProcessQuery_FullPipeline()
    {
        var terms = Synonyms.ProcessQuery("use state counter");

        Assert.Contains("usestate", terms);
        Assert.DoesNotContain("counter", terms);
        Assert.DoesNotContain("use", terms);
        Assert.DoesNotContain("state", terms);
    }

    [Fact]
    public void ProcessQuery_RemovesStopWords()
    {
        var terms = Synonyms.ProcessQuery("the button for the form");

        Assert.DoesNotContain("the", terms);
        Assert.DoesNotContain("for", terms);
        Assert.Contains("button", terms);
        Assert.Contains("formfield", terms);
        Assert.Contains("usevalidationcontext", terms);
    }
}
