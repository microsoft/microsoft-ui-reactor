using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Fundamentals;

class UseEffectPage : Component
{
    public override Element Render()
    {
        var (ticks, bumpTicks) = UseReducer(0);
        var (mountNote, setMountNote) = UseState("");
        var (query, setQuery) = UseState("");
        var (runs, bumpRuns) = UseReducer(0);

        // Empty deps — starts once on mount, and the returned cleanup stops the timer on
        // unmount. Without the cleanup the timer would outlive the page.
        UseEffect(() =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => bumpTicks(t => t + 1);
            timer.Start();
            return () => timer.Stop();
        }, Array.Empty<object>());

        UseEffect(() => setMountNote($"Mounted at {DateTime.Now:HH:mm:ss}"), Array.Empty<object>());

        // One dependency — re-runs only when the query text actually changes.
        UseEffect(() => bumpRuns(r => r + 1), query);

        return ScrollView(VStack(16,
            PageHeader("UseEffect",
                "Side effects run after the render commits. Return an Action to clean up — it runs before the next execution and once more on unmount."),

            SampleCard("An effect that cleans up after itself",
                VStack(8,
                    TextBlock($"This page has been open for {ticks} second(s).")
                        .Foreground(Theme.SecondaryText),
                    Caption("Navigate away and the cleanup stops the timer; without it the tick would keep firing into an unmounted component.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (ticks, bumpTicks) = UseReducer(0);

UseEffect(() =>
{
    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    timer.Tick += (_, _) => bumpTicks(t => t + 1);
    timer.Start();
    return () => timer.Stop();
}, Array.Empty<object>());
"),

            SampleCard("Run once, on mount",
                TextBlock(mountNote).Foreground(Theme.SecondaryText),
                sourceCode: @"
var (mountNote, setMountNote) = UseState("""");

// Array.Empty<object>() means ""no dependencies"" — the effect runs exactly once.
UseEffect(() => setMountNote($""Mounted at {DateTime.Now:HH:mm:ss}""), Array.Empty<object>());
"),

            SampleCard("Re-run when a dependency changes",
                VStack(8,
                    TextBox(query, setQuery, placeholderText: "Type to change the dependency")
                        .AutomationName("Query")
                        .Width(280),
                    TextBlock($"Effect has run {runs} time(s).").Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (query, setQuery) = UseState("""");
var (runs, bumpRuns) = UseReducer(0);

UseEffect(() => bumpRuns(r => r + 1), query);

TextBox(query, setQuery, placeholderText: ""Type to change the dependency"")
"),

            SampleCard("Dependencies must be stable across renders",
                Caption("A freshly allocated object, array, or lambda is never equal to the previous one, so the effect never reaches its stable path. A tuple expression is value-equal at runtime but is still rejected by REACTOR_HOOKS_004.")
                    .Foreground(Theme.SecondaryText),
                sourceCode: @"
var (width, setWidth) = UseState(0.0);
var (height, setHeight) = UseState(0.0);

// A scalar key collapses several values into one stable dependency.
UseEffect(() => setMountNote($""{width} x {height}""), $""{width}|{height}"");

// Or pass them as separate dependencies.
UseEffect(() => setMountNote($""{width} x {height}""), width, height);
")
        ).Margin(36, 24, 36, 36));
    }
}
