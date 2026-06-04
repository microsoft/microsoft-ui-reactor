using System;
using BenchmarkCommon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

// BlankReactorMsix — MSIX-packaged startup-only baseline for Reactor.
//
// Mirrors the unpackaged BlankReactor sibling's milestones exactly so
// blank_reactor vs. blank_reactor_msix isolates package/deployment cost:
//   wWinMainEntry  → fired before any framework initialization
//   XamlAppLoaded  → fired on first Component Render() entry
//   WindowLoaded   → fired in first post-commit UseEffect
//   FirstRender    → fired on the first CompositionTarget.Rendering after commit
//   FirstIdle      → fired from a DispatcherQueue Low-priority callback after FirstRender
//   ProcessStop    → fired on app exit
//
// The UI is deliberately a single TextBlock — no TextBox, no on-screen
// metrics readout, no state hooks — to match the unpackaged BlankReactor
// sibling exactly. Any extra UI element would skew the cold-launch cost
// measurement that the perf-gate harness is designed to capture.
const string AppName = "blank_reactor_msix";

BlankApp.Metrics.RecordAppStart();
BenchmarkTracing.Log.SetAppName(AppName);
BenchmarkTracing.Log.TraceWinMainEntry();

try
{
    // 1000x1000 to match BlankReactor / BlankRNW / BlankWinUI3. Window
    // surface area affects layout / first-paint cost, so all four variants
    // must use the same size for cross-stack comparison to be fair.
    ReactorApp.Run<BlankApp>(title: "BlankReactor", width: 1000, height: 1000);
}
finally
{
    BenchmarkTracing.Log.TraceProcessStop();
}

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
        // for FirstIdle. Mirrors the unpackaged BlankReactor sibling.
        UseEffect(() =>
        {
            BenchmarkTracing.Log.TraceWindowLoaded();

            EventHandler<object>? handler = null;
            handler = (_, _) =>
            {
                if (Metrics.IsFirstFrameRecorded) return;

                Metrics.RecordFirstFrame(); // emits FirstRender ETW
                CompositionTarget.Rendering -= handler;

                DispatcherQueue.GetForCurrentThread().TryEnqueue(
                    DispatcherQueuePriority.Low,
                    () =>
                    {
                        if (Metrics.IsFinalized) return;
                        Metrics.RecordInteractive(); // emits FirstIdle ETW
                    });
            };
            CompositionTarget.Rendering += handler;

            return () => CompositionTarget.Rendering -= handler;
        }, Array.Empty<object>());

        // Single TextBlock only — see file header comment for why no other UI.
        // Mirrors the unpackaged BlankReactor sibling so the two variants
        // differ only in deployment shape (MSIX vs unpackaged), not in
        // measured user-code cost.
        return TextBlock("Blank Reactor (MSIX) — see ETW trace for timings")
            .FontSize(14)
            .Padding(12);
    }
}
