using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.SourceMap.Tests;

/// <summary>
/// Root mount call sites — the generator intercepts <c>ReactorApp.Run</c>,
/// <c>ReactorApp.OpenWindow</c>, <c>ReactorWindow.Mount</c>, <c>ReactorHost.Mount</c> and
/// <c>ReactorHostControl.Mount</c> and brackets each with a root mount scope the
/// intercepted method claims for the host it mounts, so <c>ReactorDiagnostics.GetHosts()</c> can report where a root was mounted.
///
/// <para>A headless test cannot reach a host (each needs a WinUI window), so every call
/// here is made to fail on its first argument check — AFTER the interceptor has opened
/// its scope and BEFORE anything WinUI is touched. The scope is observed through the
/// internal <see cref="ReactorSourceMap.RootMountSiteEnteredForTest"/> seam, and the
/// tests then prove the scope was closed again despite the throw.</para>
/// </summary>
[Collection("SourceMap")]
public sealed class RootMountInterceptionTests : IDisposable
{
    private readonly List<SourceLocation> _entered = new();
    private readonly List<SourceLocation> _claimed = new();

    public RootMountInterceptionTests()
    {
        ReactorSourceMap.Enabled = true;
        ReactorSourceMap.RootMountSiteEnteredForTest = _entered.Add;
        ReactorSourceMap.RootMountSiteClaimedForTest = _claimed.Add;
    }

    public void Dispose()
    {
        ReactorSourceMap.RootMountSiteEnteredForTest = null;
        ReactorSourceMap.RootMountSiteClaimedForTest = null;
        ReactorSourceMap.Enabled = false;
    }

    private static int Line([CallerLineNumber] int line = 0) => line;

    private sealed class Probe : Component
    {
        public override Element Render() => TextBlock("probe");
    }

