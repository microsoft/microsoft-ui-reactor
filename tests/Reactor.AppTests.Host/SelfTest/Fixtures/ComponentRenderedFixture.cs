using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// <c>ReactorEventSource.ComponentRendered</c> (EventId 40) driven through the real
/// reconciler and host: one event per component render with the right reason, none for
/// a component the memo gate skipped, and a <c>componentId</c> that
/// <see cref="ReactorTrace.GetComponentControl"/> / <see cref="ReactorTrace.TryGetComponentId"/>
/// resolve to the component's wrapper.
///
/// <para>The headless suite (<c>ComponentRenderTraceTests</c>,
/// <c>ComponentRenderedEventTests</c>) covers classification, the registry and the wire
/// contract; this is the only tier that can prove the reconciler actually calls them on
/// mount, update and the host root.</para>
/// </summary>
internal class ComponentRendered_ReasonsAndIdsFollowTheReconciler(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var events = new List<ReactorEvent>();
        using var subscription = ReactorTrace.Subscribe(
            e => { if (e.EventName == nameof(ReactorEventSource.ComponentRendered)) lock (events) events.Add(e); },
            EventLevel.Verbose,
            ReactorEventSource.Keywords.RenderDetail);

        // NativeAOT strips EventSource unless EventSourceSupport=true (see
        // ReconcileTraceDepthFixture); report a skip rather than a vacuous pass.
        if (!ComponentRenderTrace.IsEnabled)
        {
            H.Skip("ComponentRendered_Mount", "EventSource disabled (NativeAOT)");
            return;
        }

        var host = H.CreateHost();
        host.Mount(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            var (show, setShow) = ctx.UseState(true);
            var (theme, setTheme) = ctx.UseState(0);
            return VStack(4,
                TextBlock($"root {n}"),
                show ? Component<RenderedPropsChild, int>(n) : null,
                Component<RenderedStatefulChild>(),
                Component<RenderedContextChild>(),
                Component<RenderedAlwaysChild>(),
                RenderEachTime(c => TextBlock($"func theme {c.UseContext(RenderedContextChild.Theme)}")),
                Memo(_ => TextBlock("memo constant"), "constant"),
                Button("bump", () => setN(n + 1)),
                Button("theme", () => setTheme(theme + 1)),
                Button("hide", () => setShow(false))).Provide(RenderedContextChild.Theme, theme);
        });
        await Harness.Render();

        List<ReactorEvent> Take()
        {
            lock (events)
            {
                var copy = events.ToList();
                events.Clear();
                return copy;
            }
        }

        static string Name(ReactorEvent e) => (string)e.Payload[0]!;
        static long Id(ReactorEvent e) => (long)e.Payload[1]!;
        static string Reason(ReactorEvent e) => (string)e.Payload[2]!;

        // ── Mount ────────────────────────────────────────────────────────
        var mount = Take();
        Console.WriteLine("# mount: " + string.Join(", ", mount.Select(e => $"{Name(e)}#{Id(e)}:{Reason(e)}")));
        H.Check("ComponentRendered_Mount_AllReasonsAreMount",
            mount.Count >= 4 && mount.All(e => Reason(e) == ComponentRenderTrace.Reasons.Mount));
        H.Check("ComponentRendered_Mount_RootReported",
            mount.Any(e => Name(e) == nameof(FuncElement)));
        H.Check("ComponentRendered_Mount_ClassComponentsByTypeName",
            mount.Any(e => Name(e) == nameof(RenderedPropsChild))
            && mount.Any(e => Name(e) == nameof(RenderedStatefulChild)));
        H.Check("ComponentRendered_Mount_MemoReported",
            mount.Any(e => Name(e) == nameof(MemoElement)));
        H.Check("ComponentRendered_Mount_IdsDistinctAndNonZero",
            mount.Select(Id).Distinct().Count() == mount.Count && mount.All(e => Id(e) != 0));

        var propsChildId = Id(mount.First(e => Name(e) == nameof(RenderedPropsChild)));
        var statefulId = Id(mount.First(e => Name(e) == nameof(RenderedStatefulChild)));
        var contextId = Id(mount.First(e => Name(e) == nameof(RenderedContextChild)));
        var alwaysId = Id(mount.First(e => Name(e) == nameof(RenderedAlwaysChild)));
        var rootId = Id(mount.First(e => Name(e) == nameof(FuncElement)));
        // The host root reports first; the other FuncElement is the context-consuming child.
        var funcConsumerId = mount.Where(e => Name(e) == nameof(FuncElement)).Select(Id).Skip(1).FirstOrDefault();

        // ── Id ↔ control ─────────────────────────────────────────────────
        var wrapper = ReactorTrace.GetComponentControl(propsChildId);
        H.Check("ComponentRendered_GetComponentControl_ResolvesWrapper",
            wrapper is Microsoft.UI.Xaml.Controls.Border b
            && b.Child is Microsoft.UI.Xaml.Controls.TextBlock tb && tb.Text == "props child 0");
        H.Check("ComponentRendered_TryGetComponentId_RoundTrips",
            wrapper is not null && ReactorTrace.TryGetComponentId(wrapper, out var back) && back == propsChildId);

        // ── Root state change ────────────────────────────────────────────
        H.ClickButton("bump");
        await Harness.Render();
        var bump = Take();
        Console.WriteLine("# bump: " + string.Join(", ", bump.Select(e => $"{Name(e)}#{Id(e)}:{Reason(e)}")));
        H.Check("ComponentRendered_Bump_RootIsState",
            bump.Any(e => Name(e) == nameof(FuncElement) && Reason(e) == ComponentRenderTrace.Reasons.State));
        H.Check("ComponentRendered_Bump_PropsChildIsPropsWithSameId",
            bump.Any(e => Id(e) == propsChildId && Reason(e) == ComponentRenderTrace.Reasons.Props));
        // Memo-gated: a propless Component (ShouldUpdate() == false) and a Memo with
        // constant deps are skipped, so they must NOT report a render.
        H.Check("ComponentRendered_Bump_SkippedComponentsSilent",
            !bump.Any(e => Id(e) == statefulId) && !bump.Any(e => Name(e) == nameof(MemoElement))
            && !bump.Any(e => Id(e) == contextId));
        // A propless Component whose ShouldUpdate() is true has no gate that could skip it.
        H.Check("ComponentRendered_Bump_UngatedChildIsParent",
            bump.Any(e => Id(e) == alwaysId && Reason(e) == ComponentRenderTrace.Reasons.Parent)
            && bump.Any(e => Id(e) == funcConsumerId && Reason(e) == ComponentRenderTrace.Reasons.Parent));

        // ── Consumed context change ──────────────────────────────────────
        H.ClickButton("theme");
        await Harness.Render();
        var theme = Take();
        Console.WriteLine("# theme: " + string.Join(", ", theme.Select(e => $"{Name(e)}#{Id(e)}:{Reason(e)}")));
        H.Check("ComponentRendered_Theme_ConsumerIsContext",
            theme.Any(e => Id(e) == contextId && Reason(e) == ComponentRenderTrace.Reasons.Context));
        H.Check("ComponentRendered_Theme_FunctionConsumerIsContext",
            funcConsumerId != 0
            && theme.Any(e => Id(e) == funcConsumerId && Reason(e) == ComponentRenderTrace.Reasons.Context));
        H.Check("ComponentRendered_Theme_UngatedProplessConsumerIsContext",
            theme.Any(e => Id(e) == alwaysId && Reason(e) == ComponentRenderTrace.Reasons.Context));
        H.Check("ComponentRendered_Theme_NonConsumersSilent",
            !theme.Any(e => Id(e) == propsChildId || Id(e) == statefulId));

        // ── Forced full render (memo gates bypassed) ─────────────────────
        host.RequestRender(force: true);
        await Harness.Render();
        var forced = Take();
        Console.WriteLine("# forced: " + string.Join(", ", forced.Select(e => $"{Name(e)}#{Id(e)}:{Reason(e)}")));
        H.Check("ComponentRendered_Forced_RootAndGatedChildrenAreForced",
            forced.Any(e => Id(e) == rootId && Reason(e) == ComponentRenderTrace.Reasons.Forced)
            && forced.Any(e => Id(e) == statefulId && Reason(e) == ComponentRenderTrace.Reasons.Forced)
            && forced.Any(e => Id(e) == propsChildId && Reason(e) == ComponentRenderTrace.Reasons.Forced));

        // ── Hot-reload pass ──────────────────────────────────────────────
        HotReloadService.UpdateApplication(null);
        host.RequestRender(force: true);
        await Harness.Render();
        var hotReload = Take();
        Console.WriteLine("# hotReload: " + string.Join(", ", hotReload.Select(e => $"{Name(e)}#{Id(e)}:{Reason(e)}")));
        H.Check("ComponentRendered_HotReload_RootAndChildrenAreHotReload",
            hotReload.Any(e => Id(e) == rootId && Reason(e) == ComponentRenderTrace.Reasons.HotReload)
            && hotReload.Any(e => Id(e) == statefulId && Reason(e) == ComponentRenderTrace.Reasons.HotReload));

        // ── Child's own state change ─────────────────────────────────────
        H.ClickButton("inner");
        await Harness.Render();
        var inner = Take();
        Console.WriteLine("# inner: " + string.Join(", ", inner.Select(e => $"{Name(e)}#{Id(e)}:{Reason(e)}")));
        H.Check("ComponentRendered_Inner_ChildIsState",
            inner.Any(e => Id(e) == statefulId && Reason(e) == ComponentRenderTrace.Reasons.State));

        // Full detach of a still-mapped wrapper drops its id in both directions.
        var statefulWrapper = ReactorTrace.GetComponentControl(statefulId) as FrameworkElement;
        long beforeDetachId = 0;
        bool mappedBefore = statefulWrapper is not null && ReactorTrace.TryGetComponentId(statefulWrapper, out beforeDetachId);
        Console.WriteLine($"# detach: statefulId={statefulId} wrapper={statefulWrapper?.GetType().Name ?? "null"} reverse={mappedBefore}:{beforeDetachId}");
        H.Check("ComponentRendered_Detach_WrapperMappedBefore", mappedBefore && beforeDetachId == statefulId);
        if (statefulWrapper is not null) Reconciler.DetachReactorState(statefulWrapper);
        H.Check("ComponentRendered_Detach_ForgetsBothDirections",
            statefulWrapper is not null
            && !ReactorTrace.TryGetComponentId(statefulWrapper, out _)
            && ReactorTrace.GetComponentControl(statefulId) is null);

        // ── Unmount drops the id, both directions ────────────────────────
        H.ClickButton("hide");
        await Harness.Render();
        H.Check("ComponentRendered_Unmount_IdNoLongerResolves",
            ReactorTrace.GetComponentControl(propsChildId) is null);
        H.Check("ComponentRendered_Unmount_WrapperNoLongerMapsToId",
            wrapper is not null && !ReactorTrace.TryGetComponentId(wrapper, out _));
    }
}

