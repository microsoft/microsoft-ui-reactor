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
// Mirrors the BlankWPF / BlankWinForms samples in
// C:\wui\Samples\FrameworkBenchmarkBlankApps so this app emits the same
// "BenchmarkSyntheticApps" ETW provider regions (WinMainEntry, WindowLoaded,
// FirstRender, FirstIdle, ProcessStop) that the K2 perf-gates harness
// (AppLifeCycleWorkload) and WPA analyzers key off of.
//
//   Provider Name : BenchmarkSyntheticApps
//   Provider GUID : FD80D616-E92B-4B2B-9BED-131ADA36A8FD
//   Keyword       : MICROSOFT_KEYWORD_MEASURES (bit 46)
//
// Lifecycle mapping (Reactor ↔ WPF):
//   wWinMainEntry  → before ReactorApp.Run          (≈ WPF App.Main entry)
//   WindowLoaded   → Window.Activated (first fire)  (≈ WPF Window.Loaded)
//   FirstRender    → CompositionTarget.Rendered     (≈ WPF Window.ContentRendered)
//                    first fire after activation (post-paint, matches -lift)
//   FirstIdle      → DispatcherQueuePriority.Low    (≈ WPF DispatcherPriority.
//                    enqueue after FirstRender         ApplicationIdle)
//   ProcessStop    → after ReactorApp.Run returns   (≈ WPF App.OnExit)
BenchmarkTracing.Log.SetAppName("blank_reactor");
BenchmarkTracing.Log.TraceWinMainEntry();
GettingStartedApp.Metrics.RecordAppStart();

try
{
    ReactorApp.Run<GettingStartedApp>(
        title: "BlankReactor",
        width: 600,
        height: 400,
        devtools: false,
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
                // "first frame on screen". Matches -lift's WinUI3 baseline
                // (RootGrid.Loaded → CompositionTarget::Rendered). Capture the
                // first fire then unhook to avoid per-frame noise.
                EventHandler<RenderedEventArgs>? onRendered = null;
                onRendered = (s, e) =>
                {
                    CompositionTarget.Rendered -= onRendered;
                    GettingStartedApp.Metrics.RecordFirstFrame();

                    // FirstIdle / RTI: schedule on the UI dispatcher at Low
                    // priority. This fires after all higher-priority work the
                    // first paint kicked off has drained — equivalent to WPF's
                    // DispatcherPriority.ApplicationIdle.
                    var dq = DispatcherQueue.GetForCurrentThread();
                    dq.TryEnqueue(DispatcherQueuePriority.Low, () =>
                    {
                        GettingStartedApp.Metrics.RecordInteractive();
                        GettingStartedApp.NotifyMetricsReady();
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

class GettingStartedApp : Component
{
    // Shared metrics + a one-shot listener so the component can re-render with
    // the final values once FirstIdle fires. Matches the on-screen
    // "First Frame: X ms | Interactive: Y ms" status bar in the BlankWPF /
    // BlankWinForms samples.
    internal static readonly BlankPerfMetrics Metrics = new();
    private static event Action? MetricsReady;
    internal static void NotifyMetricsReady() => MetricsReady?.Invoke();

    public override Element Render()
    {
        var (name, setName) = UseState("World");
        var (metricsSummary, setMetricsSummary) = UseState<string?>(null);

        // Subscribe once: when FirstIdle fires, snapshot the finalized metrics
        // into local state so Reactor re-renders the status line.
        UseEffect(() =>
        {
            Action handler = () =>
            {
                if (Metrics.IsFinalized)
                    setMetricsSummary(Metrics.Summary);
            };
            MetricsReady += handler;
            // If FirstIdle already fired before the first render finished,
            // surface the value on the next render rather than waiting forever.
            if (Metrics.IsFinalized)
                setMetricsSummary(Metrics.Summary);
            return () => MetricsReady -= handler;
        });

        return VStack(16,
            TextBlock($"Hello, {name}!").FontSize(24).Bold(),
            TextBox(name, setName, placeholderText: "Enter your name").Width(250),
            TextBlock(metricsSummary ?? "Measuring…").FontSize(12)
        ).Padding(24);
    }
}
