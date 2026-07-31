using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.DateAndTime;

class CalendarDatePickerPage : Component
{
    public override Element Render()
    {
        var (date, setDate) = UseState<DateTimeOffset?>(DateTimeOffset.Now);
        var (clearableDate, setClearableDate) = UseState<DateTimeOffset?>(DateTimeOffset.Now);

        return ScrollView(
            VStack(16,
                PageHeader("CalendarDatePicker",
                    "A drop-down control that lets a user pick a date from a calendar."),

                SampleCard("Basic CalendarDatePicker",
                    VStack(8,
                        CalendarDatePicker(date, d => setDate(d)),
                        TextBlock($"Selected: {date?.ToString("d") ?? "(none)"}")
                            .Foreground(Theme.SecondaryText)
                    ),
                    @"CalendarDatePicker(date, d => setDate(d))"),

                SampleCard("CalendarDatePicker with Clear",
                    VStack(8,
                        CalendarDatePicker(clearableDate, d => setClearableDate(d)),
                        Button("Clear Date", () => setClearableDate(null)),
                        TextBlock($"Selected: {clearableDate?.ToString("D") ?? "No date selected"}")
                            .Foreground(Theme.SecondaryText)
                    ),
                    @"CalendarDatePicker(clearableDate, d => setClearableDate(d))
Button(""Clear"", () => setClearableDate(null))")
            ).Margin(36, 24, 36, 36)
        );
    }
}
