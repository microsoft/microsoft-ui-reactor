using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// `--preview &lt;Name&gt;` is the form the removed `mur devtools` stub recommends. It runs the same in-app
/// preview host as the VS Code live preview and the Visual Studio embedded preview, so the host
/// runs it silently instead of calling it deprecated.
/// </summary>
[Collection("ConsoleTests")] // TryHandleCommandLine writes to Console.Out / Console.Error.
public sealed class DevtoolsPreviewFormTests
{
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
