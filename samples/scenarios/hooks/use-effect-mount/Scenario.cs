// id: use-effect-mount
// intent: run a fire-once effect after the component mounts
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// An empty dependency array gives this effect mount-only semantics.
ReactorApp.Run<App>("UseEffectMount", width: 400, height: 200);

class App : Component
{
    public override Element Render()
    {
        var (status, setStatus) = UseState("Mounting...");

        UseEffect(() =>
        {
            setStatus("Loaded initial data on mount.");
        }, Array.Empty<object>());

        return VStack(12,
            Heading("Mount effect"),
            TextBlock(status),
            Caption("This effect runs once after the first render."));
    }
}

