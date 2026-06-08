using System;
using System.IO;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

public class CliFlagParsingTests
{
    [Fact]
    public void NoFlags_ReturnsNullSubverb()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe"]);
        Assert.Null(opts.Subverb);
        Assert.False(opts.UsedDeprecatedPreview);
        Assert.False(opts.PreviewAndDevtoolsConflict);
    }

    [Fact]
    public void DevtoolsRun_ParsesAsRun()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.Equal(DevtoolsSubverb.Run, opts.Subverb);
        Assert.False(opts.UsedDeprecatedPreview);
    }

    [Fact]
    public void DevtoolsList_ParsesAsList()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "list"]);
        Assert.Equal(DevtoolsSubverb.List, opts.Subverb);
    }

    [Fact]
    public void DevtoolsScreenshot_ParsesAsScreenshot()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "screenshot", "CounterDemo", "--out", "c:/tmp/out.png"]);
        Assert.Equal(DevtoolsSubverb.Screenshot, opts.Subverb);
        Assert.Equal("CounterDemo", opts.ComponentName);
        Assert.Equal("c:/tmp/out.png", opts.ScreenshotOutputPath);
    }

    [Fact]
    public void DevtoolsTree_ParsesAsTree()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "tree"]);
        Assert.Equal(DevtoolsSubverb.Tree, opts.Subverb);
    }

    [Fact]
    public void DevtoolsBareFlag_DefaultsToRun()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools"]);
        Assert.Equal(DevtoolsSubverb.Run, opts.Subverb);
    }

    [Fact]
    public void DevtoolsRun_WithComponentName()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "MyComponent"]);
        Assert.Equal(DevtoolsSubverb.Run, opts.Subverb);
        Assert.Equal("MyComponent", opts.ComponentName);
    }

    [Fact]
    public void DevtoolsList_WithOutputPath()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "list", "c:/tmp/components.txt"]);
        Assert.Equal(DevtoolsSubverb.List, opts.Subverb);
        Assert.Equal("c:/tmp/components.txt", opts.ListOutputPath);
    }

    [Fact]
    public void PreviewAlias_SetsDeprecatedFlag_AndMapsToRun()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--preview", "MyComp"]);
        Assert.Equal(DevtoolsSubverb.Run, opts.Subverb);
        Assert.True(opts.UsedDeprecatedPreview);
        Assert.Equal("MyComp", opts.ComponentName);
    }

    [Fact]
    public void PreviewListAlias_SetsDeprecatedFlag_AndMapsToList()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--preview-list"]);
        Assert.Equal(DevtoolsSubverb.List, opts.Subverb);
        Assert.True(opts.UsedDeprecatedPreview);
    }

    [Fact]
    public void DevtoolsListLegacyAlias_MapsToList()
    {
        // --devtools-list is the one-step alias used by tools that pre-date the subverb form.
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools-list"]);
        Assert.Equal(DevtoolsSubverb.List, opts.Subverb);
        Assert.False(opts.UsedDeprecatedPreview);
    }

    [Fact]
    public void BothPreviewAndDevtools_IsConflict()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--preview", "--devtools", "run"]);
        Assert.True(opts.PreviewAndDevtoolsConflict);
    }

    [Fact]
    public void BothPreviewAndDevtools_WithEmbed_DoesNotHonorEmbed()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--preview", "--devtools", "run", "--embed", "--embed-host-pid", "1234"]);

        Assert.True(opts.PreviewAndDevtoolsConflict);
        Assert.False(opts.EmbedRequested);
        Assert.Null(opts.EmbedValidationError);
    }

    [Fact]
    public void BothPreviewListAndDevtoolsList_IsConflict()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--preview-list", "--devtools", "list"]);
        Assert.True(opts.PreviewAndDevtoolsConflict);
    }

    [Fact]
    public void VsCodeFlag_IsPickedUp()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--vscode"]);
        Assert.True(opts.VsCodeMode);
    }

    [Fact]
    public void FpsFlag_IsClampedToRange()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--fps", "9999"]);
        Assert.Equal(30, opts.Fps);

        var opts2 = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--fps", "0"]);
        Assert.Equal(1, opts2.Fps);

        var opts3 = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--fps", "15"]);
        Assert.Equal(15, opts3.Fps);
    }

    [Fact]
    public void McpPortFlag_IsParsed()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--mcp-port", "54321"]);
        Assert.Equal(54321, opts.McpPort);
    }

    [Fact]
    public void McpPortFlag_Default_IsNull()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.Null(opts.McpPort);
    }

    [Fact]
    public void Fps_Default_Is10()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.Equal(10, opts.Fps);
    }

    [Fact]
    public void ComponentName_LeadingDashIsNotTreatedAsName()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--vscode"]);
        Assert.Null(opts.ComponentName);
    }

    [Fact]
    public void UnknownDevtoolsVerb_FallsBackToRun()
    {
        // Defensive: an unknown trailing token is treated as a component name, not a verb.
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "MyComponent"]);
        Assert.Equal(DevtoolsSubverb.Run, opts.Subverb);
        Assert.Equal("MyComponent", opts.ComponentName);
    }

    [Fact]
    public void LogLevelFlag_IsParsed()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--devtools-log-level", "trace"]);
        Assert.Equal("trace", opts.LogLevel);
    }

    [Fact]
    public void LogLevelFlag_Default_IsNull()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.Null(opts.LogLevel);
    }

    [Fact]
    public void McpTransportFlag_Stdio_IsPicked()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--mcp-transport", "stdio"]);
        Assert.Equal(McpTransport.Stdio, opts.Transport);
    }

    [Fact]
    public void McpTransportFlag_DefaultsToHttp()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.Equal(McpTransport.Http, opts.Transport);
    }

    [Fact]
    public void McpTransportFlag_UnknownValue_KeepsHttpDefault()
    {
        // An unknown transport token should not flip to stdio silently —
        // the HTTP default stays, so users don't end up with a silently
        // broken stdout stream.
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--mcp-transport", "carrier-pigeon"]);
        Assert.Equal(McpTransport.Http, opts.Transport);
    }

    [Fact]
    public void LogsFlag_OffDisablesCapture()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--devtools-logs", "off"]);
        Assert.True(opts.LogsDisabled);
    }

    [Fact]
    public void LogsFlag_Default_CaptureEnabled()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.False(opts.LogsDisabled);
    }

    [Fact]
    public void LogsCapacityFlag_IsParsed()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--devtools-logs-capacity", "16"]);
        Assert.Equal(16, opts.LogsCapacityMb);
    }

    [Fact]
    public void LogsCapacityFlag_Default_IsNull()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.Null(opts.LogsCapacityMb);
    }

    [Fact]
    public void Embed_Flag_RequiresRunSubverb()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "list", "--embed", "--embed-host-pid", "1234"]);
        Assert.Equal("--embed requires '--devtools run'", opts.EmbedValidationError);
        Assert.Null(opts.Subverb);
    }

    [Fact]
    public void Embed_Flag_RequiresHostPid()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed"]);
        Assert.Equal("--embed requires --embed-host-pid <pid>", opts.EmbedValidationError);
        Assert.Null(opts.EmbedHostPid);
    }

    [Fact]
    public void Embed_Flag_ImpliesVscode()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed", "--embed-host-pid", "1234"]);
        Assert.True(opts.VsCodeMode);
        Assert.True(opts.EmbedAutoEnabledVsCode);
    }

    [Fact]
    public void Embed_Flag_WithExplicitVscode_NoAutoFlag()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--vscode", "--embed", "--embed-host-pid", "1234"]);
        Assert.True(opts.VsCodeMode);
        Assert.False(opts.EmbedAutoEnabledVsCode);
    }

    [Fact]
    public void EmbedMode_Child_Default()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed", "--embed-host-pid", "1234"]);
        Assert.Equal(WindowEmbedStyle.Child, opts.EmbedStyle);
    }

    [Fact]
    public void EmbedMode_Owner_Parses()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed", "--embed-mode", "owner", "--embed-host-pid", "1234"]);
        Assert.Equal(WindowEmbedStyle.Owner, opts.EmbedStyle);
    }

    [Fact]
    public void EmbedMode_InvalidValue_DefaultsToChild()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed", "--embed-mode", "bogus", "--embed-host-pid", "1234"]);
        Assert.Equal(WindowEmbedStyle.Child, opts.EmbedStyle);
    }

    [Fact]
    public void EmbedHostPid_Parses_Decimal()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed", "--embed-host-pid", "9999"]);
        Assert.Equal(9999, opts.EmbedHostPid);
    }

    [Fact]
    public void EmbedHostPid_NonInteger_ReportsParseError()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed", "--embed-host-pid", "xyz"]);

        Assert.Null(opts.EmbedHostPid);
        Assert.Equal("--embed-host-pid value 'xyz' is not a valid process id", opts.EmbedValidationError);
    }

    [Fact]
    public void NoEmbed_NoErrors()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.False(opts.EmbedRequested);
        Assert.Null(opts.EmbedValidationError);
    }
}

