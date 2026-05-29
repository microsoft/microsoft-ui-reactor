// id: slider-range
// intent: slider for numeric range selection
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Slider Range", width: 400, height: 300);

class App : Component
{
    public override Element Render()
    {
        var (volume, setVolume) = UseState(35.0);
        return VStack(12,
            Heading("Slider"),
            (Slider(volume, 0, 100, setVolume) with { Header = "Volume", StepFrequency = 5, TickFrequency = 10 }),
            TextBlock($"Current value: {volume:0}"))
            .Margin(16);
    }
}