    /// <summary>
    /// Asserts one scope was opened at <paramref name="expectedLine"/>, at the column of
    /// <paramref name="methodName"/> followed by <c>(</c> on that line. The column oracle
    /// is this file's own text (there is no <c>[CallerColumnNumber]</c>).
    /// </summary>
    private void AssertSingleSiteAt(int expectedLine, string methodName)
    {
        var site = Assert.Single(_entered);
        Assert.Equal(expectedLine, site.LineNumber);
        Assert.EndsWith("RootMountInterceptionTests.cs", site.FilePath, StringComparison.Ordinal);
        Assert.Equal(0, ReactorSourceMap.OpenRootMountScopeCountForTest);

        var text = SourceLine(expectedLine);
        int index = text.IndexOf(methodName + "(", StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{methodName}(' not found on line {expectedLine}: {text}");
        Assert.Equal(index + 1, site.ColumnNumber);
    }

    private static string SourceLine(int lineNumber)
    {
        string relative = global::System.IO.Path.Join("tests", "Reactor.SourceMap.Tests", "RootMountInterceptionTests.cs");
        var dir = new global::System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = global::System.IO.Path.Join(dir.FullName, relative);
            if (global::System.IO.File.Exists(candidate))
                return global::System.IO.File.ReadAllLines(candidate)[lineNumber - 1];
            dir = dir.Parent;
        }

        throw new InvalidOperationException("could not locate RootMountInterceptionTests.cs above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// The intercepted method claimed its OWN scope as its first statement — before the
    /// argument check that makes it throw — so no later mount could have taken it.
    /// </summary>
    private void AssertClaimedByTheEntryPoint(int expectedLine, string methodName)
    {
        AssertSingleSiteAt(expectedLine, methodName);
        Assert.Equal(_entered, _claimed);
    }

    [Fact]
    public void OpenWindowWithComponentRoot_OpensAScopeAtTheCallSite()
    {
        Assert.Throws<ArgumentNullException>(() => ReactorApp.OpenWindow(new WindowSpec(), (Func<Component>)null!)); int expected = Line();

        AssertClaimedByTheEntryPoint(expected, "OpenWindow");
    }

    [Fact]
    public void OpenWindowWithRenderRoot_OpensAScopeAtTheCallSite()
    {
        Assert.Throws<ArgumentNullException>(() => ReactorApp.OpenWindow(new WindowSpec(), (Func<RenderContext, Element>)null!)); int expected = Line();

        AssertClaimedByTheEntryPoint(expected, "OpenWindow");
    }

    [Fact]
    public void GenericRun_OpensAScopeAtTheCallSite()
    {
        Assert.Throws<ArgumentNullException>(() => ReactorApp.Run<Probe>((WindowSpec)null!)); int expected = Line();

        AssertClaimedByTheEntryPoint(expected, "Run<Probe>");
    }

    [Fact]
    public void RenderFunctionRun_OpensAScopeAtTheCallSite()
    {
        Assert.Throws<ArgumentNullException>(() => ReactorApp.Run((WindowSpec)null!, _ => TextBlock("x"))); int expected = Line();

        AssertClaimedByTheEntryPoint(expected, "Run");
    }

    [Fact]
    public void ReactorHostMount_InstanceCallIsIntercepted()
    {
        ReactorHost host = null!;

        Assert.Throws<NullReferenceException>(() => host.Mount(_ => TextBlock("x"))); int expected = Line();

        AssertSingleSiteAt(expected, "Mount");
    }

    [Fact]
    public void ReactorHostControlMount_InstanceCallIsIntercepted()
    {
        ReactorHostControl control = null!;

        Assert.Throws<NullReferenceException>(() => control.Mount(new Probe())); int expected = Line();

        AssertSingleSiteAt(expected, "Mount");
    }

    [Fact]
    public void ReactorHostControlMount_RenderFunctionOverloadIsIntercepted()
    {
        ReactorHostControl control = null!;

        Assert.Throws<NullReferenceException>(() => control.Mount(_ => TextBlock("x"))); int expected = Line();

        AssertSingleSiteAt(expected, "Mount");
    }

    [Fact]
    public void ReactorWindowMount_InstanceCallIsIntercepted()
    {
        ReactorWindow window = null!;

        Assert.Throws<NullReferenceException>(() => window.Mount(new Probe())); int expected = Line();

        AssertSingleSiteAt(expected, "Mount");
    }

    /// <summary>
    /// A null receiver in a conditional access never reaches the intercepted method, so
    /// no scope opens — the "no site" control for the conditional-access shape. The
    /// non-null case (the interceptor runs and the host keeps the site) needs a live host
    /// and is covered by the <c>HostDiag_ConditionalAccessMountReportsItsSite</c> selftest;
    /// <see cref="ConditionalAccessMount_IsIntercepted"/> proves the generator emitted an
    /// interceptor for this exact call, so the empty list below is a measurement.
    /// </summary>
    [Fact]
    public void ConditionalAccessMount_NullReceiver_OpensNoScope()
    {
        ReactorHost? host = null;
        ReactorHostControl? control = null;
        ReactorWindow? window = null;

        host?.Mount(_ => TextBlock("x"));
        control?.Mount(new Probe());
        window?.Mount(new Probe());

        Assert.Empty(_entered);
        Assert.Equal(0, ReactorSourceMap.OpenRootMountScopeCountForTest);
    }

    /// <summary>
    /// The generator emits an interceptor for <c>receiver?.Mount(...)</c> — read straight
    /// from the generated file, keyed on this file and the call's line.
    /// </summary>
    [Fact]
    public void ConditionalAccessMount_IsIntercepted()
    {
        ReactorHostControl? control = null;
        control?.Mount(new Probe()); int expected = Line();

        var generated = Directory.EnumerateFiles(
                AppContext.BaseDirectory.Split(new[] { $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}" }, StringSplitOptions.None)[0],
                "ReactorSourceMap.RootMounts.g.cs", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        Assert.NotNull(generated);

        var text = File.ReadAllText(generated!);
        var source = SourceLine(expected);
        int column = source.IndexOf("Mount(", StringComparison.Ordinal) + 1;
        Assert.True(column > 0, source);
        Assert.Contains($"RootMountInterceptionTests.cs\", {expected}, {column})", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RootRenderFunctionHooks_ResolveFromTheMountSite()
    {
        ReactorHost host = null!;

        // The lambda never runs (the call throws first); only the generator's static
        // table and the recorded site are exercised.
        Assert.Throws<NullReferenceException>(() => host.Mount(ctx =>
        {
            var (rootCount, _) = ctx.UseState(0); // hook:root-count
            return TextBlock($"{rootCount}");
        }));

        var site = Assert.Single(_entered);
        Assert.Equal($"0:rootCount@{MarkerLine("hook:root-count")}", ReactorSourceMap.GetRenderFunctionHooks(site));
    }

    private static int MarkerLine(string marker)
    {
        var dir = new global::System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = global::System.IO.Path.Combine(dir.FullName, "tests", "Reactor.SourceMap.Tests", "RootMountInterceptionTests.cs");
            if (global::System.IO.File.Exists(candidate))
            {
                var lines = global::System.IO.File.ReadAllLines(candidate);
                return Array.FindIndex(lines, l => l.Contains("// " + marker, StringComparison.Ordinal)) + 1;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("could not locate RootMountInterceptionTests.cs");
    }

    /// <summary>
    /// The multi-window startup overload mounts no root of its own, so it is
    /// deliberately left alone: an app-lifetime scope there would be claimed by the
    /// first window opened through an unintercepted path. The positive controls above,
    /// in the same file, prove interception is live — so the empty list is a measurement.
    /// </summary>
    [Fact]
    public void MultiWindowRun_IsNotIntercepted()
    {
        Assert.Throws<ArgumentNullException>(() => ReactorApp.Run((Action<ReactorAppContext>)null!));

        Assert.Empty(_entered);
    }

    /// <summary>
    /// The site is taken even while the runtime flag is still off. <c>ReactorApp.Run</c>
    /// depends on this: under <c>--devtools app</c> the devtools bootstrap turns source
    /// mapping on INSIDE Run, after Run's own call site was taken, and the window mounts
    /// later still. Whether a host keeps the site is decided at mount time
    /// (<c>KeepIfEnabled</c>, unit-tested in Reactor.Tests).
    /// </summary>
    [Fact]
    public void FlagOff_StillTakesTheSiteAndClosesTheScope()
    {
        ReactorSourceMap.Enabled = false;

        Assert.Throws<ArgumentNullException>(() => ReactorApp.OpenWindow(new WindowSpec(), (Func<Component>)null!)); int expected = Line();

        AssertClaimedByTheEntryPoint(expected, "OpenWindow");
    }

    /// <summary>
    /// <c>Run</c> → startup options: the site Run claims is what <c>ReactorApplication</c>
    /// receives for the primary window (<c>OnLaunched</c> passes
    /// <c>RootMountSite</c> to <c>OpenWindowCore</c>, the same path <c>OpenWindow</c> takes
    /// and the live selftests cover). Proven for both an explicit opt-in (flag already on)
    /// and the devtools shape (flag off when Run is called, switched on before mount).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_CarriesItsOwnSiteIntoTheStartupOptions(bool enabledAtCall)
    {
        ReactorSourceMap.Enabled = enabledAtCall;
        ReactorAppOptions? captured = null;
        ReactorApp.StartApplicationForTest = o => captured = o;
        try
        {
            ReactorApp.Run<Probe>(new WindowSpec { Title = "options probe" }); int expected = Line();

            Assert.NotNull(captured);
            Assert.Equal(expected, captured!.RootMountSite?.LineNumber);
            Assert.EndsWith("RootMountInterceptionTests.cs", captured.RootMountSite!.Value.FilePath, StringComparison.Ordinal);
            Assert.Equal(0, ReactorSourceMap.OpenRootMountScopeCountForTest);

            ReactorApp.Run(new WindowSpec { Title = "options probe" }, _ => TextBlock("x")); int expectedRender = Line();
            Assert.Equal(expectedRender, captured!.RootMountSite?.LineNumber);
        }
        finally
        {
            ReactorApp.StartApplicationForTest = null;
        }
    }

    private sealed class CapturingDevtoolsHost : IReactorDevtoolsHost
    {
        public ReactorDevtoolsBootRequest? Captured { get; private set; }

        public bool TryHandleCommandLine(ReactorDevtoolsBootRequest request)
        {
            Captured = request;
            return true;
        }

        public Element? BuildDevtoolsMenu(
            Func<IEnumerable<MenuFlyoutItemBase>>? items, string glyph, string toolTip, string? automationId) => null;
    }

    /// <summary>
    /// <c>--devtools run</c> takes over startup before <c>StartApplication</c>, so the site
    /// <c>Run</c> claimed must travel in the boot request for the preview to report it.
    /// The flag is off at the call, as it is under devtools (the preview turns it on later).
    /// </summary>
    [Fact]
    public void Run_UnderDevtoolsRun_HandsItsSiteToTheDevtoolsHost()
    {
        const string switchName = "Reactor.DevtoolsSupport";
        ReactorSourceMap.Enabled = false;
        var host = new CapturingDevtoolsHost();
        var previous = ReactorDevtoolsBootstrap.CurrentForTests;
        ReactorDevtoolsBootstrap.Register(host);
        ReactorAppOptions? started = null;
        ReactorApp.StartApplicationForTest = o => started = o;
        ReactorApp.CommandLineArgsForTest = ["app.exe", "--devtools", "run"];
        try
        {
            AppContext.SetSwitch(switchName, true);

            ReactorApp.Run<Probe>(new WindowSpec { Title = "devtools probe" }); int expected = Line();

            Assert.Null(started);
            Assert.NotNull(host.Captured);
            Assert.Equal(expected, host.Captured!.RootMountSite?.LineNumber);
            Assert.EndsWith("RootMountInterceptionTests.cs", host.Captured.RootMountSite!.Value.FilePath, StringComparison.Ordinal);
            Assert.Equal(0, ReactorSourceMap.OpenRootMountScopeCountForTest);
        }
        finally
        {
            AppContext.SetSwitch(switchName, false);
            ReactorApp.CommandLineArgsForTest = null;
            ReactorApp.StartApplicationForTest = null;
            ReactorDevtoolsBootstrap.RestoreForTests(previous);
        }
    }
}
