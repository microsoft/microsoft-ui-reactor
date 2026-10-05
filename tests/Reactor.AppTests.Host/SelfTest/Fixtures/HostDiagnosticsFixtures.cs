using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// <see cref="ReactorDiagnostics.GetHosts"/> against real hosts, and component-boundary
/// tagging on a live tree. The headless halves (registry bookkeeping, root-site scopes, the
/// <c>NeedsTag</c> predicate, generated interceptors) are pinned in <c>Reactor.Tests</c> and
/// <c>Reactor.SourceMap.Tests</c>; this proves the real <c>ReactorHost</c> /
/// <c>ReactorHostControl</c> are wired to them.
///
/// <para>The component boundaries below are built with direct record construction
/// (<c>new ComponentElement(...)</c>, <c>new FuncElement(...)</c>,
/// <c>new MemoElement(...)</c>), which the source-map generator does not intercept. They
/// therefore carry no <c>CallSite</c> and no extras bucket, so the only thing that can tag
/// their wrappers is the component-boundary arm of <c>NeedsTag</c> — and the flag-off
/// fixture runs the exact same lookup as the negative control.</para>
/// </summary>
internal static class HostDiagnosticsFixtures
{
    private static int Line([CallerLineNumber] int line = 0) => line;

    private const string SkipReason =
        "assembly built without REACTOR_SOURCEMAP (Release) - no interceptors compiled in, so there is no mount site to read";

    /// <summary>
    /// The name a root component should be reported under, spelled out independently of the
    /// product's formatter: namespace, then the nesting chain, joined with dots.
    /// </summary>
    private static string ExpectedName<T>() => typeof(T).FullName!.Replace('+', '.');

    /// <summary>
    /// The render-function name names the method that declared the lambda. NativeAOT may not
    /// keep the metadata that needs (the property is then documented to be null), so the exact
    /// name is asserted only where the runtime can describe delegates at all.
    /// </summary>
    private static bool RenderFunctionNamedFor(string? name, string declaringMethod)
    {
        var matches = name is not null && name.EndsWith("." + declaringMethod + " (lambda)", StringComparison.Ordinal);
        return global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported
            ? matches
            : name is null || matches;
    }

    private sealed class Probe : Component
    {
        public override Element Render() => TextBlock("hostdiag-component");
    }

    private sealed class IslandRoot : Component
    {
        public override Element Render() => TextBlock("hostdiag-island");
    }

    private static Element Boundaries(int n) => VStack(
        TextBlock($"hostdiag-n:{n}"),
        new ComponentElement(typeof(Probe)),
        new FuncElement(_ => TextBlock("hostdiag-func")),
        new MemoElement(_ => TextBlock("hostdiag-memo")));

    /// <summary>The Border wrapper a boundary mounts behind its rendered leaf.</summary>
    private static FrameworkElement? WrapperOf(Harness h, string leafText)
        => h.FindControl<TextBlock>(t => t.Text == leafText) is { } leaf
            ? VisualTreeHelper.GetParent(leaf) as FrameworkElement
            : null;

    private static ReactorHostInfo? InfoFor(ReactorHost host)
        => ReactorDiagnostics.GetHosts().FirstOrDefault(i => ReferenceEquals(i.Host, host));

    private static ReactorHostInfo? InfoFor(ReactorHostControl control)
        => ReactorDiagnostics.GetHosts().FirstOrDefault(i => ReferenceEquals(i.HostControl, control));