/// <summary>
/// The other host and the error path: a <see cref="ReactorHostControl"/>'s root reports
/// <c>mount</c> then <c>state</c> under one id that resolves to its content, and a root
/// whose Render() throws still reports the render (as a throwing child component does).
/// </summary>
internal class ComponentRendered_HostControlRootAndThrowingRoot(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var events = new List<ReactorEvent>();
        using var subscription = ReactorTrace.Subscribe(
            e => { if (e.EventName == nameof(ReactorEventSource.ComponentRendered)) lock (events) events.Add(e); },
            EventLevel.Verbose,
            ReactorEventSource.Keywords.RenderDetail);

        if (!ComponentRenderTrace.IsEnabled)
        {
            H.Skip("ComponentRendered_HostControl", "EventSource disabled (NativeAOT)");
            return;
        }

        List<ReactorEvent> For(string name)
        {
            lock (events) return events.Where(e => (string)e.Payload[0]! == name).ToList();
        }

        var hostControl = new ReactorHostControl();
        var root = new RenderedHostControlRoot();
        hostControl.Mount(root);
        H.SetContent(new Microsoft.UI.Xaml.Controls.Border { Child = hostControl });
        // A standalone ReactorHostControl is not ReactorApp.ActiveHost, so Harness.Render()
        // cannot await its loop; poll instead.
        H.Check("ComponentRendered_HostControl_RootMount", await Harness.WaitFor(
            () => For(nameof(RenderedHostControlRoot)).Any(e => (string)e.Payload[2]! == ComponentRenderTrace.Reasons.Mount),
            maxPasses: 32, perPassMs: 10));
        var rootId = For(nameof(RenderedHostControlRoot)).Select(e => (long)e.Payload[1]!).FirstOrDefault();
        H.Check("ComponentRendered_HostControl_RootIdResolvesToContent", await Harness.WaitFor(
            () => rootId != 0 && ReactorTrace.GetComponentControl(rootId) is not null,
            maxPasses: 32, perPassMs: 10));

        root.Bump?.Invoke();
        H.Check("ComponentRendered_HostControl_RootUpdateIsStateWithSameId", await Harness.WaitFor(
            () => For(nameof(RenderedHostControlRoot)).Any(e =>
                (long)e.Payload[1]! == rootId && (string)e.Payload[2]! == ComponentRenderTrace.Reasons.State),
            maxPasses: 32, perPassMs: 10));
        var leafId = For(nameof(RenderedHostControlLeaf)).Select(e => (long)e.Payload[1]!).FirstOrDefault();
        var leafWrapper = leafId != 0 ? ReactorTrace.GetComponentControl(leafId) : null;
        H.Check("ComponentRendered_HostControl_LeafResolves", leafWrapper is not null);
        hostControl.Dispose();
        // Dispose drops the tree without unmounting it; its components must stop resolving.
        H.Check("ComponentRendered_HostControl_DisposeForgetsComponentIds",
            ReactorTrace.GetComponentControl(leafId) is null
            && (leafWrapper is null || !ReactorTrace.TryGetComponentId(leafWrapper, out _))
            && ReactorTrace.GetComponentControl(rootId) is null);

        var throwingHost = new ReactorHostControl();
        throwingHost.Mount(new RenderedThrowingRoot());
        H.SetContent(new Microsoft.UI.Xaml.Controls.Border { Child = throwingHost });
        H.Check("ComponentRendered_ThrowingRoot_StillReported", await Harness.WaitFor(
            () => For(nameof(RenderedThrowingRoot)).Any(e => (string)e.Payload[2]! == ComponentRenderTrace.Reasons.Mount),
            maxPasses: 32, perPassMs: 10));
        var throwingId = For(nameof(RenderedThrowingRoot)).Select(e => (long)e.Payload[1]!).FirstOrDefault();
        H.Check("ComponentRendered_ThrowingRoot_IdResolvesToTheErrorPanel",
            throwingId != 0 && ReactorTrace.GetComponentControl(throwingId) is { } panel
            && ReferenceEquals(panel, throwingHost.Content));
        throwingHost.Dispose();
        H.SetContent(null);

        // Same contract through ReactorHost (the window host): the throwing root's id
        // resolves to the error panel it displays.
        var host = H.CreateHost();
        host.Mount(new RenderedThrowingWindowRoot());
        await Harness.Render();
        var windowThrowingId = For(nameof(RenderedThrowingWindowRoot)).Select(e => (long)e.Payload[1]!).FirstOrDefault();
        var resolved = windowThrowingId != 0 ? ReactorTrace.GetComponentControl(windowThrowingId) : null;
        bool panelUnderResolved = false;
        for (DependencyObject? d = H.FindTextContaining("Render error: InvalidOperationException");
             d is not null && !panelUnderResolved;
             d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d))
            panelUnderResolved = resolved is not null && ReferenceEquals(d, resolved);
        H.Check("ComponentRendered_ThrowingWindowRoot_IdResolvesToTheErrorPanel", panelUnderResolved);

        // The root rendered fine but the pass failed later (a setter threw during
        // reconcile): the outer catch shows the error panel, and the root's id follows it.
        var passHost = H.CreateHost();
        passHost.Mount(new RenderedPassFailureRoot());
        await Harness.Render();
        var passId = For(nameof(RenderedPassFailureRoot)).Select(e => (long)e.Payload[1]!).FirstOrDefault();
        var passResolved = passId != 0 ? ReactorTrace.GetComponentControl(passId) : null;
        var passText = H.FindTextContaining(RenderedPassFailureRoot.Message);
        bool passPanelUnderResolved = false;
        for (DependencyObject? d = passText; d is not null && !passPanelUnderResolved;
             d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d))
            passPanelUnderResolved = passResolved is not null && ReferenceEquals(d, passResolved);
        Console.WriteLine($"# pass failure: id={passId} resolved={passResolved?.GetType().Name ?? "null"} text={(passText is null ? "missing" : "found")}");
        H.Check("ComponentRendered_PassFailure_RootIdResolvesToTheErrorPanel", passPanelUnderResolved);

        // A root that rendered a healthy tree and then throws: the error panel replaces
        // that tree without unmounting it, so its components must stop resolving.
        var flipHost = H.CreateHost();
        var flipRoot = new RenderedFlipRoot();
        flipHost.Mount(flipRoot);
        await Harness.Render();
        var flipLeafId = For(nameof(RenderedFlipLeaf)).Select(e => (long)e.Payload[1]!).FirstOrDefault();
        var flipLeafWrapper = flipLeafId != 0 ? ReactorTrace.GetComponentControl(flipLeafId) : null;
        H.Check("ComponentRendered_FailureAfterHealthy_LeafResolvedBefore", flipLeafWrapper is not null);
        flipRoot.Explode?.Invoke();
        await Harness.Render();
        H.Check("ComponentRendered_FailureAfterHealthy_ErrorPanelShown",
            H.FindTextContaining(RenderedFlipRoot.Message) is not null);
        H.Check("ComponentRendered_FailureAfterHealthy_OldTreeIdsForgotten",
            ReactorTrace.GetComponentControl(flipLeafId) is null
            && (flipLeafWrapper is null || !ReactorTrace.TryGetComponentId(flipLeafWrapper, out _)));
    }
}

