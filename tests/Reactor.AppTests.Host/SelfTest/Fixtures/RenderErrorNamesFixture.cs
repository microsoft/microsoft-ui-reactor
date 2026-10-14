using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// <c>ReactorEventSource.RenderError</c> names the component that threw — not its element
/// record (<c>ComponentElement`1</c>) — on every path that replaces a render with the
/// error fallback: a child's first render (mount — class, function and memo components),
/// a child's re-render (update), and the root of both <c>ReactorHost</c> and
/// <c>ReactorHostControl</c> (component and render function). An explicitly thrown
/// <c>OutOfMemoryException</c> outside any boundary — which the host still recovers from —
/// is reported too. Before the fix the mount
/// and root paths emitted nothing at all,
/// and the update path reported the element type unless the Render keyword was also on.
///
/// <para>Subscribes to <c>Errors</c> ONLY, which is the configuration that exposed the
/// bug: with <c>Render</c> enabled too, the update path borrowed the correct name from
/// the render-span bookkeeping and hid it.</para>
/// </summary>
internal class RenderErrorNames_ComponentTypeOnEveryPath(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var names = new List<string>();
        using var subscription = ReactorTrace.Subscribe(
            e =>
            {
                if (e.EventName == nameof(ReactorEventSource.RenderError)
                    && e.Payload[1] as string is nameof(RenderErrorProbeException) or nameof(OutOfMemoryException))
                {
                    var name = (string)e.Payload[0]!;
                    if (e.Payload[1] as string == nameof(OutOfMemoryException)) name += OomTag;
                    lock (names) names.Add(name);
                }
            },
            EventLevel.Error,
            ReactorEventSource.Keywords.Errors);

        if (!ReactorEventSource.Log.IsEnabled(EventLevel.Error, ReactorEventSource.Keywords.Errors))
        {
            H.Skip("RenderErrorNames_Mount", "EventSource disabled (NativeAOT)");
            return;
        }

        List<string> Take()
        {
            lock (names)
            {
                var copy = names.ToList();
                names.Clear();
                return copy;
            }
        }

        // ── Mount: Component<T, TProps> throwing on its first render ─────
        var host = H.CreateHost();
        host.Mount(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            return VStack(4,
                Component<ThrowOnMountCounter, int>(n),
                Component<ThrowOnUpdateCounter, int>(n),
                RenderEachTime(_ => throw new RenderErrorProbeException("func mount")),
                Memo(_ => throw new RenderErrorProbeException("memo mount"), "stable"),
                Button("bump", () => setN(n + 1)));
        });
        await Harness.Render();

        var mount = Take();
        Console.WriteLine("# mount RenderError: " + string.Join(", ", mount));
        H.Check("RenderErrorNames_Mount_NamesTheComponent", mount.Contains(nameof(ThrowOnMountCounter)));
        // Function and memo components have no type of their own: they report their element.
        H.Check("RenderErrorNames_Mount_FuncComponentReported", mount.Count(n => n == nameof(FuncElement)) == 1);
        H.Check("RenderErrorNames_Mount_MemoComponentReported", mount.Count(n => n == nameof(MemoElement)) == 1);
        H.Check("RenderErrorNames_Mount_NoElementTypeName",
            !mount.Any(n => n.StartsWith("ComponentElement", StringComparison.Ordinal)));

        // ── Update: a component that renders once, then throws ───────────
        H.ClickButton("bump");
        await Harness.Render();

        var update = Take();
        Console.WriteLine("# update RenderError: " + string.Join(", ", update));
        H.Check("RenderErrorNames_Update_NamesTheComponent", update.Contains(nameof(ThrowOnUpdateCounter)));
        H.Check("RenderErrorNames_Update_NoElementTypeName",
            !update.Any(n => n.StartsWith("ComponentElement", StringComparison.Ordinal)));

        // ── Derived function / memo element records ──────────────────────
        // FuncElement / MemoElement are non-sealed; a derived record is named by its own type
        // on mount, as it is on update and unmount.
        H.CreateHost().Mount(_ => VStack(4,
            new DerivedFuncElement(_ => throw new RenderErrorProbeException("derived func")),
            new DerivedMemoElement(_ => throw new RenderErrorProbeException("derived memo"))));
        await Harness.Render();
        var derived = Take();
        Console.WriteLine("# derived func/memo mount RenderError: " + string.Join(", ", derived));
        H.Check("RenderErrorNames_Mount_DerivedFuncElementNamed",
            derived.Count(n => n == nameof(DerivedFuncElement)) == 1);
        H.Check("RenderErrorNames_Mount_DerivedMemoElementNamed",
            derived.Count(n => n == nameof(DerivedMemoElement)) == 1);

        // ── Fatal exceptions outside any ErrorBoundary ───────────────────
        // OOM/SO skip the fallback arm and propagate to the host, whose outer catch shows
        // its own fallback — a recovery, so they must be reported at the throw site too.
        // Each case gets its own host: the host-level fallback replaces the whole tree.
        async Task<List<string>> MountAlone(Element child)
        {
            H.CreateHost().Mount(_ => VStack(child));
            await Harness.Render();
            return Take();
        }

        var oomClass = await MountAlone(Component<ThrowsOutOfMemory>());
        var oomFunc = await MountAlone(RenderEachTime(_ => throw new OutOfMemoryException("func")));
        var oomMemo = await MountAlone(Memo(_ => throw new OutOfMemoryException("memo"), "stable"));
        Console.WriteLine("# unbounded OOM mount RenderError: "
            + string.Join(" | ", new[] { oomClass, oomFunc, oomMemo }.Select(l => string.Join(", ", l))));
        H.Check("RenderErrorNames_Mount_OutOfMemory_ReportedOnce",
            oomClass.Count(n => n == nameof(ThrowsOutOfMemory) + OomTag) == 1);
        H.Check("RenderErrorNames_Mount_FuncOutOfMemory_ReportedOnce",
            oomFunc.Count(n => n == nameof(FuncElement) + OomTag) == 1);
        H.Check("RenderErrorNames_Mount_MemoOutOfMemory_ReportedOnce",
            oomMemo.Count(n => n == nameof(MemoElement) + OomTag) == 1);

        var oomUpdateHost = H.CreateHost();
        oomUpdateHost.Mount(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            return VStack(4,
                Component<ThrowsOutOfMemoryOnUpdate, int>(n),
                Button("oom bump", () => setN(n + 1)));
        });
        await Harness.Render();
        Take();
        H.ClickButton("oom bump");
        await Harness.Render();

        var oomUpdate = Take();
        Console.WriteLine("# unbounded OOM update RenderError: " + string.Join(", ", oomUpdate));
        H.Check("RenderErrorNames_Update_OutOfMemory_ReportedOnce",
            oomUpdate.Count(n => n == nameof(ThrowsOutOfMemoryOnUpdate) + OomTag) == 1);

        // ── Host root component ──────────────────────────────────────────
        var rootHost = H.CreateHost();
        rootHost.Mount(new ThrowingRoot());
        await Harness.Render();

        var root = Take();
        Console.WriteLine("# root RenderError: " + string.Join(", ", root));
        H.Check("RenderErrorNames_Root_NamesTheComponent", root.Count(n => n == nameof(ThrowingRoot)) == 1);

        // ── Host root render function ────────────────────────────────────
        var rootFuncHost = H.CreateHost();
        rootFuncHost.Mount(_ => throw new RenderErrorProbeException("root func"));
        await Harness.Render();

        var rootFunc = Take();
        Console.WriteLine("# root func RenderError: " + string.Join(", ", rootFunc));
        H.Check("RenderErrorNames_RootFunc_Reported", rootFunc.Count(n => n == nameof(FuncElement)) == 1);

        // ── ReactorHostControl root component and render function ────────
        // A standalone ReactorHostControl doesn't register with ReactorApp.ActiveHost, so
        // Harness.Render() can't wait on its render loop — give it wall-clock time
        // (same as HostingCoverageFixtures).
        var componentControl = new ReactorHostControl();
        componentControl.Mount(new ThrowingRoot());
        var funcControl = new ReactorHostControl();
        funcControl.Mount(_ => throw new RenderErrorProbeException("control root func"));
        H.SetContent(new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Children = { componentControl, funcControl },
        });
        await Harness.Render(200);

        var control = Take();
        Console.WriteLine("# host control RenderError: " + string.Join(", ", control));
        H.Check("RenderErrorNames_HostControl_Root_NamesTheComponent",
            control.Count(n => n == nameof(ThrowingRoot)) == 1);
        H.Check("RenderErrorNames_HostControl_RootFunc_Reported",
            control.Count(n => n == nameof(FuncElement)) == 1);

        componentControl.Dispose();
        funcControl.Dispose();
        H.SetContent(null);

        // ── Root effect failures (component and render-function roots) ───
        // A root's effects are flushed by the host, not inside the root's render catch, so
        // the host reports them — like a child's effect-flush failure. Mount-only effects,
        // so each throws exactly once.
        var effectHost = H.CreateHost();
        effectHost.Mount(new ThrowingRootEffect());
        await Harness.Render();
        var effectRoot = Take();
        var effectFuncHost = H.CreateHost();
        effectFuncHost.Mount(ctx =>
        {
            ctx.UseEffect(() => throw new RenderErrorProbeException("root func effect"), Array.Empty<object>());
            return TextBlock("root func effect rendered");
        });
        await Harness.Render();
        var effectRootFunc = Take();
        Console.WriteLine("# root effect RenderError: " + string.Join(", ", effectRoot)
            + " | " + string.Join(", ", effectRootFunc));
        H.Check("RenderErrorNames_RootEffect_NamesTheComponent",
            effectRoot.Count == 1 && effectRoot[0] == nameof(ThrowingRootEffect));
        H.Check("RenderErrorNames_RootFuncEffect_Reported",
            effectRootFunc.Count == 1 && effectRootFunc[0] == nameof(FuncElement));

        var effectControl = new ReactorHostControl();
        effectControl.Mount(new ThrowingRootEffect());
        var effectFuncControl = new ReactorHostControl();
        effectFuncControl.Mount(ctx =>
        {
            ctx.UseEffect(() => throw new RenderErrorProbeException("control root func effect"), Array.Empty<object>());
            return TextBlock("control root func effect rendered");
        });
        H.SetContent(new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Children = { effectControl, effectFuncControl },
        });
        await Harness.Render(200);
        var effectControls = Take();
        Console.WriteLine("# host control root effect RenderError: " + string.Join(", ", effectControls));
        H.Check("RenderErrorNames_HostControl_RootEffect_NamesTheComponent",
            effectControls.Count(n => n == nameof(ThrowingRootEffect)) == 1);
        H.Check("RenderErrorNames_HostControl_RootFuncEffect_Reported",
            effectControls.Count(n => n == nameof(FuncElement)) == 1);

        effectControl.Dispose();
        effectFuncControl.Dispose();
        H.SetContent(null);

        // ── A root that mounts a replacement, then throws ────────────────
        // The failure belongs to the component that threw, not to the root the host holds
        // by the time the catch runs.
        var swapHost = H.CreateHost();
        swapHost.Mount(new RemountThenThrowRoot(() => swapHost.Mount(new ReplacementRoot())));
        await Harness.Render();
        var swapRender = Take();
        var swapEffectHost = H.CreateHost();
        swapEffectHost.Mount(new RemountThenThrowEffectRoot(() => swapEffectHost.Mount(new ReplacementRoot())));
        await Harness.Render();
        var swapEffect = Take();
        Console.WriteLine("# root replaced then threw RenderError: " + string.Join(", ", swapRender)
            + " | " + string.Join(", ", swapEffect));
        H.Check("RenderErrorNames_RootReplacedInRender_NamesTheThrower",
            swapRender.Count == 1 && swapRender[0] == nameof(RemountThenThrowRoot));
        H.Check("RenderErrorNames_RootReplacedInEffect_NamesTheThrower",
            swapEffect.Count == 1 && swapEffect[0] == nameof(RemountThenThrowEffectRoot));

        var swapControl = new ReactorHostControl();
        swapControl.Mount(new RemountThenThrowRoot(() => swapControl.Mount(new ReplacementRoot())));
        var swapEffectControl = new ReactorHostControl();
        swapEffectControl.Mount(new RemountThenThrowEffectRoot(() => swapEffectControl.Mount(new ReplacementRoot())));
        H.SetContent(new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Children = { swapControl, swapEffectControl },
        });
        await Harness.Render(200);
        var swapControls = Take();
        Console.WriteLine("# host control root replaced then threw RenderError: " + string.Join(", ", swapControls));
        H.Check("RenderErrorNames_HostControl_RootReplacedInRender_NamesTheThrower",
            swapControls.Count(n => n == nameof(RemountThenThrowRoot)) == 1);
        H.Check("RenderErrorNames_HostControl_RootReplacedInEffect_NamesTheThrower",
            swapControls.Count(n => n == nameof(RemountThenThrowEffectRoot)) == 1
            && !swapControls.Contains(nameof(ReplacementRoot)));

        swapControl.Dispose();
        swapEffectControl.Dispose();
        H.SetContent(null);
    }

    private const string OomTag = "!oom";
}

