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
            // Headless / unit-test host with no WinRT projection. Report the member as
            // absent so callers take the ColorValuesChanged-only path, which is what
            // they did before this probe existed.
            return false;
        }
    }
}
