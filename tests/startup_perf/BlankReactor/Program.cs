// BlankReactor — startup-only baseline for Reactor.
//
// Mirrors microsoft-ui-xaml-lift/.../FrameworkBenchmarkBlankApps/WinUI3:
//   wWinMainEntry  → fired before any framework initialization
//   XamlAppLoaded  → fired on first Component Render() entry
//   WindowLoaded   → fired in first post-commit UseEffect
//   FirstRender    → fired on the first CompositionTarget.Rendering after commit
//   FirstIdle      → fired from a DispatcherQueue Low-priority callback after FirstRender
//   ProcessStop    → fired on app exit
//
// The shared BenchmarkTracing (EventSource) emits on the same provider GUID
// (FD80D616-E92B-4B2B-9BED-131ADA36A8FD) and event names that -lift uses, so
// the same WPR profile (Common/Tracing.wprp) and the same Regions XML
// resolve our traces and -lift traces identically.

using BenchmarkCommon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

const string AppName = "blank_reactor";

// Stamp before anything else.  Top-level statements compile to Main(), so
// this is the first managed code that runs in the process.
BlankApp.Metrics.RecordAppStart();

BenchmarkTracing.Log.SetAppName(AppName);
BenchmarkTracing.Log.TraceWinMainEntry();

try
{
    // 1000x1000 to match BlankRNW + BlankWinUI3. Window surface area
    // affects layout / first-paint cost, so all three variants must use
    // the same size for cross-stack comparison to be fair.
    ReactorApp.Run<BlankApp>(title: "BlankReactor", width: 1000, height: 1000);
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
        // First render means Reactor has built the tree and is about to commit.
        // Roughly equivalent to -lift's WinUI3 App::OnLaunched.
        var firedXamlAppLoaded = UseRef(false);
        if (!firedXamlAppLoaded.Current)
        {
            firedXamlAppLoaded.Current = true;
            BenchmarkTracing.Log.TraceXamlAppLoaded();
        }

        // After the first commit, hook CompositionTarget.Rendering (one-shot)
        // for FirstRender, then schedule a Low-priority dispatcher callback
        // for FirstIdle.  Mirrors -lift's MainWindow::InitializeComponent:
        // RootGrid().Loaded → CompositionTarget::Rendered → Low-pri callback.
        UseEffect(() =>
        {
            BenchmarkTracing.Log.TraceWindowLoaded();

            EventHandler<object>? handler = null;
            handler = (_, _) =>
            {
                if (Metrics.IsFirstFrameRecorded) return;

                Metrics.RecordFirstFrame(); // emits FirstRender ETW
                CompositionTarget.Rendering -= handler;

                // FirstIdle — Normal-priority work drains before Low fires, so
                // this is a fair "interactive" marker (matches -lift's
                // DispatcherQueuePriority.Low).
                DispatcherQueue.GetForCurrentThread().TryEnqueue(
                    DispatcherQueuePriority.Low,
                    () =>
                    {
                        if (Metrics.IsFinalized) return;
                        Metrics.RecordInteractive(); // emits FirstIdle ETW
                    });
            };
            CompositionTarget.Rendering += handler;

            return () => { /* one-shot — handler unsubscribes itself */ };
        }, Array.Empty<object>());

        return TextBlock("Blank Reactor — see ETW trace for timings")
            .FontSize(14)
            .Padding(12);
    }
}
