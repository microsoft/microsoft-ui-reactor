// id: use-ref-mutable
// intent: store previous state in a mutable ref without causing re-renders
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Refs keep mutable values across renders without participating in diffing.
ReactorApp.Run<App>("UseRefMutable", width: 400, height: 200);

class App : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var previous = UseRef<int?>(null);

        UseEffect(() =>
        {
            previous.Current = count;
        }, count);

        return VStack(12,
            Heading($"Current: {count}"),
            TextBlock($"Previous: {(previous.Current is int value ? value : -1)}"),
            Button("+1", () => setCount(count + 1)));
    }
}

