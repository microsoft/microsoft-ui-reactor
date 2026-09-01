using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// The <c>Assets\AppIcon.ico</c> convention — the asset name the official WinUI 3
/// packaged template ships, which Reactor treats as "the app's icon" when nothing
/// was declared explicitly.
/// </summary>
/// <remarks>
/// <para>Two consumers need this, and they need different halves of it:
/// <see cref="ReactorWindow"/> loads it as an <c>HICON</c> for the window caption,
/// while the <c>TitleBar</c> icon default needs the <em>path</em> so it can build a
/// XAML <c>IconSource</c>. Keeping the probe here means the two cannot disagree
/// about which file the convention names.</para>
/// <para><c>baseDirectory</c> is a parameter rather than a read of
/// <see cref="AppContext.BaseDirectory"/> so tests can point the probe at a
/// temporary directory. Writing a real <c>Assets\AppIcon.ico</c> into the test
/// host's own output directory would silently give every other windowing fixture a
/// window icon and destroy the zero control the icon-default fixture measures
/// against.</para>
/// </remarks>
internal static class AppIconConvention
{
    private static string? s_probeRootOverride;

    /// <summary>
    /// The directory the convention is probed under. <see cref="AppContext.BaseDirectory"/>
    /// in production; overridable for tests.
    /// </summary>
    /// <remarks>
    /// Deliberately shared by <em>both</em> consumers — <see cref="ReactorWindow"/>'s
    /// <c>HICON</c> load and the <c>TitleBar</c> projection. An override that moved only
    /// one of them would make the window and the title bar probe different directories,
    /// and any logic that compares their verdicts would then be reasoning about two
    /// unrelated files. Keeping one root is what lets the title bar trust
    /// <c>ReactorWindow.ConventionIconApplied</c>.
    /// </remarks>
    internal static string ProbeRoot => s_probeRootOverride ?? AppContext.BaseDirectory;

    internal static void SetProbeRootForTests(string? directory) => s_probeRootOverride = directory;

    /// <summary>Directory name holding the convention asset, relative to the app root.</summary>
    internal const string AssetDirectory = "Assets";

    /// <summary>File name of the convention asset.</summary>
    internal const string AssetFileName = "AppIcon.ico";

    /// <summary>
    /// Resolves <c>&lt;baseDirectory&gt;\Assets\AppIcon.ico</c> when that file exists.
    /// </summary>
    /// <returns>
    /// <c>false</c> when the asset is absent, or when the probe itself fails (a
    /// malformed base directory, or a locked-down filesystem). A missing convention
    /// asset is never fatal — callers fall through to their next source.
    /// </returns>
    internal static bool TryGetAssetPath(string baseDirectory, out string path)
    {
        path = string.Empty;
        try
        {
            var candidate = global::System.IO.Path.Join(baseDirectory, AssetDirectory, AssetFileName);
            if (!global::System.IO.File.Exists(candidate)) return false;
            path = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException
                                      or NotSupportedException
                                      or global::System.IO.IOException
                                      or UnauthorizedAccessException
                                      or global::System.Security.SecurityException)
        {
            DiagnosticLog.SwallowedError(LogCategory.Hosting, "AppIconConvention.TryGetAssetPath", ex);
            return false;
        }
    }
}
