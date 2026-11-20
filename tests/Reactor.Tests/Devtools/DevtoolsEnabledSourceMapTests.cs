using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Spec 010: <see cref="ReactorApp.DevtoolsEnabled"/> mirrors into
/// <c>ReactorSourceMap.Enabled</c>. That mirror is how the in-app preview host
/// (<c>--devtools run</c> / <c>--preview</c>) turns call-site stamping on, and every other
/// source-map test sets <c>ReactorSourceMap.Enabled</c> directly, so without these the mirror
/// could be deleted while the whole source-map suite stayed green.
///
/// <para>In the SourceMapGlobals collection: the setter and
/// <c>ResetDevtoolsEnabledForTests()</c> both write <c>ReactorSourceMap.Enabled</c>, which
/// <c>SourceMapElementSlotTests</c> reads in parallel otherwise.</para>
/// </summary>
[Collection("SourceMapGlobals")]
public sealed class DevtoolsEnabledSourceMapTests : IDisposable
{
    public DevtoolsEnabledSourceMapTests() => ReactorApp.ResetDevtoolsEnabledForTests();
    public void Dispose() => ReactorApp.ResetDevtoolsEnabledForTests();

    [Fact]
    public void EnablingDevtools_TurnsSourceMappingOn()
    {
        Assert.False(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);

        ReactorApp.DevtoolsEnabled = true;

        Assert.True(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
    }

    [Fact]
    public void DisablingDevtools_TurnsSourceMappingOff()
    {
        ReactorApp.DevtoolsEnabled = true;
        Assert.True(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);

        ReactorApp.DevtoolsEnabled = false;

        Assert.False(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
    }

    [Fact]
    public void ResettingDevtools_TurnsSourceMappingOff()
    {
        // The reset helper writes the backing field directly, bypassing the setter that
        // mirrors, so it needs its own check: a reset that left source mapping on would leak
        // the flag into every later test.
        ReactorApp.DevtoolsEnabled = true;
        Assert.True(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);

        ReactorApp.ResetDevtoolsEnabledForTests();

        Assert.False(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
        Assert.False(ReactorApp.DevtoolsEnabled);
    }

    // `--devtools run` is the path that turns devtools (and so source mapping) on for the preview
    // host; `list` is the contrast. An unknown component makes `run` return before any window.
    [Theory]
    [InlineData("run", true)]
    [InlineData("list", false)]
    public void PreviewHost_RunSubverb_TurnsDevtoolsAndSourceMappingOn(string subverb, bool expected)
    {
        string[] args = subverb == "run"
            ? ["app.exe", "--devtools", "run", "NoSuchComponent_DevtoolsEnabledSourceMapTests"]
            : ["app.exe", "--devtools", "list", global::System.IO.Path.Join(global::System.IO.Path.GetTempPath(), $"reactor-list-{Guid.NewGuid():N}.txt")];
        var originalOut = Console.Out;
        var originalError = Console.Error;
        bool handled;
        try
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            handled = new global::Microsoft.UI.Reactor.Hosting.Devtools.DevtoolsHost().TryHandleCommandLine(
                new global::Microsoft.UI.Reactor.Hosting.Devtools.ReactorDevtoolsBootRequest(
                    global::Microsoft.UI.Reactor.Hosting.Devtools.DevtoolsCliParser.Parse(args),
                    Title: "App", Width: 800, Height: 600, FullScreen: false,
                    HostRoot: null, HostRootFactory: null, RootRenderFunc: null, Configure: null));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            if (subverb == "list" && File.Exists(args[3])) File.Delete(args[3]);
        }

        Assert.True(handled);
        Assert.Equal(expected, ReactorApp.DevtoolsEnabled);
        Assert.Equal(expected, global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
    }
}
