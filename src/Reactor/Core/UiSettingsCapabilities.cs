namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Runtime capability probes for <c>Windows.UI.ViewManagement.UISettings</c>.
/// </summary>
/// <remarks>
/// Reactor compiles against the <c>10.0.22621</c> Windows SDK projection but declares
/// <c>TargetPlatformMinVersion 10.0.17763.0</c>, so the projection exposes members that
/// are not present on every supported OS. Members added after 17763 must be probed
/// before use — the projected call compiles, then fails its <c>QueryInterface</c> at
/// runtime on an older build.
/// </remarks>
internal static class UiSettingsCapabilities
{
    private static readonly bool s_hasAnimationsEnabledChanged = ProbeAnimationsEnabledChanged();

    /// <summary>
    /// True when this OS exposes <c>UISettings.AnimationsEnabledChanged</c>, which was
    /// added in Windows 10 version 2004 (build 19041).
    /// </summary>
    /// <remarks>
    /// The <c>SupportedOSPlatformGuard</c> annotation lets the platform-compatibility
    /// analyzer (CA1416) treat a check of this property as a version guard, so call sites
    /// need no suppression. The promise holds because <c>ApiInformation.IsEventPresent</c>
    /// is strictly narrower than a build-number comparison: it reports what the running OS
    /// actually projects.
    /// </remarks>
    [global::System.Runtime.Versioning.SupportedOSPlatformGuard("windows10.0.19041.0")]
    internal static bool HasAnimationsEnabledChanged => s_hasAnimationsEnabledChanged;

    private static bool ProbeAnimationsEnabledChanged()
    {
        try
        {
            return global::Windows.Foundation.Metadata.ApiInformation.IsEventPresent(
                "Windows.UI.ViewManagement.UISettings",
                "AnimationsEnabledChanged");
        }
        catch
        {
            // A capability probe has one safe answer when it cannot determine the truth:
            // "absent". Callers then take the ColorValuesChanged-only path, which is what
            // they did before this probe existed.
            //
            // Deliberately not narrowed to specific exception types. Measured: this call
            // does not throw in the headless xUnit host — IsEventPresent returns true there
            // — so the conditions that would throw are not ones this repo can enumerate or
            // test, and an incomplete list fails in the worst available direction. The probe
            // runs from a static field initializer, so an escaping exception surfaces as a
            // TypeInitializationException at whatever unrelated site first touches this
            // type. Same reasoning as ReactorHost.InitChartingState.
            return false;
        }
    }
}
