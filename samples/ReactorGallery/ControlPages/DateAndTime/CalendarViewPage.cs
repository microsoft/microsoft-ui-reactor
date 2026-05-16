using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.DateAndTime;

class CalendarViewPage : Component
{
    public override Element Render()
    {
        var (selected, setSelected) = UseState<IReadOnlyList<DateTimeOffset>>(Array.Empty<DateTimeOffset>());

        return ScrollView(
            VStack(16,
                PageHeader("CalendarView",
                    "A calendar display that lets a user select a date."),

                SampleCard("Basic CalendarView",
                    CalendarView().Width(300).Height(350),
                    @"CalendarView()"),

                SampleCard("CalendarView in Card",
                    Border(
                        CalendarView().Width(300).Height(350)
                    ).Background(Theme.CardBackground)
                     .WithBorder(Theme.CardStroke)
                     .CornerRadius(8)
                     .Padding(8),
                    @"Border(
    CalendarView().Width(300).Height(350)
).Background(Theme.CardBackground)
 .WithBorder(Theme.CardStroke)
 .CornerRadius(8)
 .Padding(8)"),

                // Phase 8.1 — multi-select + .SelectedDatesChanged fluent (spec 039 §3.1).
                // The fluent is .SelectedDatesChanged; the underlying property is
                // OnSelectedDatesChanged. Fluents drop the leading "On" per Phase 1.
                SampleCard("Multi-select — .SelectedDatesChanged()",
                    VStack(8,
                        (CalendarView()
                            with { SelectionMode = CalendarViewSelectionMode.Multiple })
                            .SelectedDatesChanged(dates => setSelected(dates))
                            .Width(300).Height(350),
                        TextBlock($"{selected.Count} date(s) selected")
                            .Foreground(Theme.SecondaryText),
                        selected.Count > 0
                            ? (Element)Body(string.Join(", ", selected
                                .OrderBy(d => d)
                                .Select(d => d.ToString("MMM d"))))
                                .Foreground(Theme.SecondaryText)
                            : Empty()
                    ),
                    @"(CalendarView()
    with { SelectionMode = CalendarViewSelectionMode.Multiple })
    .SelectedDatesChanged(dates => setSelected(dates))
    .Width(300).Height(350)")
            ).Margin(36, 24, 36, 36)
        );
    }
}
