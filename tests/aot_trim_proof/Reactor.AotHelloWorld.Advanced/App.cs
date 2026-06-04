using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Spec 053 §10 — negative trim probe. This app references Reactor.Advanced,
// but deliberately never names any Win2D canvas factory. The companion trim
// assertion scans the NativeAOT publish output and requires all Reactor.Advanced
// Win2D handler symbols to be absent.

ReactorApp.Run<App>("Reactor.AotHelloWorld.Advanced", width: 480, height: 240);

internal sealed class App : Component
{
    public override Element Render()
    {
        var (clicks, setClicks) = UseState(0);

        return VStack(
            TextBlock($"Hello, Reactor.Advanced! Clicks: {clicks}"),
            Button("Click", () => setClicks(clicks + 1))
        );
    }
}