/// <summary>
/// A component whose Render() throws inside an <c>ErrorBoundary</c> still reports the
/// render (the boundary, not the reconciler, catches it), on mount and on update.
/// </summary>
internal class ComponentRendered_ErrorBoundaryCaughtRendersAreReported(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var events = new List<ReactorEvent>();
        using var subscription = ReactorTrace.Subscribe(
            e => { if (e.EventName == nameof(ReactorEventSource.ComponentRendered)) lock (events) events.Add(e); },
            EventLevel.Verbose,
            ReactorEventSource.Keywords.RenderDetail);

        if (!ComponentRenderTrace.IsEnabled)
        {
            H.Skip("ComponentRendered_ErrorBoundary", "EventSource disabled (NativeAOT)");
            return;
        }

        List<ReactorEvent> For(string name)
        {
            lock (events) return events.Where(e => (string)e.Payload[0]! == name).ToList();
        }

        var host = H.CreateHost();
        host.Mount(ctx =>
        {
            var (boom, setBoom) = ctx.UseState(false);
            return VStack(
                ErrorBoundary(Component<RenderedThrowOnMountChild>(), _ => TextBlock("mount caught")),
                ErrorBoundary(Component<RenderedBoomChild, bool>(boom), _ => TextBlock("update caught")),
                Button("boom", () => setBoom(true)));
        });
        await Harness.Render();

        H.Check("ComponentRendered_Boundary_FallbackShown", H.FindText("mount caught") is not null);
        H.Check("ComponentRendered_Boundary_ThrowingMountReported",
            For(nameof(RenderedThrowOnMountChild)).Any(e => (string)e.Payload[2]! == ComponentRenderTrace.Reasons.Mount));
        var boomId = For(nameof(RenderedBoomChild)).Select(e => (long)e.Payload[1]!).FirstOrDefault();
        H.Check("ComponentRendered_Boundary_HealthyChildMounted", boomId != 0);

        H.ClickButton("boom");
        await Harness.Render();
        H.Check("ComponentRendered_Boundary_FallbackShownOnUpdate", H.FindText("update caught") is not null);
        H.Check("ComponentRendered_Boundary_ThrowingUpdateReportedWithSameId",
            For(nameof(RenderedBoomChild)).Any(e =>
                (long)e.Payload[1]! == boomId && (string)e.Payload[2]! == ComponentRenderTrace.Reasons.Props));

        // A parent whose descendant throws on mount: the boundary discards the parent's
        // wrapper without unmounting it, so the parent's id must not resolve to it.
        var parentHost = H.CreateHost();
        parentHost.Mount(_ => ErrorBoundary(Component<RenderedThrowingSubtreeParent>(), _ => TextBlock("subtree caught")));
        await Harness.Render();
        var parentId = For(nameof(RenderedThrowingSubtreeParent)).Select(e => (long)e.Payload[1]!).FirstOrDefault();
        H.Check("ComponentRendered_Boundary_SubtreeFallbackShown", H.FindText("subtree caught") is not null);
        H.Check("ComponentRendered_Boundary_DiscardedParentReportedButUnmapped",
            parentId != 0 && ReactorTrace.GetComponentControl(parentId) is null);
    }
}

