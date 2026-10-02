using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
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

    private void AssertSingleSiteAt(int expectedLine)
    {
        var site = Assert.Single(_entered);
        Assert.Equal(expectedLine, site.LineNumber);
        Assert.EndsWith("RootMountInterceptionTests.cs", site.FilePath, StringComparison.Ordinal);
        Assert.Equal(0, ReactorSourceMap.OpenRootMountScopeCountForTest);
    }

    /// <summary>
    /// The intercepted method claimed its OWN scope as its first statement — before the
    /// argument check that makes it throw — so no later mount could have taken it.
    /// </summary>
    private void AssertClaimedByTheEntryPoint(int expectedLine)
    {
        AssertSingleSiteAt(expectedLine);
        Assert.Equal(_entered, _claimed);
    }

    [Fact]
    public void OpenWindowWithComponentRoot_OpensAScopeAtTheCallSite()
    {
        Assert.Throws<ArgumentNullException>(() => ReactorApp.OpenWindow(new WindowSpec(), (Func<Component>)null!)); int expected = Line();

        AssertClaimedByTheEntryPoint(expected);
    }

    [Fact]
    public void OpenWindowWithRenderRoot_OpensAScopeAtTheCallSite()
    {
        Assert.Throws<ArgumentNullException>(() => ReactorApp.OpenWindow(new WindowSpec(), (Func<RenderContext, Element>)null!)); int expected = Line();

        AssertClaimedByTheEntryPoint(expected);
    }

    [Fact]
    public void GenericRun_OpensAScopeAtTheCallSite()
    {
        Assert.Throws<ArgumentNullException>(() => ReactorApp.Run<Probe>((WindowSpec)null!)); int expected = Line();

        AssertClaimedByTheEntryPoint(expected);
    }

    [Fact]
    public void RenderFunctionRun_OpensAScopeAtTheCallSite()
    {
        Assert.Throws<ArgumentNullException>(() => ReactorApp.Run((WindowSpec)null!, _ => TextBlock("x"))); int expected = Line();

        AssertClaimedByTheEntryPoint(expected);
    }

    [Fact]
    public void ReactorHostMount_InstanceCallIsIntercepted()
    {
        ReactorHost host = null!;

        Assert.Throws<NullReferenceException>(() => host.Mount(_ => TextBlock("x"))); int expected = Line();

        AssertSingleSiteAt(expected);
    }

    [Fact]
    public void ReactorHostControlMount_InstanceCallIsIntercepted()
    {
        ReactorHostControl control = null!;

        Assert.Throws<NullReferenceException>(() => control.Mount(new Probe())); int expected = Line();

        AssertSingleSiteAt(expected);
    }

    [Fact]
    public void ReactorWindowMount_InstanceCallIsIntercepted()
    {
        ReactorWindow window = null!;

        Assert.Throws<NullReferenceException>(() => window.Mount(new Probe())); int expected = Line();

        AssertSingleSiteAt(expected);
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

    [Fact]
    public void FlagOff_ForwardsTheCallWithoutOpeningAScope()
    {
        ReactorSourceMap.Enabled = false;

        Assert.Throws<ArgumentNullException>(() => ReactorApp.OpenWindow(new WindowSpec(), (Func<Component>)null!));

        Assert.Empty(_entered);
        Assert.Equal(0, ReactorSourceMap.OpenRootMountScopeCountForTest);
    }
}
