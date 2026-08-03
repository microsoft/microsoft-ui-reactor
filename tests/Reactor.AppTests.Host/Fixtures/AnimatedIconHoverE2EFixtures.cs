using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls.AnimatedVisuals;
using static Microsoft.UI.Reactor.Factories;

// `using static Factories` shadows the WinUI type name with the factory method.
using XamlAnimatedIcon = Microsoft.UI.Xaml.Controls.AnimatedIcon;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// E2E host fixture for issue #983. The gallery page drives <c>AnimatedIcon.State</c> from the
/// pointer, and a user reports that a hover writes the state but plays no animation while a press
/// on the same icon does. Everything either side of the write has been eliminated at cheaper tiers
/// — the markers span a non-zero segment, the control is not remounted, the mount write lands, the
/// modifier chain carries the handlers, and the pointer enter/exit counts stay balanced — so what
/// is left needs real pointer input and a look at the rendered pixels, which is this tier.
/// </summary>
/// <remarks>
/// The reported state is exposed separately from the icon so a failure can say which half broke:
/// if <c>HoverState</c> never reaches <c>PointerOver</c> the input never arrived and any pixel
/// result is meaningless, and only once it has can "no animation" mean anything.
/// </remarks>
internal static class AnimatedIconHoverE2EFixtures
{
    internal class HoverTransitionComponent : Component
    {
        public override Element Render()
        {
            var source = UseMemo(() => new AnimatedSettingsVisualSource());
            var (hovering, setHovering) = UseState(false);
            var (pressing, setPressing) = UseState(false);
            var (enters, bumpEnters) = UseReducer(0);
            var state = pressing ? "Pressed" : hovering ? "PointerOver" : "Normal";

            return VStack(8,
                // A Button hosting the icon in a layout panel — the exact shape the gallery's
                // interactive cells use, so this test covers what ships. A Button is also the
                // target that UIA can find: a bare Border has no automation peer, so an
                // AutomationId on one is invisible to FindById.
                Button(HStack(AnimatedIcon(source).Size(48, 48)
                            .Set(icon => XamlAnimatedIcon.SetState(icon, state))),
                        () => { })
                    .Width(120).Height(120)
                    .AutomationId("HoverIconTarget")
                    .OnPointerEntered((_, _) => { setHovering(true); bumpEnters(n => n + 1); })
                    .OnPointerExited((_, _) => { setHovering(false); setPressing(false); })
                    .OnPointerPressed((_, _) => setPressing(true))
                    .OnPointerReleased((_, _) => setPressing(false)),

                TextBlock($"state={state}").AutomationId("HoverState"),
                TextBlock($"enters={enters}").AutomationId("HoverEnters"));
        }
    }

    internal static Element HoverTransitionTest(RenderContext ctx) =>
        Component<HoverTransitionComponent>();
}
