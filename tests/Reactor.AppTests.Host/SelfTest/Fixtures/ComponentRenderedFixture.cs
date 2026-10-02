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
            bump.Any(e => Id(e) == alwaysId && Reason(e) == ComponentRenderTrace.Reasons.Parent));

        // ── Consumed context change ──────────────────────────────────────
        H.ClickButton("theme");
        await Harness.Render();
        var theme = Take();
        Console.WriteLine("# theme: " + string.Join(", ", theme.Select(e => $"{Name(e)}#{Id(e)}:{Reason(e)}")));
        H.Check("ComponentRendered_Theme_ConsumerIsContext",
            theme.Any(e => Id(e) == contextId && Reason(e) == ComponentRenderTrace.Reasons.Context));
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
        hostControl.Dispose();

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
    }
}

internal sealed class RenderedHostControlRoot : Component
{
    public Action? Bump;

    public override Element Render()
    {
        var (n, setN) = UseState(0);
        Bump = () => setN(n + 1);
        return TextBlock($"host control root {n}");
    }
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

    public override Element Render() => TextBlock("always");
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
