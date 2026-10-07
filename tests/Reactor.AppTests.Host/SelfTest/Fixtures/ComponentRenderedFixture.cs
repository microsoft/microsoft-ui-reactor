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

        // A healthy component that finished mounting before a later sibling threw: the
        // boundary discards it too, so its mapping must be rolled back.
        var siblingHost = H.CreateHost();
        siblingHost.Mount(_ => ErrorBoundary(
            VStack(Component<RenderedHealthySibling>(), Component<RenderedThrowOnMountChild>()),
            _ => TextBlock("sibling caught")));
        await Harness.Render();
        var healthyId = For(nameof(RenderedHealthySibling)).Select(e => (long)e.Payload[1]!).FirstOrDefault();
        H.Check("ComponentRendered_Boundary_SiblingFallbackShown", H.FindText("sibling caught") is not null);
        H.Check("ComponentRendered_Boundary_HealthySiblingReportedButRolledBack",
            healthyId != 0 && ReactorTrace.GetComponentControl(healthyId) is null);

        // An exception the reconciler does not convert to a fallback (OutOfMemoryException),
        // outside any boundary: it propagates to the host, but the render is still reported.
        var fatalHost = H.CreateHost();
        fatalHost.Mount(_ => VStack(Component<RenderedFatalChild>()));
        await Harness.Render();
        H.Check("ComponentRendered_FatalChild_ReportedBeforePropagating",
            For(nameof(RenderedFatalChild)).Any(e => (string)e.Payload[2]! == ComponentRenderTrace.Reasons.Mount));
    }
}

internal sealed class RenderedFatalChild : Component
{
    public override Element Render() => throw new OutOfMemoryException("ComponentRendered selftest: simulated fatal render failure");
}

internal sealed class RenderedHealthySibling : Component
{
    public override Element Render() => TextBlock("healthy sibling");
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
            // The aborted attempt ran Render() too, so the root reports twice, same id.
            var rootRetryEvents = retry.Where(e => (string)e.Payload[0]! == nameof(RenderedHookShapeRoot)).ToList();
            H.Check("ComponentRendered_HookOrderRetry_RootAbortedAttemptReported",
                rootRetryEvents.Count == 2
                && rootRetryEvents.All(e => (string)e.Payload[2]! == ComponentRenderTrace.Reasons.HotReload)
                && rootRetryEvents.Select(e => (long)e.Payload[1]!).Distinct().Count() == 1);

            // A CHILD whose hook order changes is recovered inside the hot-reload pass by
            // the reconciler: both the aborted attempt and the retry are reported.
            RenderedHookShapeChild.Shape = 0;
            var childHost = H.CreateHost();
            childHost.Mount(_ => VStack(Component<RenderedHookShapeChild>()));
            await Harness.Render();
            Take();
            RenderedHookShapeChild.Shape = 1;
            HotReloadService.UpdateApplication(null);
            childHost.RequestRender(force: true);
            H.Check("ComponentRendered_HookOrderRetry_ChildEditApplied", await Harness.WaitFor(
                () => H.FindText("child hook shape v2") is not null, maxPasses: 32, perPassMs: 10));
            var childEvents = Take().Where(e => (string)e.Payload[0]! == nameof(RenderedHookShapeChild)).ToList();
            Console.WriteLine("# child hook-order retry: " + string.Join(", ",
                childEvents.Select(e => $"{e.Payload[0]}#{e.Payload[1]}:{e.Payload[2]}")));
            H.Check("ComponentRendered_HookOrderRetry_ChildAbortedAttemptReported",
                childEvents.Count == 2
                && childEvents.All(e => (string)e.Payload[2]! == ComponentRenderTrace.Reasons.HotReload)
                && childEvents.Select(e => (long)e.Payload[1]!).Distinct().Count() == 1);
        }
        finally
        {
            RenderedHookShapeRoot.Shape = 0;
            RenderedHookShapeChild.Shape = 0;
        }
    }
}

internal sealed class RenderedHookShapeChild : Component
{
    public static volatile int Shape;

