using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Devtools;
using static Microsoft.UI.Reactor.Factories;

AppContext.SetSwitch("Reactor.DevtoolsSupport", true);
ReactorDevtools.EnsureRegistered();
ReactorApp.Run<App>("Hello World AOT Devtools On", width: 480, height: 240);

internal sealed class App : Component
{
    public override Element Render() => TextBlock("Hello, world!");
}
