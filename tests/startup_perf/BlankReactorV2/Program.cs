// BlankReactorV2 — spec 047 Phase 0 startup-perf skeleton.
//
// Verbatim copy of BlankReactor at Phase-0 freeze. Only the AppName changes
// so the trace consumer can distinguish V2 numbers from Today numbers.
// Phase 1+ V2 changes show up as the TTFF delta against BlankReactor.

using BenchmarkCommon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

const string AppName = "blank_reactor_v2";

BlankApp.Metrics.RecordAppStart();

BenchmarkTracing.Log.SetAppName(AppName);
BenchmarkTracing.Log.TraceWinMainEntry();

try
{
    ReactorApp.Run<BlankApp>(title: "BlankReactorV2", width: 1000, height: 1000);
}
finally
{
    BenchmarkTracing.Log.TraceProcessStop();
}

// ---------------------------------------------------------------------------

internal sealed class BlankApp : Component
{
    public static readonly BlankPerfMetrics Metrics = new();

    public override Element Render()
    {
        var firedXamlAppLoaded = UseRef(false);
        if (!firedXamlAppLoaded.Current)
        {
            firedXamlAppLoaded.Current = true;
            BenchmarkTracing.Log.TraceXamlAppLoaded();
        }

        UseEffect(() =>
        {
            BenchmarkTracing.Log.TraceWindowLoaded();

            EventHandler<object>? handler = null;
            handler = (_, _) =>
            {
                if (Metrics.IsFirstFrameRecorded) return;

                Metrics.RecordFirstFrame();
                CompositionTarget.Rendering -= handler;

                DispatcherQueue.GetForCurrentThread().TryEnqueue(
                    DispatcherQueuePriority.Low,
                    () =>
                    {
                        if (Metrics.IsFinalized) return;
                        Metrics.RecordInteractive();
                    });
            };
            CompositionTarget.Rendering += handler;

            return () => { /* one-shot — handler unsubscribes itself */ };
        }, Array.Empty<object>());

        return TextBlock("Blank Reactor V2 — see ETW trace for timings")
            .FontSize(14)
            .Padding(12);
    }
}