    public override Element Render()
    {
        if (Shape == 0)
        {
            UseState(0);
            return TextBlock("child hook shape v1");
        }

        UseEffect(() => { }, "hot-reload");
        UseState(0);
        return TextBlock("child hook shape v2");
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
        var firstRoot = new RenderedSwapComponentRoot();
        host.Mount(firstRoot);
        await Harness.Render();
        H.Check("ComponentRendered_RootSwap_OldRootAliveBefore", !firstRoot.CleanedUp);
        Take();
        bool firstFuncCleaned = false;
        host.Mount(c =>
        {
            c.UseEffect(() => () => firstFuncCleaned = true);
            return TextBlock("swap func root");
        });
        await Harness.Render();
        H.Check("ComponentRendered_RootSwap_OldRootEffectsCleanedUp", firstRoot.CleanedUp);
        var swap = Take();
        Console.WriteLine("# swap: " + string.Join(", ", swap.Select(e => $"{e.Payload[0]}#{e.Payload[1]}:{e.Payload[2]}")));
        H.Check("ComponentRendered_RootSwap_NewRootShown",
            H.FindText("swap func root") is not null && H.FindText("swap component root") is null);
        H.Check("ComponentRendered_RootSwap_NewRootReportedNotOld",
            swap.Any(e => (string)e.Payload[0]! == nameof(FuncElement) && (string)e.Payload[2]! == ComponentRenderTrace.Reasons.Mount)
            && !swap.Any(e => (string)e.Payload[0]! == nameof(RenderedSwapComponentRoot)));

        // Every remount retires the outgoing root, whichever kind either side is.
        bool secondFuncCleaned = false;
        host.Mount(c =>
        {
            c.UseEffect(() => () => secondFuncCleaned = true);
            return TextBlock("swap func root 2");
        });
        await Harness.Render();
        H.Check("ComponentRendered_RootSwap_FuncToFuncCleansUp", firstFuncCleaned && !secondFuncCleaned);
        var secondRoot = new RenderedSwapComponentRoot();
        host.Mount(secondRoot);
        await Harness.Render();
        H.Check("ComponentRendered_RootSwap_FuncToComponentCleansUp", secondFuncCleaned && !secondRoot.CleanedUp);
        var thirdRoot = new RenderedSwapComponentRoot();
        host.Mount(thirdRoot);
        await Harness.Render();
        H.Check("ComponentRendered_RootSwap_ComponentToComponentCleansUp", secondRoot.CleanedUp);
        // Re-mounting the active instance is not a swap: its effects stay alive.
        host.Mount(thirdRoot);
        await Harness.Render();
        H.Check("ComponentRendered_RootSwap_SameInstanceRemountKeepsEffects",
            !thirdRoot.CleanedUp && H.FindText("swap component root") is not null);
        // A retired instance mounted again later is a fresh mount: its effect runs again.
        host.Mount(firstRoot);
        await Harness.Render();
        H.Check("ComponentRendered_RootSwap_RetiredInstanceRemountsFresh",
            thirdRoot.CleanedUp && firstRoot.EffectRuns == 2 && !firstRoot.CleanedUp);

        // A replacement root whose first render is null: nothing reconciles the old root's
        // tree away, so the host releases it (its child's cleanup runs, its content goes).
        var releaseHost = H.CreateHost();
        var cleanupProbe = new RenderedCleanupProbeRoot();
        releaseHost.Mount(cleanupProbe);
        await Harness.Render();
        H.Check("ComponentRendered_NullReplacement_OldTreeLiveBefore",
            H.FindText("cleanup probe child") is not null && RenderedCleanupProbeChild.Cleanups == 0);
        releaseHost.Mount(new RenderedNullRoot());
        await Harness.Render();
        H.Check("ComponentRendered_NullReplacement_OldTreeReleased",
            H.FindText("cleanup probe child") is null && RenderedCleanupProbeChild.Cleanups == 1,
            $"cleanups={RenderedCleanupProbeChild.Cleanups}");

        // The release finishes even when one of the old tree's cleanups throws: the other
        // components' cleanups still run and the old content still goes.
        // With a handler, the failure is reported to it as a Cleanup error.
        var releaseLog = new List<RenderError>();
        var throwingReleaseHost = H.CreateHost();
        throwingReleaseHost.RenderErrorHandler = e => { releaseLog.Add(e); return null; };
        throwingReleaseHost.Mount(new RenderedThrowingCleanupTreeRoot());
        await Harness.Render();
        H.Check("ComponentRendered_NullReplacement_ThrowingCleanup_LiveBefore",
            H.FindText("cleanup probe child") is not null && RenderedCleanupProbeChild.Cleanups == 0);
        throwingReleaseHost.Mount(new RenderedNullRoot());
        await Harness.Render();
        H.Check("ComponentRendered_NullReplacement_ThrowingCleanup_RestStillReleased",
            H.FindText("cleanup probe child") is null && H.FindText("throwing cleanup child") is null
                && RenderedCleanupProbeChild.Cleanups == 1,
            $"cleanups={RenderedCleanupProbeChild.Cleanups}");
        H.Check("ComponentRendered_NullReplacement_ThrowingCleanup_ReportedToHandler",
            releaseLog.Any(e => e.Source == RenderErrorSource.Cleanup
                && e.Exception.Message == RenderedThrowingCleanupChild.Message),
            string.Join(",", releaseLog.Select(e => e.Source)));

        // With no handler, the release still completes, then the failure leaves the release
        // like any render-time failure: the render loop shows the built-in panel for it.
        var priorDefault = ReactorApp.DefaultRenderErrorHandler;
        ReactorApp.DefaultRenderErrorHandler = null;
        try
        {
            var bareReleaseHost = H.CreateHost();
            bareReleaseHost.Mount(new RenderedThrowingCleanupTreeRoot());
            await Harness.Render();
            bareReleaseHost.Mount(new RenderedNullRoot());
            await Harness.Render();
            H.Check("ComponentRendered_NullReplacement_ThrowingCleanup_NoHandlerSurfacesAfterRelease",
                RenderedCleanupProbeChild.Cleanups == 1 && H.FindText("cleanup probe child") is null
                    && H.FindTextContaining(RenderedThrowingCleanupChild.Message) is not null,
                $"cleanups={RenderedCleanupProbeChild.Cleanups}");
        }
        finally
        {
            ReactorApp.DefaultRenderErrorHandler = priorDefault;
        }

        // A replacement root that throws on its first render (built-in panel, no handler):
        // the previous root's tree is released, not abandoned with its effects live.
        var previousDefault = ReactorApp.DefaultRenderErrorHandler;
        ReactorApp.DefaultRenderErrorHandler = null;
        try
        {
            var throwHost = H.CreateHost();
            throwHost.Mount(new RenderedCleanupProbeRoot());
            await Harness.Render();
            H.Check("ComponentRendered_ThrowingReplacement_OldTreeLiveBefore",
                H.FindText("cleanup probe child") is not null && RenderedCleanupProbeChild.Cleanups == 0);
            throwHost.Mount(new RenderedAppThrowingRoot());
            await Harness.Render();
            H.Check("ComponentRendered_ThrowingReplacement_OldTreeReleased",
                H.FindText("cleanup probe child") is null && RenderedCleanupProbeChild.Cleanups == 1
                    && H.FindTextContaining("Render error: InvalidOperationException") is not null,
                $"cleanups={RenderedCleanupProbeChild.Cleanups}");
        }
        finally
        {
            ReactorApp.DefaultRenderErrorHandler = previousDefault;
        }

        // A root whose Render() returns null still rendered.
        var nullHost = H.CreateHost();
        var nullRoot = new RenderedNullRoot();
        nullHost.Mount(nullRoot);
        await Harness.Render();
        // ...and commits like any render: its effects run.
        H.Check("ComponentRendered_NullRoot_EffectsFlushed", nullRoot.EffectRan);
        H.Check("ComponentRendered_NullRoot_Reported",
            Take().Any(e => (string)e.Payload[0]! == nameof(RenderedNullRoot)
                && (string)e.Payload[2]! == ComponentRenderTrace.Reasons.Mount));

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

/// <summary>
/// The same contracts when the app supplies the fallback through a
/// <see cref="RenderErrorHandler"/> (issue #1291): the throwing render is still reported,
/// and ids follow the fallback the handler installed rather than the built-in panel.
/// </summary>
internal class ComponentRendered_AppFallbackFollowsTheHandler(Harness h) : SelfTestFixtureBase(h)
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
            H.Skip("ComponentRendered_AppFallback", "EventSource disabled (NativeAOT)");
            return;
        }

