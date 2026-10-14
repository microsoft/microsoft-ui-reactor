namespace Microsoft.UI.Reactor.Charting;

/// <summary>
/// Charting-side activation entry point (issue #498). Called by chart elements
/// and D3 primitives the first time a chart enters the tree. On first call it
/// registers the Charting subsystem's host bridge and accessibility-scanner
/// extension with the core, then delegates to
/// <see cref="Hosting.ChartingActivation.RequestActivation"/> to lazily spin up
/// the host's WinRT accessibility settings.
/// <para>
/// Routing registration through this single method (instead of the core naming
/// the concrete Charting types) is what lets the trimmer drop the entire
/// charting accessibility chain from chart-free AOT builds.
/// </para>
/// </summary>
internal static class ChartingRuntime
{
    private static readonly Lock s_gate = new();
    private static volatile bool s_registered;

    internal static void Activate()
    {
        // Register the host bridge + scanner extension exactly once, and make the
        // `s_registered` flag observable only *after* registration completes. A
        // concurrent first-caller that loses the race blocks on the lock until
        // registration is done, so it can never reach RequestActivation() (and the
        // host's `s_chartingBridge?.CaptureForcedColorsTheme()` path) before the
        // bridge has been published. See issue #498 review (M1).
        if (!s_registered)
        {
            lock (s_gate)
            {
                if (!s_registered)
                {
                    Hosting.ReactorHost.RegisterChartingBridge(D3ChartsHostBridge.Instance);
                    Core.AccessibilityScanner.RegisterScanExtension(Accessibility.ChartAccessibilityChecker.Instance);
                    s_registered = true;
                }
            }
        }

        Hosting.ChartingActivation.RequestActivation();
    }
}