internal sealed class RenderedThrowingSubtreeParent : Component
{
    public override Element Render() => VStack(TextBlock("parent"), Component<RenderedThrowOnMountChild>());
}

/// <summary>
/// A hot-reload edit that changes the ROOT's hook order: the host resets the root and
/// retries outside the hot-reload pass. That retry (root and children) is still
/// reported as <c>hotReload</c>, not <c>forced</c>.
/// </summary>
internal class ComponentRendered_RootHookOrderRetryIsHotReload(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var events = new List<ReactorEvent>();
        using var subscription = ReactorTrace.Subscribe(
            e => { if (e.EventName == nameof(ReactorEventSource.ComponentRendered)) lock (events) events.Add(e); },
            EventLevel.Verbose,
            ReactorEventSource.Keywords.RenderDetail);

        if (!ComponentRenderTrace.IsEnabled)
        {
            H.Skip("ComponentRendered_HookOrderRetry", "EventSource disabled (NativeAOT)");
            return;
        }

        List<ReactorEvent> Take()
        {
            lock (events)
            {
                var copy = events.ToList();
                events.Clear();
                return copy;
            }
        }

        RenderedHookShapeRoot.Shape = 0;
        try
        {
            var host = H.CreateHost();
            host.Mount(new RenderedHookShapeRoot());
            await Harness.Render();
            H.Check("ComponentRendered_HookOrderRetry_InitialShape", H.FindText("hook shape v1") is not null);
            Take();

            // The "edit": the root's first hook changes type, so the hot-reload render
            // throws HookOrderException and the host retries.
            RenderedHookShapeRoot.Shape = 1;
            HotReloadService.UpdateApplication(null);
            host.RequestRender(force: true);
            H.Check("ComponentRendered_HookOrderRetry_EditApplied", await Harness.WaitFor(
                () => H.FindText("hook shape v2") is not null, maxPasses: 32, perPassMs: 10));

            var retry = Take();
            Console.WriteLine("# hook-order retry: " + string.Join(", ",
                retry.Select(e => $"{e.Payload[0]}#{e.Payload[1]}:{e.Payload[2]}")));
            H.Check("ComponentRendered_HookOrderRetry_RootIsHotReload",
                retry.Any(e => (string)e.Payload[0]! == nameof(RenderedHookShapeRoot)
                    && (string)e.Payload[2]! == ComponentRenderTrace.Reasons.HotReload)
                && !retry.Any(e => (string)e.Payload[2]! == ComponentRenderTrace.Reasons.Forced));
            H.Check("ComponentRendered_HookOrderRetry_ChildIsHotReload",
                retry.Any(e => (string)e.Payload[0]! == nameof(RenderedHotReloadLeaf)
                    && (string)e.Payload[2]! == ComponentRenderTrace.Reasons.HotReload));
        }
        finally
        {
            RenderedHookShapeRoot.Shape = 0;
        }
    }
}

