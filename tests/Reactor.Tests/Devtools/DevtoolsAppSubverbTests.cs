using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Spec 028 — the <c>--devtools app</c> subverb. Runs the app normally with the
/// in-app devtools UI flag flipped on. Contrast with <c>--devtools run</c>,
/// which is preview mode with MCP + VS Code integration.
/// </summary>
public class DevtoolsAppSubverbTests
{
    [Fact]
    public void DevtoolsAppArg_ParsesAsAppSubverb()
    {
        var options = DevtoolsCliParser.Parse(["myapp.exe", "--devtools", "app"]);
        Assert.Equal(DevtoolsSubverb.App, options.Subverb);
    }

    [Fact]
    public void DevtoolsApp_DoesNotTakeComponentArg()
    {
        // Unlike `run`, the `app` subverb runs the whole app — there's no
        // single component to preview, so a trailing token should not be
        // interpreted as a component name.
        var options = DevtoolsCliParser.Parse(["myapp.exe", "--devtools", "app", "SomeComponent"]);
        Assert.Equal(DevtoolsSubverb.App, options.Subverb);
        Assert.Null(options.ComponentName);
    }

    [Fact]
    public void BareDevtools_StillDefaultsToRun_NotApp()
    {
        // Preserving existing behavior: bare `--devtools` with no verb still
        // means preview mode. The `app` subverb is explicit-opt-in only.
        var options = DevtoolsCliParser.Parse(["myapp.exe", "--devtools"]);
        Assert.Equal(DevtoolsSubverb.Run, options.Subverb);
    }

    [Fact]
    public void DevtoolsAppArg_CoexistsWithOtherFlags()
    {
        var options = DevtoolsCliParser.Parse(
            ["myapp.exe", "--devtools", "app", "--mcp-port", "7100"]);
        Assert.Equal(DevtoolsSubverb.App, options.Subverb);
        Assert.Equal(7100, options.McpPort);
    }
}
