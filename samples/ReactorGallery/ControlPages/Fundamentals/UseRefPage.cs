using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Fundamentals;

class UseRefPage : Component
{
    public override Element Render()
    {
        var silentRef = UseRef(0);
        var (shown, setShown) = UseState(0);

        var timerRef = UseRef<DispatcherTimer?>(null);
        var (elapsed, bumpElapsed) = UseReducer(0);
        var (running, setRunning) = UseState(false);

        // A ref is not disposed for you: the reconciler runs UseEffect cleanups on unmount,
        // but a handle parked in a Ref is invisible to it. Without this the timer would keep
        // ticking into an unmounted component after you navigate away mid-run.
        UseEffect(() => () =>
        {
            timerRef.Current?.Stop();
            timerRef.Current = null;
        }, Array.Empty<object>());

        // A ref is the right place for "have I already done this?" — writing it never
        // schedules a render, so the guard cannot itself cause the work to repeat.
        var greetedRef = UseRef(false);
        var (greeting, setGreeting) = UseState("");
        UseEffect(() =>
        {
            if (greetedRef.Current) return;
            greetedRef.Current = true;
            setGreeting("Greeted exactly once.");
        }, Array.Empty<object>());

        return ScrollView(VStack(16,
            PageHeader("UseRef",
                "A mutable box that survives re-renders and, unlike UseState, never triggers one. Read and write it through .Current."),

            SampleCard("Writing a ref does not re-render",
                VStack(8,
                    HStack(8,
                        Button("Count silently", () => silentRef.Current++),
                        Button("Publish the count", () => setShown(silentRef.Current))),
                    TextBlock($"Published: {shown}").Foreground(Theme.SecondaryText),
                    Caption("The first button changes the ref and nothing re-renders. The second copies it into state, which is what makes the new number appear.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var silentRef = UseRef(0);
var (shown, setShown) = UseState(0);

HStack(8,
    Button(""Count silently"", () => silentRef.Current++),
    Button(""Publish the count"", () => setShown(silentRef.Current)))
"),

            SampleCard("Holding a handle across renders",
                VStack(8,
                    HStack(8,
                        Button("Start", () =>
                        {
                            if (timerRef.Current is not null) return;
                            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                            timer.Tick += (_, _) => bumpElapsed(e => e + 1);
                            timer.Start();
                            timerRef.Current = timer;
                            setRunning(true);
                        }),
                        Button("Stop", () =>
                        {
                            timerRef.Current?.Stop();
                            timerRef.Current = null;
                            setRunning(false);
                        })),
                    TextBlock($"Elapsed: {elapsed}s — {(running ? "running" : "stopped")}")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var timerRef = UseRef<DispatcherTimer?>(null);

// A Ref is never disposed for you — pair it with an unmount cleanup.
UseEffect(() => () => { timerRef.Current?.Stop(); timerRef.Current = null; }, Array.Empty<object>());

Button(""Start"", () =>
{
    if (timerRef.Current is not null) return;
    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    timer.Tick += (_, _) => bumpElapsed(e => e + 1);
    timer.Start();
    timerRef.Current = timer;   // survives every later render
})
"),

            SampleCard("A one-time guard",
                VStack(8,
                    TextBlock(greeting).Foreground(Theme.SecondaryText),
                    Caption("Ref<T> exposes .Current, not .Value. Do not confuse it with ElementRef / UseElementRef<T>, which points at a realized WinUI element and is filled in by the reconciler rather than by you.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var greetedRef = UseRef(false);

UseEffect(() =>
{
    if (greetedRef.Current) return;
    greetedRef.Current = true;      // .Current is the property — NOT .Value
    setGreeting(""Greeted exactly once."");
}, Array.Empty<object>());
")
        ).Margin(36, 24, 36, 36));
    }
}