/// <summary>
/// The root id follows host changes: switching a <c>ReactorHost</c> from a component
/// root to a function root renders (and reports) the new root, and once the event is
/// switched off a root that changes content stops resolving to the control it left.
/// </summary>
internal class ComponentRendered_RootMappingFollowsHostChanges(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var events = new List<ReactorEvent>();
        using var subscription = ReactorTrace.Subscribe(
            e => { if (e.EventName == nameof(ReactorEventSource.ComponentRendered)) lock (events) events.Add(e); },
            EventLevel.Verbose,
            ReactorEventSource.Keywords.RenderDetail);

        if (!ComponentRenderTrace.IsEnabled)
        {
            H.Skip("ComponentRendered_RootMapping", "EventSource disabled (NativeAOT)");
            return;
        }

        List<ReactorEvent> Take()
        {
            lock (events)
            {
                var copy = events.ToList();
                events.Clear();
                return copy;
            }
        }

        // ── Component root → function root on one ReactorHost ──────────────
        var host = H.CreateHost();
        host.Mount(new RenderedSwapComponentRoot());
        await Harness.Render();
        Take();
        host.Mount(_ => TextBlock("swap func root"));
        await Harness.Render();
        var swap = Take();
        Console.WriteLine("# swap: " + string.Join(", ", swap.Select(e => $"{e.Payload[0]}#{e.Payload[1]}:{e.Payload[2]}")));
        H.Check("ComponentRendered_RootSwap_NewRootShown",
            H.FindText("swap func root") is not null && H.FindText("swap component root") is null);
        H.Check("ComponentRendered_RootSwap_NewRootReportedNotOld",
            swap.Any(e => (string)e.Payload[0]! == nameof(FuncElement) && (string)e.Payload[2]! == ComponentRenderTrace.Reasons.Mount)
            && !swap.Any(e => (string)e.Payload[0]! == nameof(RenderedSwapComponentRoot)));

        // ── Content changes while the event is off ──────────────────────────
        var root = new RenderedHostControlRoot();
        var offHost = H.CreateHost();
        offHost.Mount(root);
        await Harness.Render();
        var rootId = Take().Where(e => (string)e.Payload[0]! == nameof(RenderedHostControlRoot))
            .Select(e => (long)e.Payload[1]!).FirstOrDefault();
        var before = rootId != 0 ? ReactorTrace.GetComponentControl(rootId) : null;
        H.Check("ComponentRendered_RootOff_ResolvedWhileOn", before is not null);

        subscription.Dispose();   // idempotent; the using disposes again harmlessly
        if (ComponentRenderTrace.IsEnabled)
        {
            H.Skip("ComponentRendered_RootOff_ForgottenWhileOff", "another ComponentRendered listener is active in this process");
            return;
        }
        root.Bump?.Invoke();
        await Harness.Render();
        H.Check("ComponentRendered_RootOff_ForgottenWhileOff",
            H.FindText("host control root 1") is not null && ReactorTrace.GetComponentControl(rootId) is null);
    }
}

