// Recipe: multi-select calendar with selected-date summary.
//
// Pattern: CalendarView in `Multiple` selection mode (set via `with`),
// state holds an IReadOnlyList<DateTimeOffset>, the .SelectedDatesChanged
// fluent fires with the new list on every selection edit. Pass the list
// back through .SelectedDates for two-way binding so programmatic clears
// re-apply.

// In this clone, run `mur pack-local` once. Bump the version below to match
// whatever `mur pack-local` printed (default: 0.0.0-local). For a real NuGet
// consumer, set Version to a published Microsoft.UI.Reactor release.
#:package Microsoft.UI.Reactor@0.0.0-local
#:package Microsoft.WindowsAppSDK@2.0.1
#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;  // CalendarViewSelectionMode
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Calendar multi-select", width: 520, height: 600);

class App : Component
{
    public override Element Render()
    {
        var (dates, setDates) = UseState<IReadOnlyList<DateTimeOffset>>(
            Array.Empty<DateTimeOffset>());

        return VStack(16,
            Subtitle("Pick travel days"),

            (CalendarView() with { SelectionMode = CalendarViewSelectionMode.Multiple })
                .SelectedDates(dates)
                .SelectedDatesChanged(setDates),

            Body(dates.Count == 0
                ? "No dates selected."
                : $"{dates.Count} selected: " +
                  string.Join(", ", dates.Select(d => d.ToString("MMM d")))),

            Button("Clear", () => setDates(Array.Empty<DateTimeOffset>()))
                .SubtleButton()
        ).Padding(24);
    }
}
