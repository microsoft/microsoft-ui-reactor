using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Fixtures validating the Phase 2 trampoline-based event dispatch model
/// (spec 027 §Tier 2). The trampoline is attached once per element per event
/// and redirects to a mutable field, so per-render re-subscription is gone.
///
/// We can't directly observe WinUI subscription counts, so these fixtures rely on
/// behavioural invariants the trampoline guarantees: the latest closure wins,
/// handler=null silently no-ops, and state is stable across re-renders.
/// </summary>
internal static class TrampolineFixtures
{
    internal class LatestHandlerWinsAfterRerender(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int clickedVersion = -1;
            int version = 0;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (v, setV) = ctx.UseState(0);
                version = v;
                // Fresh closure each render — trampoline must redirect to this one.
                return VStack(
                    Button("target", () => clickedVersion = v)
                        .Set(b => b.Name = "target"),
                    Button("bump", () => setV(v + 1))
                        .Set(b => b.Name = "bump")
                );
            });
            await Harness.Render();

            // Bump state 5 times so the button handler closure changes 5 times.
            for (int i = 0; i < 5; i++)
            {
                H.ClickButton("bump");
                await Harness.Render();
            }

            H.Check("Trampoline_VersionAdvanced", version == 5);

            H.ClickButton("target");
            await Harness.Render();

            H.Check("Trampoline_ClickedLatest", clickedVersion == 5);
        }
    }

    internal class HandlerRemovedBecomesNoOp(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int doubleTaps = 0;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (armed, setArmed) = ctx.UseState(true);
                return VStack(
                    Button("toggle", () => setArmed(!armed)).Set(b => b.Name = "toggle"),
                    armed
                        ? TextBlock("dt-area").OnDoubleTapped((_, _) => doubleTaps++)
                        : TextBlock("dt-area")
                );
            });
            await Harness.Render();

            var tb = H.FindText("dt-area");
            H.Check("Trampoline_TextBlockMounted", tb is not null);
            H.Check("Trampoline_Armed_DoubleTapEnabled",
                tb is not null && tb.IsDoubleTapEnabled);

            // Disarm: handler becomes null — trampoline should stay attached but no-op.
            H.ClickButton("toggle");
            await Harness.Render();

            var tb2 = H.FindText("dt-area");
            H.Check("Trampoline_Disarmed_DoubleTapDisabled",
                tb2 is not null && !tb2.IsDoubleTapEnabled);

            // Rearm: handler non-null again — trampoline should resume without re-subscribe.
            H.ClickButton("toggle");
            await Harness.Render();

            var tb3 = H.FindText("dt-area");
            H.Check("Trampoline_Rearmed_DoubleTapEnabled",
                tb3 is not null && tb3.IsDoubleTapEnabled);
        }
    }

    internal class ReRenderSameControlUnderlyingRefStable(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Microsoft.UI.Xaml.Controls.Button? firstRef = null;
            Microsoft.UI.Xaml.Controls.Button? lastRef = null;
            int clicks = 0;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (n, setN) = ctx.UseState(0);
                return VStack(
                    Button("target", () => clicks++).Set(b => b.Name = "t"),
                    Button("bump", () => setN(n + 1)).Set(b => b.Name = "bump"),
                    TextBlock($"n={n}")
                );
            });
            await Harness.Render();

            firstRef = H.FindControl<Microsoft.UI.Xaml.Controls.Button>(b => b.Name == "t");

            for (int i = 0; i < 100; i++)
            {
                H.ClickButton("bump");
                await Harness.Render();
            }

            lastRef = H.FindControl<Microsoft.UI.Xaml.Controls.Button>(b => b.Name == "t");

            // Reactor reuses the same WinUI control across re-renders — that's exactly
            // what makes trampoline pooling valuable.
            H.Check("Trampoline_ControlRefStableAcrossRenders",
                firstRef is not null && ReferenceEquals(firstRef, lastRef));

            H.ClickButton("target");
            await Harness.Render();
            H.Check("Trampoline_HandlerFiresAfterManyRerenders", clicks == 1);
        }
    }
}
