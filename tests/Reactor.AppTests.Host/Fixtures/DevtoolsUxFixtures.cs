using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
// Disambiguate against Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase in case
// a future file in this folder imports the WinUI namespace.
using MenuFlyoutItemBase = Microsoft.UI.Reactor.Core.MenuFlyoutItemBase;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// E2E fixtures for the spec 028 in-app devtools UX. Hosts a DevtoolsMenu with
/// a "Debug UI" ToggleMenuItem bound to an Observable&lt;bool&gt;; a TextBlock
/// mirrors the flag so Appium can read it through UIA.
/// </summary>
internal static class DevtoolsUxFixtures
{
    // Process-global flag shared across renders. Static so the E2E test can verify
    // the Dev menu toggle actually flows through to subscribers.
    internal static readonly Observable<bool> DebugUI = new(false);

    internal class DevtoolsUxTestComponent : Component
    {
        public override Element Render()
        {
            // One-shot reset on first render. Done synchronously *before* reading the
            // observable, not in a UseEffect, for two reasons:
            //   (1) Reactor re-renders from the root, so putting the reset in the
            //       factory above would clobber every toggle the moment its
            //       notification loops back around.
            //   (2) UseEffect runs *after* Render completes, so a reset there would
            //       not influence the already-captured `debugUI` value on the first
            //       mount — the initial UI would still reflect stale state from a
            //       prior test, and no further re-render would be triggered because
            //       the new value matches the subscribed one.
            var hasReset = UseRef(false);
            if (!hasReset.Current)
            {
                DebugUI.Value = false;
                hasReset.Current = true;
            }

            // Subscribe so the enclosing component re-renders when the flag flips,
            // which rebuilds the DevtoolsMenu flyout with fresh IsChecked state and
            // toggles the mirrored TextBlock below.
            var debugUI = UseObservable(DebugUI).Value;

            return VStack(8,
                HStack(8,
                    TextBlock("Devtools UX E2E").AutomationId("DevtoolsUxTitle"),
                    DevtoolsMenu(
                        () => new MenuFlyoutItemBase[]
                        {
                            ToggleMenuItem("Debug UI",
                                isChecked: debugUI,
                                onToggled: v => DebugUI.Value = v),
                        },
                        automationId: "DevtoolsTrigger")
                ),

                // Mirrored state — Appium reads this to confirm a menu toggle
                // actually changed the observable.
                TextBlock(debugUI ? "debug-on" : "debug-off")
                    .AutomationId("DebugUiState"),

                // Conditional dev-only UX. Only added to the tree when debugUI is
                // true; flipping the flag makes the element appear/disappear.
                debugUI
                    ? TextBlock("debug-overlay-visible").AutomationId("DebugOverlay")
                    : null
            );
        }
    }

    internal static Element DevtoolsUxTest(RenderContext ctx)
    {
        // Fixture-scoped side effect: turn on the in-app devtools UI for just this
        // fixture, so tests don't need to launch the host with `--devtools app`.
        // The AND gate is satisfied: DevtoolsEnabled is set here, and in production
        // would only be reached when both Run(devtools:true) AND `--devtools app`
        // CLI were present.
        ReactorApp.DevtoolsEnabled = true;
        return Component<DevtoolsUxTestComponent>();
    }
}
