// id: use-callback
// intent: memoize a callback so child props stay stable across unrelated parent renders
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Stable callback identity helps memoized children skip unnecessary updates.
ReactorApp.Run<App>("UseCallback", width: 400, height: 200);

class App : Component
{
    record ChildProps(Action OnPress);

    public override Element Render()
    {
        var (parentRenders, setParentRenders) = UseState(0);
        var (clicks, setClicks) = UseState(0);
        var increment = UseCallback(() => setClicks(clicks + 1), clicks);

        return VStack(12,
            Heading($"Clicks: {clicks}"),
            Button("Re-render parent", () => setParentRenders(parentRenders + 1)),
            Caption($"Unrelated parent renders: {parentRenders}"),
            Component<ChildButton, ChildProps>(new(increment)));
    }

    class ChildButton : Component<ChildProps>
    {
        int _renders;

        public override Element Render()
        {
            _renders++;
            return VStack(8,
                Caption($"Child renders: {_renders}"),
                Button("Increment from child", Props.OnPress));
        }
    }
}