internal sealed class RenderedSwapComponentRoot : Component
{
    public override Element Render() => TextBlock("swap component root");
}

internal sealed class RenderedHookShapeRoot : Component
{
    public static volatile int Shape;

    public override Element Render()
    {
        if (Shape == 0)
        {
            UseState(0);
            return VStack(TextBlock("hook shape v1"), Component<RenderedHotReloadLeaf>());
        }

        UseEffect(() => { }, "hot-reload");
        UseState(0);
        return VStack(TextBlock("hook shape v2"), Component<RenderedHotReloadLeaf>());
    }
}

internal sealed class RenderedHotReloadLeaf : Component
{
    public override Element Render() => TextBlock("hot reload leaf");
}

internal sealed class RenderedThrowOnMountChild : Component
{
    public override Element Render() => throw new InvalidOperationException("ComponentRendered selftest: boundary mount failure");
}

internal sealed class RenderedBoomChild : Component<bool>
{
    public override Element Render() =>
        Props ? throw new InvalidOperationException("ComponentRendered selftest: boundary update failure") : TextBlock("boom child ok");
}

internal sealed class RenderedPassFailureRoot : Component
{
    public const string Message = "ComponentRendered selftest: reconcile pass failure";

    public override Element Render() =>
        TextBlock("pass failure root").Set(_ => throw new InvalidOperationException(Message));
}

