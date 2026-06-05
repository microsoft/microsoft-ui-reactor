using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ControlledOptionalToggleFamilyFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Run(CheckBoxScenario());
            await Run(ToggleSwitchScenario());
            await Run(RadioButtonScenario());
            await Run(ToggleSplitButtonScenario());
            await Run(ExpanderScenario());
        }

        private async Task Run<TControl, TValue>(ControlledOptionalSelfTestHelpers.Scenario<TControl, TValue> scenario)
            where TControl : DependencyObject
        {
            const string fixture = "ControlledOptionalToggleFamily";
            await ControlledOptionalSelfTestHelpers.RunUnsetSurvivesSiblingRerenderAsync(H, fixture, scenario);
            await ControlledOptionalSelfTestHelpers.RunBoundUpdatesControlAsync(H, fixture, scenario);
            await ControlledOptionalSelfTestHelpers.RunSnapBackAsync(H, fixture, scenario);
        }
    }

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.CheckBox, bool?> CheckBoxScenario() =>
        new(
            "CheckBox",
            (value, changed) => ThreeStateCheckBox(value, changed, "check"),
            h => h.FindControl<WinUI.CheckBox>(c => c.Content as string == "check"),
            c => c.IsChecked,
            (c, v) => c.IsChecked = v,
            true,
            false,
            false);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.ToggleSwitch, bool> ToggleSwitchScenario() =>
        new(
            "ToggleSwitch",
            (value, changed) => ToggleSwitch(value, changed),
            h => h.FindControl<WinUI.ToggleSwitch>(_ => true),
            c => c.IsOn,
            (c, v) => c.IsOn = v,
            true,
            false,
            false);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.RadioButton, bool> RadioButtonScenario() =>
        new(
            "RadioButton",
            (value, changed) => RadioButton("radio", value, changed, "controlled-optional-toggle"),
            h => h.FindControl<WinUI.RadioButton>(c => c.Content as string == "radio"),
            c => c.IsChecked == true,
            (c, v) => c.IsChecked = v,
            true,
            false,
            false);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.ToggleSplitButton, bool> ToggleSplitButtonScenario() =>
        new(
            "ToggleSplitButton",
            (value, changed) => ToggleSplitButton("split", value, changed),
            h => h.FindControl<WinUI.ToggleSplitButton>(c => c.Content as string == "split"),
            c => c.IsChecked,
            (c, v) => c.IsChecked = v,
            true,
            false,
            false);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.Expander, bool> ExpanderScenario() =>
        new(
            "Expander",
            (value, changed) => Expander("expander", TextBlock("content"), value, changed),
            h => h.FindControl<WinUI.Expander>(_ => true),
            c => c.IsExpanded,
            (c, v) =>
            {
                var peer = new ExpanderAutomationPeer(c);
                var provider = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);
                if (v) provider.Expand(); else provider.Collapse();
            },
            true,
            false,
            false);
}
