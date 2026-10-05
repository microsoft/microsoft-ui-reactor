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
/// <c>ReactorHostControl</c> (component and render function). Before the fix the mount
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
                    && e.Payload[1] as string == nameof(RenderErrorProbeException))
                    lock (names) names.Add((string)e.Payload[0]!);
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
    }
}

internal sealed class RenderErrorProbeException(string message) : Exception(message);

/// <summary>
/// An error an <c>ErrorBoundary</c> catches and recovers from must still reach a
/// <c>RenderError</c> listener — the user sees the fallback, so an inspector must too — and
/// must name the descendant that threw (the boundary cannot know it). Exactly one event per
/// throw: the component reports at the throw site and the boundary does not report again.
/// Covers the mount path (first render inside the boundary — class, function and memo
/// components, plus a throw that passes through an intermediate component) and the update
/// path (a component inside the boundary that starts throwing on re-render).
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
                    && e.Payload[1] as string == nameof(RenderErrorProbeException))
                    lock (names) names.Add((string)e.Payload[0]!);
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

        H.ClickButton("bump");
        await Harness.Render();

        var update = Take();
        Console.WriteLine("# boundary update RenderError: " + string.Join(", ", update));
        H.Check("RenderErrorNames_Boundary_Update_FallbackShown", H.FindText("update fallback") is not null);
        H.Check("RenderErrorNames_Boundary_Update_ReportedOnceByName",
            update.Count(n => n == nameof(ThrowOnUpdateCounter)) == 1);
        H.Check("RenderErrorNames_Boundary_NoElementTypeName",
            !mount.Concat(update).Any(n => n.StartsWith("ComponentElement", StringComparison.Ordinal)));
    }
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