// Mounts a replacement root from its own Render(), then throws.
internal sealed class RemountThenThrowRoot(Action remount) : Component
{
    public override Element Render()
    {
        remount();
        throw new RenderErrorProbeException("replaced then threw");
    }
}

// Mounts a replacement root from its (mount-only) effect, then throws.
internal sealed class RemountThenThrowEffectRoot(Action remount) : Component
{
    public override Element Render()
    {
        UseEffect(() => { remount(); throw new RenderErrorProbeException("replaced in effect then threw"); },
            Array.Empty<object>());
        return TextBlock("remount effect root rendered");
    }
}

internal sealed class ReplacementRoot : Component
{
    public override Element Render() => TextBlock("replacement root");
}

internal sealed class ThrowingRootEffect : Component
{
    public override Element Render()
    {
        UseEffect(() => throw new RenderErrorProbeException("root effect"), Array.Empty<object>());
        return TextBlock("root effect rendered");
    }
}

/// <summary>
/// An <c>ErrorBoundary</c> that catches a child's failed first render rolls the child back
/// (issue #1291), unmounting the half-built component. That <c>ComponentUnmount</c> names the
/// component exactly as a normal unmount does (<c>Foo&lt;Int32&gt;</c>, not <c>Foo`1</c>).
/// </summary>
internal class RenderErrorNames_BoundaryRollbackUnmountNamesComponent(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var unmounts = new List<string>();
        using var subscription = ReactorTrace.Subscribe(
            e =>
            {
                if (e.EventName == nameof(ReactorEventSource.ComponentUnmount)
                    && e.Payload[0] is string name && name.StartsWith("RollbackThrower", StringComparison.Ordinal))
                    lock (unmounts) unmounts.Add(name);
            },
            EventLevel.Informational,
            ReactorEventSource.Keywords.Lifecycle);

        if (!ReactorEventSource.Log.IsEnabled(EventLevel.Informational, ReactorEventSource.Keywords.Lifecycle))
        {
            H.Skip("RenderErrorNames_BoundaryRollback", "EventSource disabled (NativeAOT)");
            return;
        }

        var host = H.CreateHost();
        host.Mount(_ => ErrorBoundary(Component<RollbackThrower<int>>(), _ => TextBlock("rollback fallback")));
        await Harness.Render();

        List<string> seen;
        lock (unmounts) seen = unmounts.ToList();
        Console.WriteLine("# rollback ComponentUnmount: " + string.Join(", ", seen));
        H.Check("RenderErrorNames_BoundaryRollback_FallbackShown", H.FindText("rollback fallback") is not null);
        H.Check("RenderErrorNames_BoundaryRollback_UnmountNamesGenericComponent",
            seen.Count == 1 && seen[0] == "RollbackThrower<Int32>");
    }
}