        List<ReactorEvent> For(string name)
        {
            lock (events) return events.Where(e => (string)e.Payload[0]! == name).ToList();
        }

        // Ids resolve to the outermost control standing in for the component; an app
        // fallback is wrapped in an internal guard boundary, so check containment.
        static bool Contains(UIElement? ancestor, DependencyObject? descendant)
        {
            for (var d = descendant; d is not null && ancestor is not null; d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d))
                if (ReferenceEquals(d, ancestor)) return true;
            return false;
        }

        Element? Handler(RenderError e) => e.IsHostLevel
            ? Component<RenderedAppFallback>()
            : TextBlock("in-tree app fallback");

        // ── Root throws on mount: the handler's component stands in for it ──
        var host = H.CreateHost();
        host.RenderErrorHandler = Handler;
        host.Mount(new RenderedAppThrowingRoot());
        await Harness.Render();
        var rootId = For(nameof(RenderedAppThrowingRoot)).Select(e => (long)e.Payload[1]!).FirstOrDefault();
        var fallbackId = For(nameof(RenderedAppFallback)).Select(e => (long)e.Payload[1]!).FirstOrDefault();
        var fallbackWrapper = fallbackId != 0 ? ReactorTrace.GetComponentControl(fallbackId) : null;
        Console.WriteLine($"# app fallback: root={rootId} fallback={fallbackId} wrapper={fallbackWrapper?.GetType().Name ?? "null"}");
        H.Check("ComponentRendered_AppFallback_Shown", H.FindText("app fallback") is not null);
        H.Check("ComponentRendered_AppFallback_ThrowingRootReported",
            rootId != 0 && For(nameof(RenderedAppThrowingRoot)).Any(e => (string)e.Payload[2]! == ComponentRenderTrace.Reasons.Mount));
        H.Check("ComponentRendered_AppFallback_FallbackComponentMapped",
            fallbackWrapper is not null && ReactorTrace.TryGetComponentId(fallbackWrapper, out var back) && back == fallbackId);
        var rootControl = ReactorTrace.GetComponentControl(rootId);
        H.Check("ComponentRendered_AppFallback_RootIdResolvesToTheFallback",
            fallbackWrapper is not null && Contains(rootControl, fallbackWrapper));

        // ── Root throws after a healthy render: the old tree is reconciled away ──
        var flipHost = H.CreateHost();
        flipHost.RenderErrorHandler = Handler;
        var flipRoot = new RenderedFlipRoot();
        flipHost.Mount(flipRoot);
        await Harness.Render();
        var leafIds = For(nameof(RenderedFlipLeaf)).Select(e => (long)e.Payload[1]!).Distinct().ToList();
        var leafId = leafIds.LastOrDefault();
        H.Check("ComponentRendered_AppFallback_HealthyLeafMapped", leafId != 0 && ReactorTrace.GetComponentControl(leafId) is not null);
        flipRoot.Explode?.Invoke();
        await Harness.Render();
        var flipRootId = For(nameof(RenderedFlipRoot)).Select(e => (long)e.Payload[1]!).LastOrDefault();
        var flipFallbackId = For(nameof(RenderedAppFallback)).Select(e => (long)e.Payload[1]!).LastOrDefault();
        H.Check("ComponentRendered_AppFallback_AfterHealthy_OldLeafForgotten", ReactorTrace.GetComponentControl(leafId) is null);
        H.Check("ComponentRendered_AppFallback_AfterHealthy_RootIdFollowsTheFallback",
            flipFallbackId != fallbackId && ReactorTrace.GetComponentControl(flipFallbackId) is { } flipWrapper
            && Contains(ReactorTrace.GetComponentControl(flipRootId), flipWrapper));

        // ── A child throws into the handler's in-tree fallback ─────────────
        var childHost = H.CreateHost();
        childHost.RenderErrorHandler = Handler;
        childHost.Mount(_ => VStack(Component<RenderedThrowOnMountChild>()));
        await Harness.Render();
        var childId = For(nameof(RenderedThrowOnMountChild)).Select(e => (long)e.Payload[1]!).LastOrDefault();
        H.Check("ComponentRendered_AppFallback_InTreeShown", H.FindText("in-tree app fallback") is not null);
        H.Check("ComponentRendered_AppFallback_InTreeChildReportedAndMapped",
            childId != 0
            && Contains(ReactorTrace.GetComponentControl(childId), H.FindText("in-tree app fallback")));
    }
}

