// id: use-state-list-pitfall
// intent: show why mutating List<T> in place is a UseState anti-pattern
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Mutating the same List<T> instance changes data but not state identity, so no re-render happens.
ReactorApp.Run<App>("UseStateListPitfall", width: 400, height: 200);

class App : Component
{
    public override Element Render()
    {
        var (items, _) = UseState(new List<string> { "Alpha" });
        var (rerenders, setRerenders) = UseState(0);

        return VStack(12,
            Heading($"Visible count: {items.Count}"),
            Caption($"Unrelated renders: {rerenders}"),
            Button("Mutate list in place (wrong)", () =>
            {
                // Wrong: UseState still holds the same List<T> reference, so Reactor
                // does not detect a new state value and the UI stays stale.
                items.Add($"Item {items.Count + 1}");
            }),
            Button("Force unrelated re-render", () => setRerenders(rerenders + 1)),
            ForEach(items, item => TextBlock(item)));
    }
}

