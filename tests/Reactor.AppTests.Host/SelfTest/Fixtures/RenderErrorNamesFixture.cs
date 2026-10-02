using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// <c>ReactorEventSource.RenderError</c> names the component that threw — not its element
/// record (<c>ComponentElement`1</c>) — on every path that replaces a render with the
/// error fallback: a child's first render (mount), a child's re-render (update), and a
/// host's root component. Before the fix the mount and root paths emitted nothing at all,
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
                Button("bump", () => setN(n + 1)));
        });
        await Harness.Render();

        var mount = Take();
        Console.WriteLine("# mount RenderError: " + string.Join(", ", mount));
        H.Check("RenderErrorNames_Mount_NamesTheComponent", mount.Contains(nameof(ThrowOnMountCounter)));
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
        H.Check("RenderErrorNames_Root_NamesTheComponent", root.Contains(nameof(ThrowingRoot)));
    }
}

internal sealed class RenderErrorProbeException(string message) : Exception(message);

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