    /// <summary>
    /// A real window host is listed with its window, content target, root control, root name
    /// and mount site; its component boundaries are tagged on mount and still tagged after an
    /// update; it drops out of the list once disposed.
    /// </summary>
    internal class WindowHostIsListedAndBoundariesAreTagged(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            var host = H.CreateHost();
            try
            {
                Action<int>? setN = null;
                Func<RenderContext, Element> root = ctx =>
                {
                    var (n, set) = ctx.UseState(0);
                    setN = set;
                    return Boundaries(n);
                };
                host.Mount(root); var mountLine = Line();
                await Harness.Render();
                await host.WaitForIdleAsync();

                var info = InfoFor(host);
                H.Check("HostDiag_WindowHostListed", info is not null);
                if (info is null) return;

                H.Check("HostDiag_WindowHostKind", info.Kind == ReactorHostKind.WindowHost);
                H.Check("HostDiag_WindowHostWindow", ReferenceEquals(info.Window, H.Window));
                H.Check("HostDiag_WindowHostElementIsContentTarget",
                    info.HostElement is not null && ReferenceEquals(info.HostElement, host.ContentTarget));
                H.Check("HostDiag_WindowHostRenderRoot",
                    info.RootComponentName is null && RenderFunctionNamedFor(info.RootRenderFunctionName, nameof(RunAsync)),
                    $"render={info.RootRenderFunctionName ?? "null"} component={info.RootComponentName ?? "null"}");
                H.Check("HostDiag_WindowHostRootControl",
                    info.RootControl is not null && ReferenceEquals(info.RootControl, host.ContentTarget?.Child),
                    $"root={info.RootControl?.GetType().Name ?? "null"}");
                H.Check("HostDiag_WindowHostNotAReactorWindow", info.ReactorWindow is null);

#if REACTOR_SOURCEMAP
                H.Check("HostDiag_WindowHostMountSite",
                    info.MountSite is { } site
                    && site.LineNumber == mountLine
                    && site.FilePath.EndsWith("HostDiagnosticsFixtures.cs", StringComparison.Ordinal),
                    $"site={info.MountSite?.ToShortString() ?? "null"} expected line {mountLine}");
#else
                _ = mountLine;
                H.Skip("HostDiag_WindowHostMountSite", SkipReason);
#endif

                CheckBoundaryTags("Mount");

                setN!(1);
                await Harness.WaitFor(() => H.FindControl<TextBlock>(t => t.Text == "hostdiag-n:1") is not null);
                H.Check("HostDiag_Updated", H.FindControl<TextBlock>(t => t.Text == "hostdiag-n:1") is not null);
                CheckBoundaryTags("Update");

                host.Dispose();
                H.Check("HostDiag_DisposedHostRemoved", InfoFor(host) is null);
            }
            finally
            {
                host.Dispose();
                ReactorSourceMap.Enabled = previous;
            }
        }