/// <summary>
/// A root whose <c>Render()</c> is left by an exception the app asked to propagate
/// (<c>RenderError.Propagate()</c> in nested Reactor work, declined by the app's unhandled
/// callback) shows no fallback, but its render still ran and is reported.
/// </summary>
internal class ComponentRendered_PropagatedRootFailureReported(Harness h) : SelfTestFixtureBase(h)
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
            H.Skip("ComponentRendered_PropagatedRoot", "EventSource disabled (NativeAOT)");
            return;
        }

        var previousCallback = ReactorApplication.OnUnhandledException;
        ReactorApplication.OnUnhandledException = _ => false;
        using var nestedScope = new NestedWindowScope("ComponentRendered propagated root");
        var nestedWindow = nestedScope.Window;
        var created = nestedScope.Hosts;
        try
        {
            var host = H.CreateHost();
            Exception? escaped = null;
            try
            {
                host.Mount(new RenderedNestedPropagatingRoot(nestedWindow, created));
            }
            catch (InvalidOperationException ex) { escaped = ex; }
            await Harness.Render();

            List<ReactorEvent> rootEvents;
            lock (events) rootEvents = events.Where(e => (string)e.Payload[0]! == nameof(RenderedNestedPropagatingRoot)).ToList();
            H.Check("ComponentRendered_PropagatedRoot_Escaped", escaped is not null, escaped?.Message ?? "(nothing escaped)");
            H.Check("ComponentRendered_PropagatedRoot_RenderReported",
                rootEvents.Any(e => (string)e.Payload[2]! == ComponentRenderTrace.Reasons.Mount));
        }
        finally
        {
            ReactorApplication.OnUnhandledException = previousCallback;
        }
    }
}

