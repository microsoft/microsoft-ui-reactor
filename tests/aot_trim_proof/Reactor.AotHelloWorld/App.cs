using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Spec 048 §11 / §13.2 — AOT/trim verification app.
//
// Reachability surface: this file (the only one in the assembly) calls
// `TextBlock` and `Button` from the Reactor factory facade. It calls
// nothing else from the catalog — no Marquee, no TreeView, no GridView,
// no TabView, no Image, no CalendarView, no NumberBox.
//
// Spec 048's lazy-registration shape (Patterns A and B) means the
// reachability graph from this `Render()` body must NOT pull
// MarqueeHandler / MarqueeControl / TreeView / GridView / TabViewHandler
// into the published binary. The companion assertion project
// (Reactor.AotHelloWorld.TrimAssertions) verifies that invariant
// empirically against the published exe + bundled assemblies.
//
// DO NOT add new factory calls to Render() unless you also update the
// allow-list in the trim-assertion project. A new factory here changes
// the reachability surface that the assertion is checking.

ReactorApp.Run<App>("Reactor.AotHelloWorld", width: 480, height: 240);

internal sealed class App : Component
{
    public override Element Render()
    {
        var (clicks, setClicks) = UseState(0);

        return VStack(
            TextBlock($"Hello, Reactor! Clicks: {clicks}"),
            Button("Click", () => setClicks(clicks + 1))
        );
    }
}