        private void CheckBoundaryTags(string phase)
        {
            H.Check($"HostDiag_{phase}_ComponentWrapperTagged",
                WrapperOf(H, "hostdiag-component") is { } c
                && Reconciler.GetElementTag(c) is ComponentElement comp
                && comp.ComponentType == typeof(Probe));
            H.Check($"HostDiag_{phase}_FuncWrapperTagged",
                WrapperOf(H, "hostdiag-func") is { } f && Reconciler.GetElementTag(f) is FuncElement);
            H.Check($"HostDiag_{phase}_MemoWrapperTagged",
                WrapperOf(H, "hostdiag-memo") is { } m && Reconciler.GetElementTag(m) is MemoElement);
        }
    }

    /// <summary>
    /// Negative control for the fixture above: the same tree and the same wrapper lookup
    /// with source mapping off. The wrappers exist but carry no tag, and no mount site is
    /// recorded — so the positive checks measure the flag, not something else that tags.
    /// </summary>
    internal class FlagOffLeavesBoundariesUntagged(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = false;
            var host = H.CreateHost();
            try
            {
                host.Mount(_ => Boundaries(0));
                await Harness.Render();
                await host.WaitForIdleAsync();

                var component = WrapperOf(H, "hostdiag-component");
                var func = WrapperOf(H, "hostdiag-func");
                var memo = WrapperOf(H, "hostdiag-memo");
                H.Check("HostDiagOff_WrappersFound", component is not null && func is not null && memo is not null);
                if (component is null || func is null || memo is null) return;

                H.Check("HostDiagOff_ComponentWrapperUntagged", Reconciler.GetElementTag(component) is null);
                H.Check("HostDiagOff_FuncWrapperUntagged", Reconciler.GetElementTag(func) is null);
                H.Check("HostDiagOff_MemoWrapperUntagged", Reconciler.GetElementTag(memo) is null);

                var info = InfoFor(host);
                H.Check("HostDiagOff_HostStillListed", info is not null);
                H.Check("HostDiagOff_NoMountSite", info is not null && info.MountSite is null);
            }
            finally
            {
                host.Dispose();
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// A <c>ReactorHostControl</c> island is listed with itself as the host element and no
    /// window, reports its component root and mount site, and drops out once disposed. A
    /// second island whose root comes from <c>ComponentFactory</c> (the XAML shape) reports
    /// no mount site — it must not borrow one from anywhere else.
    /// </summary>
    internal class HostControlIsListed(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            var island = new ReactorHostControl();
            var factoryIsland = new ReactorHostControl { ComponentFactory = static () => new IslandRoot() };
            object? sentinel = null;
            try
            {
                var root = new IslandRoot();
                island.Mount(root); var mountLine = Line();

                // An unrelated root-mount scope left open while the factory island loads.
                // ComponentFactory has no call site in app code, so it must not borrow this
                // one; the sentinel being still unclaimed afterwards is what proves it.
                sentinel = ReactorSourceMap.EnterRootMountSite("Sentinel.cs", 123);
                H.SetContent(new StackPanel { Children = { island, factoryIsland } });
                var rendered = await Harness.WaitFor(
                    () => island.Content is not null && factoryIsland.Content is not null, maxPasses: 40, perPassMs: 10);
                H.Check("HostDiagIsland_Rendered", rendered);
                H.Check("HostDiagIsland_FactoryDidNotClaimAnUnrelatedScope",
                    ReactorSourceMap.TakeRootMountSite() is { LineNumber: 123 });
                ReactorSourceMap.ExitRootMountSite(sentinel);
                sentinel = null;

                var info = InfoFor(island);
                H.Check("HostDiagIsland_Listed", info is not null);
                if (info is null) return;

                H.Check("HostDiagIsland_Kind", info.Kind == ReactorHostKind.HostControl);
                H.Check("HostDiagIsland_HostElementIsItself", ReferenceEquals(info.HostElement, island));
                H.Check("HostDiagIsland_NoWindow", info.Window is null && info.Host is null && info.ReactorWindow is null);
                H.Check("HostDiagIsland_ComponentRoot",
                    info.RootComponentName == ExpectedName<IslandRoot>() && info.RootRenderFunctionName is null,
                    $"component={info.RootComponentName ?? "null"}");
                H.Check("HostDiagIsland_RootControl", info.RootControl is not null && ReferenceEquals(info.RootControl, island.Content));

#if REACTOR_SOURCEMAP
                H.Check("HostDiagIsland_MountSite",
                    info.MountSite?.LineNumber == mountLine,
                    $"site={info.MountSite?.ToShortString() ?? "null"} expected line {mountLine}");
#else
                _ = mountLine;
                H.Skip("HostDiagIsland_MountSite", SkipReason);
#endif

                var factoryInfo = InfoFor(factoryIsland);
                H.Check("HostDiagIsland_FactoryIslandListed", factoryInfo is not null);
                H.Check("HostDiagIsland_FactoryIslandHasNoMountSite",
                    factoryInfo is not null && factoryInfo.RootComponentName == ExpectedName<IslandRoot>() && factoryInfo.MountSite is null);

                island.Dispose();
                H.Check("HostDiagIsland_DisposedRemoved", InfoFor(island) is null);
            }
            finally
            {
                ReactorSourceMap.ExitRootMountSite(sentinel);
                island.Dispose();
                factoryIsland.Dispose();
                H.SetContent(null);
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// The <c>ReactorApp.OpenWindow</c> → <c>ReactorWindow</c> → <c>ReactorHost</c> path:
    /// the window's host is listed with its <c>ReactorWindow</c>, and both the opening call
    /// and a later <c>ReactorWindow.Mount</c> report their own lines — the site travels down
    /// through <c>OpenWindowCore</c> / <c>MountAndActivate</c> rather than being re-claimed.
    /// </summary>
    internal class ReactorWindowReportsItsMountSites(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            if (ReactorApp.UIDispatcher is null)
                ReactorApp.UIDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;

            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            ReactorWindow? win = null;
            try
            {
                var spec = new WindowSpec { Title = "HostDiag ReactorWindow", Width = 240, Height = 160 };
                win = ReactorApp.OpenWindow(spec, static () => new Probe()); var openLine = Line();
                await win.Host.WaitForIdleAsync();
                await Harness.Render(50);

                var info = ReactorDiagnostics.GetHosts().FirstOrDefault(i => ReferenceEquals(i.ReactorWindow, win));
                H.Check("HostDiagWin_Listed", info is not null);
                if (info is null) return;

                H.Check("HostDiagWin_Kind", info.Kind == ReactorHostKind.WindowHost);
                H.Check("HostDiagWin_Host", ReferenceEquals(info.Host, win.Host));
                H.Check("HostDiagWin_Window", ReferenceEquals(info.Window, win.NativeWindow));
                H.Check("HostDiagWin_Root", info.RootComponentName == ExpectedName<Probe>(), $"component={info.RootComponentName ?? "null"}");

                win.Mount(new IslandRoot()); var remountLine = Line();
                await win.Host.WaitForIdleAsync();
                var remounted = ReactorDiagnostics.GetHosts().FirstOrDefault(i => ReferenceEquals(i.ReactorWindow, win));
                H.Check("HostDiagWin_RemountedRoot", remounted?.RootComponentName == ExpectedName<IslandRoot>());

                // A render-function remount on top of a component root: ReactorHost keeps
                // rendering the component (pre-existing behaviour), so the snapshot must keep
                // describing the component — its type AND its mount site — not the ignored
                // render function.
                win.Mount(static _ => TextBlock("hostdiag-ignored-render"));
                await win.Host.WaitForIdleAsync();
                var afterRender = ReactorDiagnostics.GetHosts().FirstOrDefault(i => ReferenceEquals(i.ReactorWindow, win));
                H.Check("HostDiagWin_IgnoredRenderRemountKeepsComponentRoot",
                    afterRender?.RootComponentName == ExpectedName<IslandRoot>() && afterRender.RootRenderFunctionName is null);

#if REACTOR_SOURCEMAP
                H.Check("HostDiagWin_OpenWindowSite",
                    info.MountSite is { } site
                    && site.LineNumber == openLine
                    && site.FilePath.EndsWith("HostDiagnosticsFixtures.cs", StringComparison.Ordinal),
                    $"site={info.MountSite?.ToShortString() ?? "null"} expected line {openLine}");
                H.Check("HostDiagWin_RemountSite",
                    remounted?.MountSite?.LineNumber == remountLine,
                    $"site={remounted?.MountSite?.ToShortString() ?? "null"} expected line {remountLine}");
                H.Check("HostDiagWin_IgnoredRenderRemountKeepsComponentSite",
                    afterRender?.MountSite?.LineNumber == remountLine,
                    $"site={afterRender?.MountSite?.ToShortString() ?? "null"} expected line {remountLine}");
#else
                _ = openLine; _ = remountLine;
                H.Skip("HostDiagWin_OpenWindowSite", SkipReason);
                H.Skip("HostDiagWin_RemountSite", SkipReason);
                H.Skip("HostDiagWin_IgnoredRenderRemountKeepsComponentSite", SkipReason);
#endif
            }
            finally
            {
                try { win?.Close(); } catch { /* already closed */ }
                await Task.Delay(80);
                ReactorSourceMap.Enabled = previous;
            }
        }
    }
}