internal sealed class RollbackThrower<T> : Component
{
    public override Element Render() => throw new RenderErrorProbeException("rollback");
}

internal sealed class RenderErrorProbeException(string message) : Exception(message);

internal sealed record DerivedFuncElement(Func<RenderContext, Element> Render) : FuncElement(Render);

internal sealed record DerivedMemoElement(Func<RenderContext, Element> Render) : MemoElement(Render, new object?[] { "stable" });

/// <summary>
/// <c>RenderError</c> and an app's <c>RenderErrorHandler</c> (issue #1291) see the same
/// failures. The event is emitted before the handler runs, so it fires once, naming the
/// component, whatever the handler does: replace the fallback (in-tree and at the root),
/// or call <c>Propagate()</c>. An exception the app <em>declined</em> keeps going out
/// through every Reactor frame. The throw site already reported it, so a component frame it
/// crosses on the way — here one inside an <c>ErrorBoundary</c>, whose report-and-rethrow arm
/// would otherwise take it — must not report it again.
/// </summary>
internal class RenderErrorNames_RenderErrorHandlerOutcomesReportOnce(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var names = new List<string>();
        using var subscription = ReactorTrace.Subscribe(
            e =>
            {
                if (e.EventName == nameof(ReactorEventSource.RenderError)
                    && e.Payload[1] as string == nameof(RenderErrorProbeException))
                    lock (names) names.Add((string)e.Payload[0]!);
            },
            EventLevel.Error,
            ReactorEventSource.Keywords.Errors);

        if (!ReactorEventSource.Log.IsEnabled(EventLevel.Error, ReactorEventSource.Keywords.Errors))
        {
            H.Skip("RenderErrorNames_Handler", "EventSource disabled (NativeAOT)");
            return;
        }

        List<string> Take()
        {
            lock (names)
            {
                var copy = names.ToList();
                names.Clear();
                return copy;
            }
        }

        var previousUnhandled = ReactorApplication.OnUnhandledException;
        try
        {
            // ── App fallback replaces a child's render ───────────────────
            var appChild = H.CreateHost();
            appChild.RenderErrorHandler = _ => TextBlock("app child fallback");
            appChild.Mount(_ => VStack(Component<ThrowOnMountCounter, int>(0)));
            await Harness.Render();
            var child = Take();
            Console.WriteLine("# app fallback (child) RenderError: " + string.Join(", ", child));
            H.Check("RenderErrorNames_Handler_AppFallbackChild_ReportedOnceByName",
                H.FindText("app child fallback") is not null
                && child.Count == 1 && child[0] == nameof(ThrowOnMountCounter));

            // ── App fallback replaces the root render ────────────────────
            var appRoot = H.CreateHost();
            appRoot.RenderErrorHandler = _ => TextBlock("app root fallback");
            appRoot.Mount(new ThrowingRoot());
            await Harness.Render();
            var root = Take();
            Console.WriteLine("# app fallback (root) RenderError: " + string.Join(", ", root));
            H.Check("RenderErrorNames_Handler_AppFallbackRoot_ReportedOnceByName",
                H.FindText("app root fallback") is not null
                && root.Count == 1 && root[0] == nameof(ThrowingRoot));

            // ── Propagate(), handled by the app ──────────────────────────
            var handled = new List<Exception>();
            ReactorApplication.OnUnhandledException = ex => { handled.Add(ex); return true; };
            var propagating = H.CreateHost();
            propagating.RenderErrorHandler = e => { e.Propagate(); return null; };
            propagating.Mount(_ => VStack(Component<ThrowOnMountCounter, int>(0)));
            await Harness.Render();
            var propagated = Take();
            Console.WriteLine("# propagate (handled) RenderError: " + string.Join(", ", propagated));
            H.Check("RenderErrorNames_Handler_PropagateHandled_ReportedOnceByName",
                handled.Count == 1
                && propagated.Count == 1 && propagated[0] == nameof(ThrowOnMountCounter));

            // ── Propagate(), declined: escapes through an ErrorBoundary ──
            // The nested host's root render reports the throw (FuncElement), then the app
            // declines it and it escapes the nested Mount — out through the outer component's
            // Render(), which sits inside a boundary.
            ReactorApplication.OnUnhandledException = _ => false;
            var nestedWindow = new Microsoft.UI.Xaml.Window { Title = "RenderErrorNames declined propagation" };
            nestedWindow.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(300, 200));
            nestedWindow.Activate();
            var created = new List<ReactorHost>();
            Exception? escaped = null;
            try
            {
                H.CreateHost().Mount(_ => ErrorBoundary(
                    Component<NestedDecliningComponent, NestedDecliningProps>(new NestedDecliningProps(nestedWindow, created)),
                    TextBlock("declined boundary fallback")));
            }
            catch (RenderErrorProbeException ex) { escaped = ex; }
            await Harness.Render();
            var declined = Take();
            Console.WriteLine("# propagate (declined, through boundary) RenderError: " + string.Join(", ", declined));
            H.Check("RenderErrorNames_Handler_PropagateDeclined_ReportedOnceAtThrowSite",
                escaped is not null
                && declined.Count == 1 && declined[0] == nameof(FuncElement));

            foreach (var nested in created)
                nested.Dispose();
            nestedWindow.Close();
        }
        finally
        {
            ReactorApplication.OnUnhandledException = previousUnhandled;
        }
    }
}

