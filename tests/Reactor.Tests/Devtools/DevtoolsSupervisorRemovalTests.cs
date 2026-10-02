using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Behaviour of the in-app devtools host that leaned on the removed `mur devtools`
/// supervisor: <c>reload</c> used to exit 42 and <c>--mcp-port</c> conflicts exited 43,
/// both for the supervisor to relaunch. Nothing relaunches the app now, so neither exits.
/// </summary>
[Collection("ConsoleTests")] // TryHandleCommandLine writes to Console.Out / Console.Error.
public sealed class DevtoolsSupervisorRemovalTests
{
    [Fact]
    public void ReloadTool_ReturnsAToolErrorNamingWinappWatch_AndRequestsNothing()
    {
        var shutdowns = 0;
        using var server = new DevtoolsMcpServer(null!, null!);
        DevtoolsTools.RegisterCore(server, new DevtoolsTools.ToolHostContext
        {
            GetComponents = () => ["App"],
            GetCurrentComponent = () => "App",
            SwitchComponent = _ => false,
            RequestShutdown = () => shutdowns++,
            Windows = new WindowRegistry(server.BuildTag),
        });

        Assert.True(server.Tools.TryGet("reload", out var reload));
        var ex = Assert.Throws<McpToolException>(() => reload(null));

        Assert.Equal(JsonRpcErrorCodes.ToolExecution, ex.Code);
        Assert.Equal(DevtoolsTools.ReloadUnavailableMessage, ex.Message);
        Assert.Contains("`winapp run . --devtools --watch`", ex.Message);
        Assert.Contains("the app keeps running", ex.Message);
        Assert.Equal(0, shutdowns);

        // Positive control: the same context does wire host callbacks — shutdown still
        // reaches the host, so reload's silence is the handler's doing, not a dead ctx.
        Assert.True(server.Tools.TryGet("shutdown", out var shutdown));
        shutdown(null);
        Assert.Equal(1, shutdowns);
    }

    [Fact]
    public void ToolDescriptions_NoLongerPromiseTheSupervisor()
    {
        using var server = new DevtoolsMcpServer(null!, null!);
        DevtoolsTools.RegisterCore(server, new DevtoolsTools.ToolHostContext
        {
            GetComponents = () => ["App"],
            GetCurrentComponent = () => "App",
            SwitchComponent = _ => false,
            RequestShutdown = () => { },
            Windows = new WindowRegistry(server.BuildTag),
        });

        var descriptors = server.Tools.List().ToDictionary(d => d.Name);
        Assert.Contains("reload", descriptors.Keys);
        Assert.Contains("shutdown", descriptors.Keys);
        Assert.StartsWith("Unavailable:", descriptors["reload"].Description);
        Assert.Contains("winapp run . --devtools --watch", descriptors["reload"].Description);
        Assert.All(descriptors.Values, d =>
        {
            Assert.DoesNotContain("supervisor", d.Description, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mur devtools", d.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("code 42", d.Description, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("--preview-list")]
    [InlineData("--devtools-list")]
    public void PreviewForm_RunsSilently_WithoutTheOldDeprecationWarning(string flag)
    {
        // `--preview <Name>` is the form `mur devtools` now recommends, so the host must not
        // call it deprecated. The list subverb is the headless way through TryHandleCommandLine.
        // The list subverb writes its (possibly empty) result to this file, which proves it ran.
        var listOut = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "reactor-preview-list-" + Guid.NewGuid().ToString("N") + ".txt");
        var options = DevtoolsCliParser.Parse(["app.exe", flag, listOut]);
        Assert.Equal(flag == "--preview-list", options.UsedDeprecatedPreview); // positive control: the flag the warning keyed on

        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        bool handled;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            handled = new DevtoolsHost().TryHandleCommandLine(new ReactorDevtoolsBootRequest(
                options,
                Title: "Preview",
                Width: 800,
                Height: 600,
                FullScreen: false,
                HostRoot: null,
                HostRootFactory: null,
                RootRenderFunc: null,
                Configure: null));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        try
        {
            Assert.True(handled);
            Assert.Equal(string.Empty, stderr.ToString());
            Assert.True(File.Exists(listOut), "the list subverb did not run");
        }
        finally
        {
            File.Delete(listOut);
        }
    }

    [Fact]
    public void PreviewAndDevtoolsTogether_IsStillAConflict()
    {
        var options = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--preview", "Counter"]);

        Assert.True(options.PreviewAndDevtoolsConflict);
        Assert.Null(options.Subverb);
    }
}
