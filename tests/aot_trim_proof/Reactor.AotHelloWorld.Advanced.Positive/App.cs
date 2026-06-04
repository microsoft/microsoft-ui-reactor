using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;

// Spec 053 §10 — positive trim probe. Calling Win2DCanvas(...) must root the
// Win2DCanvasHandler symbol (and, under the per-library trim model, the other
// Win2D handlers too, since they share the Advanced.Factories static cctor),
// proving the negative probe is not vacuous.

ReactorApp.Run<App>("Reactor.AotHelloWorld.Advanced.Positive", width: 480, height: 240);

internal sealed class App : Component
{
    public override Element Render()
    {
        var (clicks, setClicks) = UseState(0);

        return VStack(
            TextBlock($"Hello, Reactor.Advanced! Clicks: {clicks}"),
            Button("Click", () => setClicks(clicks + 1)),
            Win2DCanvas(static (CanvasDrawingSession _, CanvasDrawEventArgs _) => { })
        );
    }
}
