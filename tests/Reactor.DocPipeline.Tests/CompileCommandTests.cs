using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

public class CompileCommandTests
{
    [Fact]
    public void ParseScreenshotFilter_allows_bare_ids_with_topic()
    {
        var refs = CompileCommand.ParseScreenshotFilter(
            ["--topic", "docking", "--screenshots", "two-pane,side-pin"],
            topic: "docking");

        Assert.NotNull(refs);
        Assert.Contains("docking/two-pane", refs);
        Assert.Contains("docking/side-pin", refs);
    }

    [Fact]
    public void ParseScreenshotFilter_allows_full_ids_without_topic()
    {
        var refs = CompileCommand.ParseScreenshotFilter(
            ["--screenshots", "docking/two-pane", "--screenshot", "controls/forms"],
            topic: null);

        Assert.NotNull(refs);
        Assert.Contains("docking/two-pane", refs);
        Assert.Contains("controls/forms", refs);
    }

    [Fact]
    public void ParseScreenshotFilter_rejects_bare_ids_without_topic()
    {
        Assert.Throws<ArgumentException>(() =>
            CompileCommand.ParseScreenshotFilter(["--screenshots", "two-pane"], topic: null));
    }

    [Fact]
    public void ParseScreenshotFilter_rejects_topic_mismatch()
    {
        Assert.Throws<ArgumentException>(() =>
            CompileCommand.ParseScreenshotFilter(["--screenshots", "controls/forms"], topic: "docking"));
    }
}