internal sealed record NestedDecliningProps(Microsoft.UI.Xaml.Window Window, List<ReactorHost> Created);

// Starts nested Reactor work during its own render: a host whose first (inline) render
// fails and propagates.
internal sealed class NestedDecliningComponent : Component<NestedDecliningProps>
{
    public override Element Render()
    {
        var nested = new ReactorHost(Props.Window) { RenderErrorHandler = e => { e.Propagate(); return null; } };
        Props.Created.Add(nested);
        nested.Mount(_ => throw new RenderErrorProbeException("nested declined"));
        return TextBlock("unreachable");
    }
}

/// <summary>
/// An error an <c>ErrorBoundary</c> catches and recovers from must still reach a
/// <c>RenderError</c> listener — the user sees the fallback, so an inspector must too — and
/// must name the descendant that threw (the boundary cannot know it). Exactly one event per
/// throw: the component reports at the throw site and the boundary does not report again.
/// Covers the mount path (first render inside the boundary — class, function and memo
/// components, plus a throw that passes through an intermediate component) and the update
/// path (a component inside the boundary that starts throwing on re-render). The boundary
/// recovers from every exception, so an explicitly thrown <c>OutOfMemoryException</c> and an
/// exception whose <c>Message</c> getter throws must be reported too.
/// </summary>
internal class RenderErrorNames_ErrorBoundaryCatchIsReported(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        var names = new List<string>();
        using var subscription = ReactorTrace.Subscribe(
            e =>
            {
                if (e.EventName == nameof(ReactorEventSource.RenderError)
                    && e.Payload[1] as string is nameof(RenderErrorProbeException) or nameof(MessageThrowsProbeException)
                        or nameof(OutOfMemoryException))
                {
                    // Tag OOM reports so they don't disturb the per-name counts of the other probes.
                    var name = (string)e.Payload[0]!;
                    if (e.Payload[1] as string == nameof(OutOfMemoryException)) name += OomTag;
                    lock (names) names.Add(name);
                }
            },
            EventLevel.Error,
            ReactorEventSource.Keywords.Errors);

        if (!ReactorEventSource.Log.IsEnabled(EventLevel.Error, ReactorEventSource.Keywords.Errors))
        {
            H.Skip("RenderErrorNames_Boundary_Mount", "EventSource disabled (NativeAOT)");
            return;
        }

        List<string> Take()
        {
            lock (names)
            {
                var copy = names.ToList();
                names.Clear();
                return copy;
            }
        }

        var host = H.CreateHost();
        host.Mount(ctx =>
        {
            var (n, setN) = ctx.UseState(0);
            return VStack(4,
                ErrorBoundary(Component<ThrowOnMountCounter, int>(n), _ => TextBlock("mount fallback")),
                ErrorBoundary(Component<ThrowOnUpdateCounter, int>(n), _ => TextBlock("update fallback")),
                ErrorBoundary(RenderEachTime(_ => throw new RenderErrorProbeException("func")),
                    _ => TextBlock("func fallback")),
                ErrorBoundary(Memo(_ => throw new RenderErrorProbeException("memo"), "stable"),
                    _ => TextBlock("memo fallback")),
                // The throw passes through an intermediate component on its way to the
                // boundary; only the component that threw may report it.
                ErrorBoundary(Component<NestedThrowWrapper>(), _ => TextBlock("nested fallback")),
                // Reporting must not read the exception's Message: an override that throws
                // would replace the original exception on the rethrow path.
                ErrorBoundary(Component<ThrowsBadMessage>(), _ => TextBlock("bad message fallback")),
                // A boundary recovers from every exception, so every one must be reported —
                // including an explicitly thrown OutOfMemoryException.
                ErrorBoundary(Component<ThrowsOutOfMemory>(), _ => TextBlock("oom fallback")),
                ErrorBoundary(RenderEachTime(_ => throw new OutOfMemoryException("func oom")),
                    _ => TextBlock("func oom fallback")),
                ErrorBoundary(Memo(_ => throw new OutOfMemoryException("memo oom"), "stable"),
                    _ => TextBlock("memo oom fallback")),
                ErrorBoundary(Component<ThrowsOutOfMemoryOnUpdate, int>(n), _ => TextBlock("update oom fallback")),
                Button("bump", () => setN(n + 1)));
        });
        await Harness.Render();

        var mount = Take();
        Console.WriteLine("# boundary mount RenderError: " + string.Join(", ", mount));
        // Positive control: the boundary really caught it (fallback on screen, no raw crash).
        H.Check("RenderErrorNames_Boundary_Mount_FallbackShown", H.FindText("mount fallback") is not null);
        H.Check("RenderErrorNames_Boundary_Mount_ReportedOnceByName",
            mount.Count(n => n == nameof(ThrowOnMountCounter)) == 1);
        H.Check("RenderErrorNames_Boundary_Func_ReportedOnce",
            H.FindText("func fallback") is not null && mount.Count(n => n == nameof(FuncElement)) == 1);
        H.Check("RenderErrorNames_Boundary_Memo_ReportedOnce",
            H.FindText("memo fallback") is not null && mount.Count(n => n == nameof(MemoElement)) == 1);
        H.Check("RenderErrorNames_Boundary_Nested_ReportedOnceByThrower",
            H.FindText("nested fallback") is not null
            && mount.Count(n => n == nameof(NestedThrowInner)) == 1
            && !mount.Contains(nameof(NestedThrowWrapper)));
        H.Check("RenderErrorNames_Boundary_ThrowingMessage_ReportedOnce",
            H.FindText("bad message fallback") is not null
            && mount.Count(n => n == nameof(ThrowsBadMessage)) == 1);
        H.Check("RenderErrorNames_Boundary_OutOfMemory_ReportedOnce",
            H.FindText("oom fallback") is not null
            && mount.Count(n => n == nameof(ThrowsOutOfMemory) + OomTag) == 1);
        H.Check("RenderErrorNames_Boundary_FuncOutOfMemory_ReportedOnce",
            H.FindText("func oom fallback") is not null
            && mount.Count(n => n == nameof(FuncElement) + OomTag) == 1);
        H.Check("RenderErrorNames_Boundary_MemoOutOfMemory_ReportedOnce",
            H.FindText("memo oom fallback") is not null
            && mount.Count(n => n == nameof(MemoElement) + OomTag) == 1);

        H.ClickButton("bump");
        await Harness.Render();

        var update = Take();
        Console.WriteLine("# boundary update RenderError: " + string.Join(", ", update));
        H.Check("RenderErrorNames_Boundary_Update_FallbackShown", H.FindText("update fallback") is not null);
        H.Check("RenderErrorNames_Boundary_Update_ReportedOnceByName",
            update.Count(n => n == nameof(ThrowOnUpdateCounter)) == 1);
        H.Check("RenderErrorNames_Boundary_UpdateOutOfMemory_ReportedOnce",
            H.FindText("update oom fallback") is not null
            && update.Count(n => n == nameof(ThrowsOutOfMemoryOnUpdate) + OomTag) == 1);
        H.Check("RenderErrorNames_Boundary_NoElementTypeName",
            !mount.Concat(update).Any(n => n.StartsWith("ComponentElement", StringComparison.Ordinal)));
    }

    private const string OomTag = "!oom";
}

