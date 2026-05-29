// id: calendardatepicker
// intent: date selection via calendar popup
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("CalendarDatePicker", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var (date, setDate) = UseState<DateTimeOffset?>(DateTimeOffset.Now);
        return VStack(12,
            Heading("CalendarDatePicker"),
            (CalendarDatePicker(date, setDate) with { Header = "Start date", DateFormat = "{month.full} {day.integer}, {year.full}" }),
            TextBlock($"Selected: {(date is null ? "None" : date.Value.ToString("D"))}"))
            .Margin(16);
    }
}
