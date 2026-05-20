// id: use-state-basic
// intent: count clicks; demonstrate UseState with primitive value
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("UseStateBasic", width: 400, height: 200);

class App : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        return VStack(
            Heading($"Count: {count}"),
            Button("+1", () => setCount(count + 1)));
    }
}
