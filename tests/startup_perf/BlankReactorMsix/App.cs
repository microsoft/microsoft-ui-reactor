using System;
using BenchmarkCommon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

// ── Perf instrumentation ────────────────────────────────────────────────
// Synthetic blank app for measuring Reactor + WinUI 3 cold-launch cost
// via the MSIX-packaged deployment path. Emits the "BenchmarkSyntheticApps"
// ETW provider regions (WinMainEntry, WindowLoaded, FirstRender,
// FirstIdle, ProcessStop) so the same WPA regions resolve here as in the
// sibling BlankReactor / BlankWinUI3 / BlankRNW apps in the same
// tests/startup_perf/ directory, enabling apples-to-apples comparison.
//
//   Provider Name : BenchmarkSyntheticApps
//   Provider GUID : FD80D616-E92B-4B2B-9BED-131ADA36A8FD
//   Keyword       : MICROSOFT_KEYWORD_MEASURES (bit 46)
//
// Lifecycle mapping (Reactor ↔ WPF):
//   wWinMainEntry  → before ReactorApp.Run          (≈ WPF App.Main entry)
//   WindowLoaded   → Window.Activated (first fire)  (≈ WPF Window.Loaded)
//   FirstRender    → CompositionTarget.Rendered     (≈ WPF Window.ContentRendered)
//                    first fire after activation (post-paint)
//   FirstIdle      → DispatcherQueuePriority.Low    (≈ WPF DispatcherPriority.
//                    enqueue after FirstRender         ApplicationIdle)
//   ProcessStop    → after ReactorApp.Run returns   (≈ WPF App.OnExit)
//
// The UI is deliberately a single TextBlock — no TextBox, no on-screen
// metrics readout, no state hooks — to match the unpackaged BlankReactor
// sibling exactly. Any extra UI element would skew the cold-launch cost
// measurement that the perf-gate harness is designed to capture.
BenchmarkTracing.Log.SetAppName("blank_reactor");
BenchmarkTracing.Log.TraceWinMainEntry();
BlankApp.Metrics.RecordAppStart();

try
{
    // 1000x1000 to match BlankReactor / BlankRNW / BlankWinUI3. Window
    // surface area affects layout / first-paint cost, so all four variants
    // must use the same size for cross-stack comparison to be fair.
    ReactorApp.Run<BlankApp>(
        title: "BlankReactor",
        width: 1000,
        height: 1000,
        configure: host =>
        {
            // Hook before Activate() so the first WM_ACTIVATE arrives at our handler.
            // configure(host) runs after the Window is constructed but before
            // RegisterWindow/Activate (ReactorApp.cs line ~453).
            TypedEventHandler<object, WindowActivatedEventArgs>? onActivated = null;
            onActivated = (sender, args) =>
            {
                host.Window.Activated -= onActivated;
                BenchmarkTracing.Log.TraceWindowLoaded();

                // FirstRender: the first composition frame after activation.
                // CompositionTarget.Rendered fires after each frame has been
                // composed and presented (post-paint) — the right marker for
                // "first frame on screen". Capture the first fire then unhook
                // to avoid per-frame noise.
                EventHandler<RenderedEventArgs>? onRendered = null;
                onRendered = (s, e) =>
                {
                    CompositionTarget.Rendered -= onRendered;
                    BlankApp.Metrics.RecordFirstFrame();

                    // FirstIdle / RTI: schedule on the UI dispatcher at Low
                    // priority. This fires after all higher-priority work the
                    // first paint kicked off has drained — equivalent to WPF's
                    // DispatcherPriority.ApplicationIdle.
                    var dq = DispatcherQueue.GetForCurrentThread();
                    dq.TryEnqueue(DispatcherQueuePriority.Low, () =>
                    {
                        BlankApp.Metrics.RecordInteractive();
                    });
                };
                CompositionTarget.Rendered += onRendered;
            };
            host.Window.Activated += onActivated;
        });
}
finally
{
    BenchmarkTracing.Log.TraceProcessStop();
    BenchmarkTracing.Log.Dispose();
}

internal sealed class BlankApp : Component
{
    internal static readonly BlankPerfMetrics Metrics = new();

    public override Element Render()
    {
        // Single TextBlock only — see file header comment for why no other UI.
        // Mirrors the unpackaged BlankReactor sibling so the two variants
        // differ only in deployment shape (MSIX vs unpackaged), not in
        // measured user-code cost.
        return TextBlock("Blank Reactor (MSIX) — see ETW trace for timings")
            .FontSize(14)
            .Padding(12);
    }
}
