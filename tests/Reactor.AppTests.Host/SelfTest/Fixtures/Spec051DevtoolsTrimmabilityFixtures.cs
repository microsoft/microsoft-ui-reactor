using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using MenuFlyoutItemBase = Microsoft.UI.Reactor.Core.MenuFlyoutItemBase;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class Spec051DevtoolsTrimmabilityFixtures
{
    private const string SwitchName = "Reactor.DevtoolsSupport";

    private static void EnableDevtoolsAppSession()
    {
        AppContext.SetSwitch(SwitchName, true);
        ReactorApp.TryRunDevtoolsForTest(
            ["app.exe", "--devtools", "app"],
            title: "Spec051",
            width: 320,
            height: 240);
    }

    private sealed class UseDevtoolsProbe : Component
    {
        public override Element Render()
        {
            var enabled = Context.UseDevtools();
            return TextBlock(enabled ? "use-devtools:true" : "use-devtools:false")
                .AutomationId("Spec051UseDevtoolsValue");
        }
    }

    internal sealed class DevtoolsMenu_SwitchOff_RendersEmpty(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            AppContext.SetSwitch(SwitchName, false);
            ReactorApp.ResetDevtoolsEnabledForTests();
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx => VStack(
                    TextBlock("anchor").AutomationId("Spec051Anchor"),
                    DevtoolsMenu(
                        () => new MenuFlyoutItemBase[] { MenuItem("should-not-render") },
                        automationId: "Spec051DevtoolsTrigger")));

                await Harness.Render();

                H.Check("Spec051_MenuOff_AnchorMounted", H.FindText("anchor") is not null);
                var trigger = H.FindControl<Button>(b =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "Spec051DevtoolsTrigger");
                H.Check("Spec051_MenuOff_NoTrigger", trigger is null);
            }
            finally
            {
                ReactorApp.ResetDevtoolsEnabledForTests();
                AppContext.SetSwitch(SwitchName, false);
            }
        }
    }

    internal sealed class DevtoolsMenu_SwitchOn_RendersTrigger(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ReactorApp.ResetDevtoolsEnabledForTests();
            try
            {
                EnableDevtoolsAppSession();

                var host = H.CreateHost();
                host.Mount(ctx => DevtoolsMenu(
                    () => new MenuFlyoutItemBase[] { MenuItem("noop") },
                    automationId: "Spec051DevtoolsTrigger"));

                await Harness.Render();

                var trigger = H.FindControl<Button>(b =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "Spec051DevtoolsTrigger");
                H.Check("Spec051_MenuOn_TriggerPresent", trigger is not null);
                H.Check("Spec051_MenuOn_TriggerGlyph", trigger?.Content is string glyph && glyph == "⚡");
            }
            finally
            {
                ReactorApp.ResetDevtoolsEnabledForTests();
                AppContext.SetSwitch(SwitchName, false);
            }
        }
    }

    internal sealed class UseDevtools_SwitchOff_ReturnsFalse(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            AppContext.SetSwitch(SwitchName, false);
            ReactorApp.ResetDevtoolsEnabledForTests();
            try
            {
                var host = H.CreateHost();
                host.Mount(new UseDevtoolsProbe());

                await Harness.Render();

                H.Check("Spec051_UseDevtoolsOff_False", H.FindText("use-devtools:false") is not null);
            }
            finally
            {
                ReactorApp.ResetDevtoolsEnabledForTests();
                AppContext.SetSwitch(SwitchName, false);
            }
        }
    }

    internal sealed class UseDevtools_SwitchOn_PlusCli_ReturnsTrue(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ReactorApp.ResetDevtoolsEnabledForTests();
            try
            {
                EnableDevtoolsAppSession();

                var host = H.CreateHost();
                host.Mount(new UseDevtoolsProbe());

                await Harness.Render();

                H.Check("Spec051_UseDevtoolsOnCli_True", H.FindText("use-devtools:true") is not null);
            }
            finally
            {
                ReactorApp.ResetDevtoolsEnabledForTests();
                AppContext.SetSwitch(SwitchName, false);
            }
        }
    }
}
