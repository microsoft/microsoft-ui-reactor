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
            using var host = H.CreateHost();
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
            using var host = H.CreateHost();
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
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// Turning source mapping on late tags boundaries that were mounted while it was off —
    /// including a cached one that later renders skip by reference and so never re-evaluate
    /// <c>NeedsTag</c>. The parent re-renders with the SAME element instance; without the
    /// activation refresh the wrapper would stay untagged forever.
    /// </summary>
    internal class LateEnableTagsCachedBoundaries(Harness h) : SelfTestFixtureBase(h)
    {
        private static readonly Element CachedBoundary = new ComponentElement(typeof(Probe));

        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = false;
            using var host = H.CreateHost();
            try
            {
                Action<int>? setN = null;
                host.Mount(ctx =>
                {
                    var (n, set) = ctx.UseState(0);
                    setN = set;
                    return VStack(TextBlock($"hostdiag-late:{n}"), CachedBoundary);
                });
                await Harness.Render();
                await host.WaitForIdleAsync();

                var wrapper = WrapperOf(H, "hostdiag-component");
                H.Check("HostDiagLate_WrapperFound", wrapper is not null);
                if (wrapper is null) return;
                H.Check("HostDiagLate_UntaggedWhileOff", Reconciler.GetElementTag(wrapper) is null);

                // Called on the UI thread, so the refresh runs inline.
                ReactorSourceMap.Enabled = true;
                H.Check("HostDiagLate_TaggedOnEnable",
                    Reconciler.GetElementTag(wrapper) is ComponentElement comp && comp.ComponentType == typeof(Probe));

                // A re-render that skips the cached boundary by reference keeps the tag.
                setN!(1);
                await Harness.WaitFor(() => H.FindControl<TextBlock>(t => t.Text == "hostdiag-late:1") is not null);
                var after = WrapperOf(H, "hostdiag-component");
                H.Check("HostDiagLate_SameWrapperAfterSkip", ReferenceEquals(after, wrapper));
                H.Check("HostDiagLate_StillTaggedAfterSkip", after is not null && Reconciler.GetElementTag(after) is ComponentElement);
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }
    /// <summary>
    /// A component that is a <c>Flyout</c> target shares its wrapper with the decorator, which
    /// tags it with the <c>FlyoutElement</c> and resolves <c>OnOpened</c>/<c>OnClosed</c>
    /// through that tag. Turning source mapping on late must leave that tag alone (an
    /// undecorated sibling still gets its component tag), and the callbacks must keep firing.
    /// </summary>
    internal class LateEnableKeepsDecoratorTag(Harness h) : SelfTestFixtureBase(h)
    {
        private sealed class FlyoutTarget : Component
        {
            public override Element Render() => TextBlock("hostdiag-flyout-target");
        }

        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = false;
            using var host = H.CreateHost();
            try
            {
                var opened = 0;
                var closed = 0;
                host.Mount(_ => VStack(
                    new ComponentElement(typeof(Probe)),
                    Flyout(new ComponentElement(typeof(FlyoutTarget)), TextBlock("hostdiag-flyout-content")) with
                    {
                        OnOpened = () => opened++,
                        OnClosed = () => closed++,
                    }));
                await Harness.Render();
                await host.WaitForIdleAsync();

                var decorated = WrapperOf(H, "hostdiag-flyout-target");
                var plain = WrapperOf(H, "hostdiag-component");
                H.Check("HostDiagDecor_WrappersFound", decorated is not null && plain is not null);
                if (decorated is null || plain is null) return;
                H.Check("HostDiagDecor_FlyoutTagBeforeEnable", Reconciler.GetElementTag(decorated) is FlyoutElement);

                ReactorSourceMap.Enabled = true;
                H.Check("HostDiagDecor_FlyoutTagKept", Reconciler.GetElementTag(decorated) is FlyoutElement,
                    $"tag={Reconciler.GetElementTag(decorated)?.GetType().Name ?? "null"}");
                H.Check("HostDiagDecor_PlainBoundaryTagged", Reconciler.GetElementTag(plain) is ComponentElement);

                var flyout = Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.GetAttachedFlyout(decorated);
                H.Check("HostDiagDecor_FlyoutAttached", flyout is not null);
                if (flyout is null) return;
                flyout.ShowAt(decorated);
                H.Check("HostDiagDecor_OnOpenedFires", await Harness.WaitFor(() => opened > 0));
                flyout.Hide();
                H.Check("HostDiagDecor_OnClosedFires", await Harness.WaitFor(() => closed > 0));
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// <c>host?.Mount(...)</c> on a non-null host is intercepted like a plain call: the
    /// conditional access compiles to a member binding, which the generator now handles.
    /// (The null-receiver control lives in <c>RootMountInterceptionTests</c>.)
    /// </summary>
    internal class ConditionalAccessMountReportsItsSite(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            using var created = H.CreateHost();
            try
            {
                ReactorHost? host = created;
                host?.Mount(new Probe()); var mountLine = Line();
                await created.WaitForIdleAsync();

                var info = InfoFor(created);
                H.Check("HostDiagCond_Listed", info is not null);
#if REACTOR_SOURCEMAP
                H.Check("HostDiagCond_MountSite",
                    info?.MountSite?.LineNumber == mountLine,
                    $"site={info?.MountSite?.ToShortString() ?? "null"} expected line {mountLine}");
#else
                _ = mountLine;
                H.Skip("HostDiagCond_MountSite", SkipReason);
#endif
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// <c>GetHosts()</c> is documented as callable from any thread. A background reader
    /// snapshots a real host while the UI thread remounts it with two different sites. The host
    /// stays alive and every mount passes a non-null site with mapping on, so every snapshot must
    /// list the host with exactly one of the two sites: never missing, null, or a torn mix.
    /// </summary>
    internal class BackgroundSnapshotsDuringRemounts(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            using var host = H.CreateHost();
            try
            {
                var a = new SourceLocation("RemountA.cs", 111, 0);
                var b = new SourceLocation("RemountBbbbbbbb.cs", 222222, 0);
                var root = new Probe();
                host.Mount(root, a);
                await host.WaitForIdleAsync();

                var stop = 0;
                var reads = 0;
                var bad = 0;
                var missing = 0;
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                // A dedicated thread, not the pool: on a loaded CI runner a pool task may not be
                // scheduled before the remount loop ends, and then nothing overlaps.
                var reader = Task.Factory.StartNew(() =>
                {
                    while (Volatile.Read(ref stop) == 0)
                    {
                        var info = ReactorDiagnostics.GetHosts().FirstOrDefault(i => ReferenceEquals(i.Host, host));
                        Interlocked.Increment(ref reads);
                        started.TrySetResult();
                        if (info?.MountSite is not { } site)
                            Interlocked.Increment(ref missing);
                        else if (site != a && site != b)
                            Interlocked.Increment(ref bad);
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(10)));
                var readsBefore = Volatile.Read(ref reads);

                // Remount until the reader has overlapped a meaningful number of snapshots.
                var budget = global::System.Diagnostics.Stopwatch.StartNew();
                int remounts = 0;
                while (remounts < 5000 || (Volatile.Read(ref reads) - readsBefore < 200 && budget.Elapsed < TimeSpan.FromSeconds(10)))
                {
                    host.Mount(root, (remounts & 1) == 0 ? b : a);
                    remounts++;
                }
                if ((remounts & 1) == 1)
                    host.Mount(root, a);
                var overlapped = Volatile.Read(ref reads) - readsBefore;

                Volatile.Write(ref stop, 1);
                await reader;
                await host.WaitForIdleAsync();

                H.Check("HostDiagRace_ReaderRan", overlapped > 0, $"reads during remounts={overlapped}, total={reads}, remounts={remounts}");
                H.Check("HostDiagRace_NoTornSites", bad == 0, $"torn={bad} of {reads}");
                H.Check("HostDiagRace_HostAndSiteAlwaysPresent", missing == 0, $"missing host or site in {missing} of {reads}");
                H.Check("HostDiagRace_FinalSite", InfoFor(host)?.MountSite == a);
                // Mounting the active instance again keeps the root, but the site follows the call.
                host.Mount(root, b);
                H.Check("HostDiagRace_SameInstanceRemountUpdatesSite", InfoFor(host)?.MountSite == b,
                    $"site={InfoFor(host)?.MountSite?.ToShortString() ?? "null"}");
            }
            finally
            {
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
            using var island = new ReactorHostControl();
            using var factoryIsland = new ReactorHostControl { ComponentFactory = static () => new IslandRoot() };
            object? sentinel = null;
            try
            {
                var root = new IslandRoot();
                island.Mount(root); var mountLine = Line();

                // An unrelated root-mount scope left open while the factory island loads.
                // ComponentFactory has no call site in app code, so it must not borrow this
                // one; the sentinel being still unclaimed afterwards is what proves it.
                sentinel = ReactorSourceMap.EnterRootMountSite("Sentinel.cs", 123, 0);
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
                // Mounting the active instance again keeps the root, but the site follows the call.
                island.Mount(root); var againLine = Line();
                var againSite = InfoFor(island)?.MountSite;
                H.Check("HostDiagIsland_SameInstanceRemountUpdatesSite",
                    againSite?.LineNumber == againLine && againLine != mountLine,
                    $"site={againSite?.ToShortString() ?? "null"} expected line {againLine}");
#else
                _ = mountLine;
                H.Skip("HostDiagIsland_MountSite", SkipReason);
                H.Skip("HostDiagIsland_SameInstanceRemountUpdatesSite", SkipReason);
#endif

                var factoryInfo = InfoFor(factoryIsland);
                H.Check("HostDiagIsland_FactoryIslandListed", factoryInfo is not null);
                H.Check("HostDiagIsland_FactoryIslandHasNoMountSite",
                    factoryInfo is not null && factoryInfo.RootComponentName == ExpectedName<IslandRoot>() && factoryInfo.MountSite is null);

                // Mount(Func<RenderContext, Element>) is a separate overload with its own
                // root/site bookkeeping: a remount replaces the component root with the render
                // function and reports the new call's line.
                island.Mount(_ => TextBlock("hostdiag-island-render")); var renderMountLine = Line();
                var rerendered = await Harness.WaitFor(
                    () => H.FindControl<TextBlock>(t => t.Text == "hostdiag-island-render") is not null, maxPasses: 40, perPassMs: 10);
                H.Check("HostDiagIsland_RenderFuncRemounted", rerendered);
                var renderInfo = InfoFor(island);
                H.Check("HostDiagIsland_RenderFuncRoot",
                    renderInfo is not null && renderInfo.RootComponentName is null
                        && RenderFunctionNamedFor(renderInfo.RootRenderFunctionName, nameof(RunAsync)),
                    $"render={renderInfo?.RootRenderFunctionName ?? "null"} component={renderInfo?.RootComponentName ?? "null"}");
                H.Check("HostDiagIsland_RenderFuncRootControl",
                    renderInfo?.RootControl is not null && ReferenceEquals(renderInfo.RootControl, island.Content));
#if REACTOR_SOURCEMAP
                H.Check("HostDiagIsland_RenderFuncMountSite",
                    renderInfo?.MountSite?.LineNumber == renderMountLine,
                    $"site={renderInfo?.MountSite?.ToShortString() ?? "null"} expected line {renderMountLine}");
#else
                _ = renderMountLine;
                H.Skip("HostDiagIsland_RenderFuncMountSite", SkipReason);
#endif

                island.Dispose();
                H.Check("HostDiagIsland_DisposedRemoved", InfoFor(island) is null);
            }
            finally
            {
                ReactorSourceMap.ExitRootMountSite(sentinel);
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

                // A render-function remount on top of a component root replaces it (issue
                // #1326: the component root is retired, and no longer keeps rendering), so the
                // snapshot describes the render function — its name AND its mount site.
                win.Mount(static _ => TextBlock("hostdiag-render-remount")); var renderLine = Line();
                await win.Host.WaitForIdleAsync();
                var afterRender = ReactorDiagnostics.GetHosts().FirstOrDefault(i => ReferenceEquals(i.ReactorWindow, win));
                H.Check("HostDiagWin_RenderRemountReplacesComponentRoot",
                    afterRender?.RootComponentName is null && afterRender?.RootRenderFunctionName is not null,
                    $"component={afterRender?.RootComponentName ?? "null"} render={afterRender?.RootRenderFunctionName ?? "null"}");

#if REACTOR_SOURCEMAP
                H.Check("HostDiagWin_OpenWindowSite",
                    info.MountSite is { } site
                    && site.LineNumber == openLine
                    && site.FilePath.EndsWith("HostDiagnosticsFixtures.cs", StringComparison.Ordinal),
                    $"site={info.MountSite?.ToShortString() ?? "null"} expected line {openLine}");
                H.Check("HostDiagWin_RemountSite",
                    remounted?.MountSite?.LineNumber == remountLine,
                    $"site={remounted?.MountSite?.ToShortString() ?? "null"} expected line {remountLine}");
                H.Check("HostDiagWin_RenderRemountReportsItsSite",
                    afterRender?.MountSite?.LineNumber == renderLine,
                    $"site={afterRender?.MountSite?.ToShortString() ?? "null"} expected line {renderLine}");
#else
                _ = openLine; _ = remountLine; _ = renderLine;
                H.Skip("HostDiagWin_OpenWindowSite", SkipReason);
                H.Skip("HostDiagWin_RemountSite", SkipReason);
                H.Skip("HostDiagWin_RenderRemountReportsItsSite", SkipReason);
#endif
            }
            finally
            {
                win?.Close(); // idempotent
                await Task.Delay(80);
                ReactorSourceMap.Enabled = previous;
            }
        }
    }
}
