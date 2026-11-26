// <snippet:minimal-app>
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<MyApp>("My App");

class MyApp : Component
{
    public override Element Render()
    {
        return VStack(12,
            TextBlock("Hello, Reactor!").FontSize(24).Bold(),
            TextBlock("Your setup works.")
        ).Padding(24);
    }
}
// </snippet:minimal-app>

// Other ways to start the same app. Only one ReactorApp.Run call can run as a
// top-level statement, so these live in methods that nothing calls.
static class OtherStartups
{
    public static void WithWindowSize()
    {
        // <snippet:window-size>
        ReactorApp.Run<MyApp>("My App", width: 800, height: 600);
        // </snippet:window-size>
    }

    public static void WithRenderFunction()
    {
        // <snippet:function-root>
        ReactorApp.Run("Quick Test", ctx =>
        {
            var (count, setCount) = ctx.UseState(0);
            return Button($"Clicked {count} times", () => setCount(count + 1));
        });
        // </snippet:function-root>
    }
}
