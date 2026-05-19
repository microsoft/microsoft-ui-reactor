using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

internal static class DemoFixtures
{
    // ── Counter Demo ───────────────────────────────────────────────

    internal class CounterDemoComponent : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);

            return VStack(12,
                TextBlock($"Current count: {count}")
                    .FontSize(24)
                    .AutomationId("CountDisplay"),

                HStack(8,
                    Button("-1", () => setCount(count - 1)).AutomationId("DecrementBtn"),
                    Button("Reset", () => setCount(0))
                        .IsEnabled(!(count == 0))
                        .AutomationId("ResetBtn"),
                    Button("+1", () => setCount(count + 1)).AutomationId("IncrementBtn")
                ),

                // Conditional text based on count
                (count switch
                {
                    0 => TextBlock("Try clicking the buttons!").AutomationId("CountMessage"),
                    > 0 and < 10 => TextBlock("Going up...").AutomationId("CountMessage"),
                    >= 10 => TextBlock("That's a lot!").AutomationId("CountMessage"),
                    < 0 and > -10 => TextBlock("Going negative...").AutomationId("CountMessage"),
                    _ => TextBlock("Way down!").AutomationId("CountMessage"),
                })
            );
        }
    }

    internal static Element CounterDemo(RenderContext ctx) =>
        Component<CounterDemoComponent>();

    // ── Conditional Demo ───────────────────────────────────────────

    internal class ConditionalDemoComponent : Component
    {
        public override Element Render()
        {
            var (showAdvanced, setShowAdvanced) = UseState(false);

            return VStack(12,
                CheckBox(showAdvanced, v => setShowAdvanced(v), "Show advanced options")
                    .AutomationId("AdvancedCheckBox"),

                showAdvanced
                    ? VStack(8,
                        TextBlock("Advanced Settings").AutomationId("AdvancedTitle"),
                        TextBlock("Debug mode: ON").AutomationId("DebugMode"),
                        TextBlock("Verbose logging: ON").AutomationId("VerboseLogging")
                    ).AutomationId("AdvancedPanel")
                    : Empty()
            );
        }
    }

    internal static Element ConditionalDemo(RenderContext ctx) =>
        Component<ConditionalDemoComponent>();

    // ── Tab Navigation Demo ────────────────────────────────────────

    internal class TabNavigationComponent : Component
    {
        public override Element Render()
        {
            var (tab, setTab) = UseState("Home");

            return VStack(12,
                HStack(8,
                    Button("Home", () => setTab("Home"))
                        .IsEnabled(!(tab == "Home")).AutomationId("DemoHomeTab"),
                    Button("Settings", () => setTab("Settings"))
                        .IsEnabled(!(tab == "Settings")).AutomationId("DemoSettingsTab"),
                    Button("About", () => setTab("About"))
                        .IsEnabled(!(tab == "About")).AutomationId("DemoAboutTab")
                ),
                Border(
                    tab switch
                    {
                        "Home" => VStack(
                            TextBlock("Welcome Home").AutomationId("DemoHomeTitle"),
                            TextBlock("Home page content.").AutomationId("DemoHomeContent")),
                        "Settings" => VStack(
                            TextBlock("Settings Page").AutomationId("DemoSettingsTitle"),
                            TextBlock("Configure your preferences.").AutomationId("DemoSettingsContent")),
                        "About" => VStack(
                            TextBlock("About Page").AutomationId("DemoAboutTitle"),
                            TextBlock("Version 1.0").AutomationId("DemoAboutVersion")),
                        _ => Empty()
                    }
                ).Padding(16).AutomationId("DemoTabContent")
            );
        }
    }

    internal static Element TabNavigation(RenderContext ctx) =>
        Component<TabNavigationComponent>();
}
