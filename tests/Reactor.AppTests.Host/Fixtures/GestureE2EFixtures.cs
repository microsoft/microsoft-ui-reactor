using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// E2E host fixtures for spec 027 Tier 3 gestures — pan, double-tap, right-tap,
/// long-press. WinAppDriver drives real user input against these fixtures; see
/// <c>tests/Reactor.AppTests/Tests/GestureTests.cs</c>.
/// </summary>
internal static class GestureE2EFixtures
{
    // ── Pan: reports cumulative translation via OnPan callbacks ──

    internal class PanTestComponent : Component
    {
        public override Element Render()
        {
            var (tx, setTx) = UseState(0.0);
            var (ty, setTy) = UseState(0.0);
            var (phase, setPhase) = UseState("idle");

            // Button has a default AutomationPeer so WinAppDriver's FindById can locate it.
            // Pan uses the ManipulationDelta pipeline, which is independent of Button.Click,
            // so attaching .OnPan to a Button works cleanly.
            return VStack(8,
                Button("Pan me", null)
                    .Width(220).Height(160)
                    .AutomationId("PanTarget")
                    .OnPan(
                        onChanged: g =>
                        {
                            setTx(g.Translation.X);
                            setTy(g.Translation.Y);
                            setPhase("changed");
                        },
                        onBegan: _ => setPhase("began"),
                        onEnded: _ => setPhase("ended"),
                        minimumDistance: 4.0),

                TextBlock($"tx={tx:F0} ty={ty:F0}").AutomationId("PanTranslation"),
                TextBlock($"phase={phase}").AutomationId("PanPhase")
            );
        }
    }

    internal static Element PanTest(RenderContext ctx) => Component<PanTestComponent>();

    // ── Double-tap on a Button (Button receives DoubleTapped when IsDoubleTapEnabled) ──

    internal class DoubleTapTestComponent : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);

            return VStack(8,
                Button("Double-tap me", null)
                    .Padding(20)
                    .OnDoubleTap(() => setCount(count + 1))
                    .AutomationId("DoubleTapTarget"),

                TextBlock($"Doubletap count: {count}").AutomationId("DoubleTapCount")
            );
        }
    }

    internal static Element DoubleTapTest(RenderContext ctx) => Component<DoubleTapTestComponent>();

    // ── Right-tap — Button reacts by incrementing a right-click counter ──

    internal class RightTapTestComponent : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);

            return VStack(8,
                Button("Right-tap me", null)
                    .Padding(20)
                    .OnRightTapped((_, e) =>
                    {
                        e.Handled = true;
                        setCount(count + 1);
                    })
                    .AutomationId("RightTapTarget"),

                TextBlock($"Righttap count: {count}").AutomationId("RightTapCount")
            );
        }
    }

    internal static Element RightTapTest(RenderContext ctx) => Component<RightTapTestComponent>();

    // ── Long-press — with mouse emulation on so WinAppDriver can drive it ──

    internal class LongPressTestComponent : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);

            return VStack(8,
                Button("Hold me", null)
                    .Padding(20)
                    .OnLongPress(() => setCount(count + 1),
                        minimumDuration: TimeSpan.FromMilliseconds(400),
                        enableMouseEmulation: true)
                    .AutomationId("LongPressTarget"),

                TextBlock($"Longpress count: {count}").AutomationId("LongPressCount")
            );
        }
    }

    internal static Element LongPressTest(RenderContext ctx) => Component<LongPressTestComponent>();
}
