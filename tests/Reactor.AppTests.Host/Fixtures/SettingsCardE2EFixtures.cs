using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// E2E fixture for spec 065 — SettingsCard and SettingsExpander under real input and a
/// cross-process UI Automation client.
///
/// <para>Selftests drive the cards through their automation peers in-process. This scene is
/// for what only real input proves: a pointer click and the Enter / Space keys reaching a
/// clickable card's <c>onClick</c> through <c>ButtonBase</c>, Tab focus landing on it, and a
/// real click on the expander header raising <c>onIsExpandedChanged</c>. The click count is
/// kept on a <see cref="Ref{T}"/> so a double dispatch for one input can't be hidden by state
/// batching.</para>
/// </summary>
internal static class SettingsCardE2EFixtures
{
    internal static Element Scene(RenderContext ctx) => Component<SceneComponent>();

    internal sealed class SceneComponent : Component
    {
        public override Element Render()
        {
            var (clicks, setClicks) = UseState(0);
            var (notify, setNotify) = UseState(true);
            var (expanded, setExpanded) = UseState(false);
            var count = UseRef(0);

            return VStack(4,
                Button("Before", () => { }).AutomationId("ScBefore"),
                SettingsCard("Storage",
                    description: "Free up space",
                    icon: FontIcon("\uEDA2"),
                    onClick: () =>
                    {
                        count.Current++;
                        setClicks(count.Current);
                    }).AutomationId("ScClickable"),
                SettingsCard("Notifications",
                    content: ToggleSwitch(notify, setNotify).AutomationId("ScToggle"))
                    .AutomationId("ScGroup"),
                SettingsExpander("Display",
                    description: "Brightness and scale",
                    isExpanded: expanded,
                    onIsExpandedChanged: setExpanded,
                    items:
                    [
                        SettingsCard("Brightness", content: Slider(50, 0, 100, _ => { }).AutomationId("ScSlider")),
                    ]).AutomationId("ScExpander"),
                TextBlock($"Clicks: {clicks}").AutomationId("ScClicks"),
                TextBlock($"Expanded: {expanded}").AutomationId("ScExpandedState")
            ).Width(560);
        }
    }
}
