using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class OptionalEchoStrandRegressionFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            try
            {
                await Run(ControlledOptionalSelectionFamilyFixture.ComboBoxScenario());
            await Run(ControlledOptionalSelectionFamilyFixture.ListBoxScenario());
            await Run(ControlledOptionalSelectionFamilyFixture.ListViewScenario());
            await Run(ControlledOptionalSelectionFamilyFixture.GridViewScenario());
            await Run(ControlledOptionalSelectionFamilyFixture.FlipViewScenario());
            await Run(ControlledOptionalSelectionFamilyFixture.TemplatedFlipViewScenario());
            await Run(ControlledOptionalSelectionFamilyFixture.RadioButtonsScenario());
            await Run(ControlledOptionalSelectionFamilyFixture.PivotScenario());
            await Run(ControlledOptionalSelectionFamilyFixture.TabViewScenario());
            await Run(ControlledOptionalSelectionFamilyFixture.SelectorBarScenario());
            await Run(ControlledOptionalSelectionFamilyFixture.PipsPagerScenario());

            await Run(ControlledOptionalToggleFamilyFixture.CheckBoxScenario());
            await Run(ControlledOptionalToggleFamilyFixture.ToggleSwitchScenario());
            await Run(ControlledOptionalToggleFamilyFixture.RadioButtonScenario());
            await Run(ControlledOptionalToggleFamilyFixture.ToggleSplitButtonScenario());
            await Run(ControlledOptionalToggleFamilyFixture.ExpanderScenario());

            await Run(ControlledOptionalNumericFamilyFixture.SliderScenario());
            await Run(ControlledOptionalNumericFamilyFixture.NumberBoxScenario());
            await Run(ControlledOptionalNumericFamilyFixture.RatingControlScenario());
            await Run(ControlledOptionalNumericFamilyFixture.ColorPickerScenario());

            await Run(ControlledOptionalDateTimeFamilyFixture.CalendarDatePickerScenario());
            await Run(ControlledOptionalDateTimeFamilyFixture.DatePickerScenario());
            await Run(ControlledOptionalDateTimeFamilyFixture.TimePickerScenario());

            await Run(ControlledOptionalTextInputFamilyFixture.TextBoxScenario());
            await Run(ControlledOptionalTextInputFamilyFixture.PasswordBoxScenario());
            await Run(ControlledOptionalTextInputFamilyFixture.RichEditBoxScenario());
            await Run(ControlledOptionalTextInputFamilyFixture.AutoSuggestBoxScenario());
            }
            finally
            {
                // #539 isolation, generalized: drain the AutoSuggestBox popup /
                // template-part machinery before the next fixture renders, to
                // avoid the 0xC0000409 (STATUS_STACK_BUFFER_OVERRUN) stress
                // crash documented in #539.
                H.SetContent(null);
                await Harness.Render();
            }
        }

        private Task Run<TControl, TValue>(ControlledOptionalSelfTestHelpers.Scenario<TControl, TValue> scenario)
            where TControl : DependencyObject =>
            ControlledOptionalSelfTestHelpers.RunEchoNoStrandAsync(H, "OptionalEchoStrandRegression", scenario);
    }
}
