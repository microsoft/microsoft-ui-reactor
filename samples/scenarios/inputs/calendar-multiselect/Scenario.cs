// id: calendar-multiselect
// intent: select multiple dates in a calendar and summarize the chosen days
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Calendar Multi-select", width: 520, height: 420);

class App : Component
{
    public override Element Render()
    {
        var (dates, setDates) = UseState<IReadOnlyList<DateTimeOffset>>(Array.Empty<DateTimeOffset>());

        return VStack(16,
            Subtitle("Pick travel days"),
            (CalendarView() with { SelectionMode = CalendarViewSelectionMode.Multiple })
                .SelectedDates(dates)
                .SelectedDatesChanged(setDates),
            Body(dates.Count == 0
                ? "No dates selected."
                : $"{dates.Count} selected: {string.Join(", ", dates.Select(d => d.ToString("MMM d")))}"),
            Button("Clear", () => setDates(Array.Empty<DateTimeOffset>()))
                .SubtleButton())
            .Padding(24);
    }
}
