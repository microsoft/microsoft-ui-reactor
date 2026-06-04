using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.UI;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ControlledOptionalNumericFamilyFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Run(SliderScenario());
            await Run(NumberBoxScenario());
            await Run(RatingControlScenario());
            await Run(ColorPickerScenario());
        }

        private async Task Run<TControl, TValue>(ControlledOptionalSelfTestHelpers.Scenario<TControl, TValue> scenario)
            where TControl : DependencyObject
        {
            const string fixture = "ControlledOptionalNumericFamily";
            await ControlledOptionalSelfTestHelpers.RunUnsetSurvivesSiblingRerenderAsync(H, fixture, scenario);
            await ControlledOptionalSelfTestHelpers.RunBoundUpdatesControlAsync(H, fixture, scenario);
            await ControlledOptionalSelfTestHelpers.RunSnapBackAsync(H, fixture, scenario);
        }
    }

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.Slider, double> SliderScenario() =>
        new(
            "Slider",
            (value, changed) => Slider(value, 0, 10, changed),
            h => h.FindControl<WinUI.Slider>(_ => true),
            c => c.Value,
            (c, v) => c.Value = v,
            5,
            7,
            3);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.NumberBox, double> NumberBoxScenario() =>
        new(
            "NumberBox",
            (value, changed) => NumberBox(value, changed) with { Minimum = 0, Maximum = 10 },
            h => h.FindControl<WinUI.NumberBox>(_ => true),
            c => c.Value,
            (c, v) => c.Value = v,
            5,
            7,
            3);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.RatingControl, double> RatingControlScenario() =>
        new(
            "RatingControl",
            (value, changed) => RatingControl(value, changed),
            h => h.FindControl<WinUI.RatingControl>(_ => true),
            c => c.Value,
            (c, v) =>
            {
                var peer = new RatingControlAutomationPeer(c);
                var provider = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue);
                provider.SetValue(v);
            },
            4,
            2,
            5);

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.ColorPicker, Color> ColorPickerScenario() =>
        new(
            "ColorPicker",
            (value, changed) => ColorPicker(value, changed),
            h => h.FindControl<WinUI.ColorPicker>(_ => true),
            c => c.Color,
            (c, v) => c.Color = v,
            Color.FromArgb(255, 10, 20, 30),
            Color.FromArgb(255, 80, 90, 100),
            Color.FromArgb(255, 140, 150, 160));
}
