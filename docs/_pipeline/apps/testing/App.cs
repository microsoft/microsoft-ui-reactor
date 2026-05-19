using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Tiny doc app for `testing.md` — renders the kind of UI the page's
// fixtures mount (a counter component) so the doc-app harness can capture
// one representative screenshot. The page itself uses the snippets below
// as the canonical Reactor-test shapes (component-under-test, effect-aware,
// accessibility-scanner target).
ReactorApp.Run<TestingApp>("Testing Demo", width: 360, height: 240
#if DEBUG
    , preview: true
#endif
);

// <snippet:counter-component>
class Counter : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(8,
            TextBlock($"Count: {count}").FontSize(20).Bold(),
            Button("Increment", () => setCount(count + 1))
        ).Padding(16);
    }
}
// </snippet:counter-component>

class TestingApp : Component
{
    public override Element Render() => Component<Counter>();
}

// <snippet:effectful>
// Effect-aware component used as a fixture target. UseEffect fires on the
// next flush, not during render — tests must wait for the flush before
// observing the side effect's log entry (see testing.md, "Async patterns").
class EffectfulCounter : Component
{
    public List<string> Log { get; } = new();

    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        UseEffect(() =>
        {
            Log.Add($"effect:{count}");
            return () => Log.Add($"cleanup:{count}");
        }, count);
        return Button($"count={count}", () => setCount(count + 1));
    }
}
// </snippet:effectful>

// <snippet:icon-only>
// AccessibilityScanner fixture target. The scanner walks the element tree
// post-render and returns one A11yDiagnostic per finding; an icon-only
// button without an accessible name is the canonical positive case.
class IconOnlyButton : Component
{
    public override Element Render() =>
        Button("", () => { });   // no accessible name → diagnostic
}
// </snippet:icon-only>
