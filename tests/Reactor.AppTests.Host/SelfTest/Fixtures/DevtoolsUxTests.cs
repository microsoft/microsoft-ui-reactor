using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
// Disambiguate against Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase.
using MenuFlyoutItemBase = Microsoft.UI.Reactor.Core.MenuFlyoutItemBase;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selftest coverage for the spec 028 in-app devtools UX:
///   - UseDevtools() reflects ReactorApp.DevtoolsEnabled
///   - DevtoolsMenu(...) renders Empty when disabled (no trigger in visual tree)
///     and a lightning-bolt Button when enabled
///   - Observable&lt;T&gt; change notifications trigger re-renders on subscribers
/// </summary>
internal static class DevtoolsUxTests
{
    // ── DevtoolsMenu renders nothing when DevtoolsEnabled is false ──
    internal class MenuHiddenWhenDisabled(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ReactorApp.ResetDevtoolsEnabledForTests();
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx => VStack(
                    TextBlock("anchor").AutomationId("anchor"),
                    DevtoolsMenu(
                        () => new MenuFlyoutItemBase[] { MenuItem("should-not-render") },
                        automationId: "DevtoolsTrigger")
                ));

                await Harness.Render();

                // Anchor confirms the subtree mounted; the lightning trigger must not exist.
                var anchor = H.FindControl<TextBlock>(tb => tb.Text == "anchor");
                H.Check("DevtoolsUx_DisabledAnchorMounted", anchor is not null);

                var trigger = H.FindControl<Button>(b =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "DevtoolsTrigger");
                H.Check("DevtoolsUx_DisabledNoTrigger", trigger is null);
            }
            finally
            {
                ReactorApp.ResetDevtoolsEnabledForTests();
            }
        }
    }

    // ── DevtoolsMenu renders the trigger Button when DevtoolsEnabled is true ──
    internal class MenuVisibleWhenEnabled(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ReactorApp.ResetDevtoolsEnabledForTests();
            try
            {
                ReactorApp.DevtoolsEnabled = true;

                var host = H.CreateHost();
                host.Mount(ctx => VStack(
                    DevtoolsMenu(
                        () => new MenuFlyoutItemBase[] { MenuItem("noop") },
                        automationId: "DevtoolsTrigger")
                ));

                await Harness.Render();

                var trigger = H.FindControl<Button>(b =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "DevtoolsTrigger");
                H.Check("DevtoolsUx_EnabledTriggerPresent", trigger is not null);
                H.Check("DevtoolsUx_EnabledTriggerGlyph", trigger?.Content is string s && s == "⚡");
                // Flyout is wired onto the Button via Reconciler.Mount.SetFlyoutOnControl —
                // the recent fix swapped SetAttachedFlyout for this path so clicks open.
                H.Check("DevtoolsUx_EnabledFlyoutWired", trigger?.Flyout is not null);
            }
            finally
            {
                ReactorApp.ResetDevtoolsEnabledForTests();
            }
        }
    }

    // ── Flipping an Observable<bool> re-renders subscribers via UseObservable ──
    internal class ObservableToggleRerendersSubscribers(Harness h) : SelfTestFixtureBase(h)
    {
        private static readonly Observable<bool> DebugUI = new(false);

        private sealed class FlagSubscriber : Component
        {
            public override Element Render()
            {
                var on = UseObservable(DebugUI).Value;
                return VStack(
                    TextBlock(on ? "debug-on" : "debug-off").AutomationId("flagText")
                );
            }
        }

        public override async Task RunAsync()
        {
            DebugUI.Value = false;

            var host = H.CreateHost();
            host.Mount(ctx => Component<FlagSubscriber>());

            await Harness.Render();

            var initial = H.FindControl<TextBlock>(tb =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(tb) == "flagText");
            H.Check("DevtoolsUx_ObservableInitialOff",
                initial?.Text == "debug-off");

            // Mutate from "anywhere" — just like a Dev menu toggle would.
            DebugUI.Value = true;

            await Harness.Render();

            var afterOn = H.FindControl<TextBlock>(tb =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(tb) == "flagText");
            H.Check("DevtoolsUx_ObservableToggledOn",
                afterOn?.Text == "debug-on");

            DebugUI.Value = false;

            await Harness.Render();

            var afterOff = H.FindControl<TextBlock>(tb =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(tb) == "flagText");
            H.Check("DevtoolsUx_ObservableToggledOff",
                afterOff?.Text == "debug-off");
        }
    }
}
