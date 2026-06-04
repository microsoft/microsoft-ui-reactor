using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Hello World AOT", width: 480, height: 240);

internal sealed class App : Component
{
    public override Element Render() => TextBlock("Hello, world!");
}
