using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.DateAndTime;

class DatePickerPage : Component
{
    public override Element Render()
    {
        var (date, setDate) = UseState(DateTimeOffset.Now);
        var (resetDate, setResetDate) = UseState(DateTimeOffset.Now);

        return ScrollView(
            VStack(16,
                PageHeader("DatePicker",
                    "A control that lets a user pick a date using spinners."),

                SampleCard("Basic DatePicker",
                    VStack(8,
                        DatePicker(date, d => setDate(d)),
                        TextBlock($"Selected: {date:d}").Foreground(Theme.SecondaryText)
                    ),
                    @"DatePicker(date, d => setDate(d))"),

                SampleCard("DatePicker with Reset",
                    VStack(8,
                        DatePicker(resetDate, d => setResetDate(d)),
                        HStack(8,
                            Button("Today", () => setResetDate(DateTimeOffset.Now)),
                            TextBlock($"Selected: {resetDate:D}").Foreground(Theme.SecondaryText)
                        )
                    ),
                    @"DatePicker(resetDate, d => setResetDate(d))
Button(""Today"", () => setResetDate(DateTimeOffset.Now))")
            ).Margin(36, 24, 36, 36)
        );
    }
}
