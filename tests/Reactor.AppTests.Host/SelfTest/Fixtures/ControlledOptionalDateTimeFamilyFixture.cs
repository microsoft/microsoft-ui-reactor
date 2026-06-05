using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ControlledOptionalDateTimeFamilyFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Run(CalendarDatePickerScenario());
            await Run(DatePickerScenario());
            await Run(TimePickerScenario());
        }

        private async Task Run<TControl, TValue>(ControlledOptionalSelfTestHelpers.Scenario<TControl, TValue> scenario)
            where TControl : DependencyObject
        {
            const string fixture = "ControlledOptionalDateTimeFamily";
            await ControlledOptionalSelfTestHelpers.RunUnsetSurvivesSiblingRerenderAsync(H, fixture, scenario);
            await ControlledOptionalSelfTestHelpers.RunBoundUpdatesControlAsync(H, fixture, scenario);
            await ControlledOptionalSelfTestHelpers.RunSnapBackAsync(H, fixture, scenario);
        }
    }

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.CalendarDatePicker, DateTimeOffset?> CalendarDatePickerScenario() =>
        new(
            "CalendarDatePicker",
            (value, changed) => CalendarDatePicker(value, changed),
            h => h.FindControl<WinUI.CalendarDatePicker>(_ => true),
            c => c.Date,
            (c, v) => c.Date = v,
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 3, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero));

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.DatePicker, DateTimeOffset> DatePickerScenario() =>
        new(
            "DatePicker",
            (value, changed) => DatePicker(value, changed),
            h => h.FindControl<WinUI.DatePicker>(_ => true),
            c => c.Date,
            (c, v) => c.Date = v,
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 3, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero));

    internal static ControlledOptionalSelfTestHelpers.Scenario<WinUI.TimePicker, TimeSpan> TimePickerScenario() =>
        new(
            "TimePicker",
            (value, changed) => TimePicker(value, changed),
            h => h.FindControl<WinUI.TimePicker>(_ => true),
            c => c.Time,
            (c, v) => c.Time = v,
            TimeSpan.FromHours(5),
            TimeSpan.FromHours(7),
            TimeSpan.FromHours(3));
}
