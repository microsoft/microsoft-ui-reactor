using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Doc app for `cheat-sheet.md` — tiny vignettes showing the most common
// one-line patterns so the cheat sheet can reference real snippets.
ReactorApp.Run<CheatSheetApp>("Cheat Sheet", width: 480, height: 360
#if DEBUG
    , preview: true
#endif
);

class CheatSheetApp : Component
{
    public override Element Render() => Component<MiniApp>();
}

// <snippet:hello>
class HelloVignette : Component
{
    public override Element Render() =>
        VStack(8,
            Heading("Hello"),
            Button("Click", () => { })
        ).Padding(20);
}
// </snippet:hello>

// <snippet:state-vignette>
class StateVignette : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return Button($"clicked {count}×", () => setCount(count + 1));
    }
}
// </snippet:state-vignette>

// <snippet:effect-vignette>
class EffectVignette : Component
{
    public override Element Render()
    {
        var (tick, setTick) = UseState(0);
        UseEffect(() =>
        {
            var timer = new System.Timers.Timer(1000);
            timer.Elapsed += (_, _) => setTick(tick + 1);
            timer.Start();
            return () => timer.Dispose();
        });
        return TextBlock($"Tick: {tick}");
    }
}
// </snippet:effect-vignette>

class MiniApp : Component
{
    public override Element Render() => VStack(16,
        Component<HelloVignette>(),
        Component<StateVignette>(),
        Component<EffectVignette>()
    ).Padding(16);
}