internal sealed class RenderedHostControlRoot : Component
{
    public Action? Bump;

    public override Element Render()
    {
        var (n, setN) = UseState(0);
        Bump = () => setN(n + 1);
        return VStack(TextBlock($"host control root {n}"), Component<RenderedHostControlLeaf>());
    }
}

internal sealed class RenderedHostControlLeaf : Component
{
    public override Element Render() => TextBlock("host control leaf");
}

internal sealed class RenderedFlipRoot : Component
{
    public const string Message = "ComponentRendered selftest: root failure after a healthy render";
    public Action? Explode;

    public override Element Render()
    {
        var (exploded, setExploded) = UseState(false);
        Explode = () => setExploded(true);
        if (exploded) throw new InvalidOperationException(Message);
        return VStack(TextBlock("flip root"), Component<RenderedFlipLeaf>());
    }
}

internal sealed class RenderedFlipLeaf : Component
{
    public override Element Render() => TextBlock("flip leaf");
}

internal sealed class RenderedThrowingRoot : Component
{
    public override Element Render() => throw new InvalidOperationException("ComponentRendered selftest: deliberate root render failure");
}

internal sealed class RenderedThrowingWindowRoot : Component
{
    public override Element Render() => throw new InvalidOperationException("ComponentRendered selftest: deliberate window-root render failure");
}

internal sealed class RenderedContextChild : Component
{
    public static readonly Context<int> Theme = new(0);

    public override Element Render() => TextBlock($"theme {UseContext(Theme)}");
}

internal sealed class RenderedAlwaysChild : Component
{
    protected internal override bool ShouldUpdate() => true;

    // Consumes the theme too: a propless component whose ShouldUpdate() is true reports
    // "parent" for a plain parent re-render but "context" when the context changed.
    public override Element Render() => TextBlock($"always {UseContext(RenderedContextChild.Theme)}");
}

internal sealed class RenderedPropsChild : Component<int>
{
    public override Element Render() => TextBlock($"props child {Props}");
}

internal sealed class RenderedStatefulChild : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return Button("inner", () => setCount(count + 1));
    }
}