[Collection("ConsoleTests")]
public class DevtoolsHostCliTests
{
    [Fact]
    public void TryRunDevtoolsForTest_InvalidEmbed_PrintsValidationErrorToStderr()
    {
        using var stderr = new StringWriter();
        var originalError = Console.Error;
        var originalExitCode = Environment.ExitCode;

        try
        {
            Environment.ExitCode = 0;
            Console.SetError(stderr);

            var handled = ReactorApp.TryRunDevtoolsForTest(
                ["app.exe", "--devtools", "list", "--embed", "--embed-host-pid", "1234"],
                title: "Preview",
                width: 800,
                height: 600);

            Assert.True(handled);
            Assert.Equal(2, Environment.ExitCode);
        }
        finally
        {
            Console.SetError(originalError);
            Environment.ExitCode = originalExitCode;
        }

        Assert.Contains("[reactor] --embed requires '--devtools run'", stderr.ToString());
    }

    [Fact]
    public void TryHandleCommandLine_InvalidEmbed_PrintsValidationErrorToStderr()
    {
        var options = DevtoolsCliParser.Parse(["app.exe", "--devtools", "list", "--embed", "--embed-host-pid", "1234"]);
        using var stderr = new StringWriter();
        var originalError = Console.Error;

        try
        {
            Console.SetError(stderr);
            var handled = new DevtoolsHost().TryHandleCommandLine(new ReactorDevtoolsBootRequest(
                options,
                Title: "Preview",
                Width: 800,
                Height: 600,
                FullScreen: false,
                HostRoot: null,
                HostRootFactory: null,
                RootRenderFunc: null,
                Configure: null));

            Assert.True(handled);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Contains("[reactor] --embed requires '--devtools run'", stderr.ToString());
    }
}