/// <summary>A secondary window plus the hosts nested work created on it; disposes both.</summary>
internal sealed class NestedWindowScope : IDisposable
{
    public NestedWindowScope(string title)
    {
        Window = new Window { Title = title };
        Window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(300, 200));
        Window.Activate();
    }

    public Window Window { get; }
    public List<ReactorHost> Hosts { get; } = new();

    public void Dispose()
    {
        foreach (var host in Hosts) host.Dispose();
        Window.Close();
    }
}

/// <summary>
/// Replacing a root whose effect cleanup throws still completes (issue #1291 routing): with
/// a handler the failure is reported and the new root is installed; with none it escapes
/// from Mount, but only after the new root is installed, and the failed cleanup is not left
/// armed to fail again on the next Mount.
/// </summary>
internal class ComponentRendered_RootReplacementSurvivesThrowingCleanup(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        // With a handler: reported as a Cleanup failure, Mount does not throw.
        var log = new List<RenderError>();
        var handled = H.CreateHost();
        handled.RenderErrorHandler = e => { log.Add(e); return null; };
        handled.Mount(new RenderedThrowingCleanupRoot());
        await Harness.Render();
        Exception? handledEscape = null;
        try { handled.Mount(_ => TextBlock("replacement handled")); }
        catch (InvalidOperationException ex) { handledEscape = ex; }
        await Harness.Render();
        H.Check("ComponentRendered_RetireCleanup_Handled_Reported",
            handledEscape is null && log.Any(e => e.Source == RenderErrorSource.Cleanup),
            handledEscape?.Message ?? string.Join(",", log.Select(e => e.Source)));
        H.Check("ComponentRendered_RetireCleanup_Handled_NewRootShown", H.FindText("replacement handled") is not null);

        // Without a handler: the failure escapes Mount, after the new root is installed.
        var previousDefault = ReactorApp.DefaultRenderErrorHandler;
        ReactorApp.DefaultRenderErrorHandler = null;
        try
        {
            var bare = H.CreateHost();
            bare.Mount(new RenderedThrowingCleanupRoot());
            await Harness.Render();
            Exception? escaped = null;
            try { bare.Mount(_ => TextBlock("replacement bare")); }
            catch (InvalidOperationException ex) { escaped = ex; }
            await Harness.Render();
            H.Check("ComponentRendered_RetireCleanup_Bare_Escapes",
                escaped?.Message == RenderedThrowingCleanupRoot.Message, escaped?.Message ?? "(nothing escaped)");
            H.Check("ComponentRendered_RetireCleanup_Bare_NewRootShown", H.FindText("replacement bare") is not null);
            Exception? second = null;
            try { bare.Mount(_ => TextBlock("replacement bare 2")); }
            catch (InvalidOperationException ex) { second = ex; }
            await Harness.Render();
            H.Check("ComponentRendered_RetireCleanup_Bare_NotRearmed",
                second is null && H.FindText("replacement bare 2") is not null, second?.Message ?? "");
        }
        finally
        {
            ReactorApp.DefaultRenderErrorHandler = previousDefault;
        }
    }
}

