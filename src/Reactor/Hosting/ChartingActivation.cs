namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// One-line indirection used by chart elements / D3 primitives to tell the
/// active <see cref="ReactorHost"/> that charting is in use. The host then
/// lazy-initializes <c>AccessibilitySettings</c> + <c>UISettings</c> and
/// pushes their values into <c>Charting.D3Charts</c>'s thread-statics.
///
/// Apps with no charts never call this, never load the WinRT a11y settings,
/// and never trigger the <c>D3Charts</c> static cctor cascade — saving
/// 15–30 ms on cold start.
/// </summary>
internal static class ChartingActivation
{
    public static void RequestActivation()
    {
        ReactorApp.ActiveHost?.EnsureChartingActive();
    }
}
