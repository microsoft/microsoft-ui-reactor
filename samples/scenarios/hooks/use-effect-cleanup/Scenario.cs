// id: use-effect-cleanup
// intent: set up a timer in an effect and clean it up on unmount
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

// Cleanup returns the timer to a stopped, unsubscribed state when the component leaves.
ReactorApp.Run<App>("UseEffectCleanup", width: 400, height: 200);

class App : Component
{
    public override Element Render()
    {
        var (seconds, updateSeconds) = UseReducer(0);

        UseEffect(() =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            EventHandler<object> onTick = (_, _) => updateSeconds(value => value + 1);
            timer.Tick += onTick;
            timer.Start();

            return () =>
            {
                timer.Tick -= onTick;
                timer.Stop();
            };
        }, Array.Empty<object>());

        return VStack(12,
            Heading($"Elapsed: {seconds}s"),
            Caption("The cleanup lambda stops the timer when this scenario unmounts."));
    }
}