internal sealed class ThrowOnMountCounter : Component<int>
{
    public override Element Render() => throw new RenderErrorProbeException("mount");
}

internal sealed class ThrowOnUpdateCounter : Component<int>
{
    public override Element Render()
        => Props == 0 ? TextBlock("fine") : throw new RenderErrorProbeException("update");
}

internal sealed class ThrowingRoot : Component
{
    public override Element Render() => throw new RenderErrorProbeException("root");
}

internal sealed class NestedThrowWrapper : Component
{
    public override Element Render() => VStack(TextBlock("wrapper"), Component<NestedThrowInner>());
}

internal sealed class NestedThrowInner : Component
{
    public override Element Render() => throw new RenderErrorProbeException("nested");
}

internal sealed class MessageThrowsProbeException : Exception
{
    public override string Message => throw new InvalidOperationException("Message getter threw");
}

internal sealed class ThrowsBadMessage : Component
{
    public override Element Render() => throw new MessageThrowsProbeException();
}

internal sealed class ThrowsOutOfMemory : Component
{
    public override Element Render() => throw new OutOfMemoryException("probe");
}

internal sealed class ThrowsOutOfMemoryOnUpdate : Component<int>
{
    public override Element Render()
        => Props == 0 ? TextBlock("fine") : throw new OutOfMemoryException("update probe");
}