internal sealed class RenderedThrowingCleanupRoot : Component
{
    public const string Message = "ComponentRendered selftest: root cleanup failure";

    public override Element Render()
    {
        UseEffect(() => () => throw new InvalidOperationException(Message));
        return TextBlock("throwing cleanup root");
    }
}

// Starts nested Reactor work during its own render: a host whose first (inline) render
// fails and whose handler asks to propagate.
internal sealed class RenderedNestedPropagatingRoot(Window window, List<ReactorHost> created) : Component
{
    public override Element Render()
    {
        var nested = new ReactorHost(window) { RenderErrorHandler = e => { e.Propagate(); return null; } };
        created.Add(nested);
        nested.Mount(_ => throw new InvalidOperationException("ComponentRendered selftest: nested render propagated"));
        return TextBlock("unreachable");
    }
}

internal sealed class RenderedAppThrowingRoot : Component
{
    public override Element Render() => throw new InvalidOperationException("ComponentRendered selftest: root failure for the app handler");
}

internal sealed class RenderedAppFallback : Component
{
    public override Element Render() => TextBlock("app fallback");
}

internal sealed class RenderedCleanupProbeRoot : Component
{
    public override Element Render()
    {
        RenderedCleanupProbeChild.Cleanups = 0;
        return VStack(Component<RenderedCleanupProbeChild>());
    }
}

internal sealed class RenderedThrowingCleanupTreeRoot : Component
{
    public override Element Render()
    {
        RenderedCleanupProbeChild.Cleanups = 0;
        return VStack(Component<RenderedThrowingCleanupChild>(), Component<RenderedCleanupProbeChild>());
    }
}

internal sealed class RenderedThrowingCleanupChild : Component
{
    public const string Message = "ComponentRendered selftest: child cleanup failure";

    public override Element Render()
    {
        UseEffect(() => () => throw new InvalidOperationException(Message));
        return TextBlock("throwing cleanup child");
    }
}

internal sealed class RenderedCleanupProbeChild : Component
{
    public static int Cleanups;

    public override Element Render()
    {
        UseEffect(() => () => Cleanups++);
        return TextBlock("cleanup probe child");
    }
}

internal sealed class RenderedNullRoot : Component
{
    public bool EffectRan;

    public override Element Render()
    {
        UseEffect(() => EffectRan = true);
        return null!;
    }
}

internal sealed class RenderedSwapComponentRoot : Component
{
    public volatile bool CleanedUp;
    public int EffectRuns;

    public override Element Render()
    {
        UseEffect(() =>
        {
            EffectRuns++;
            CleanedUp = false;
            return () => CleanedUp = true;
        });
        return TextBlock("swap component root");
    }
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
