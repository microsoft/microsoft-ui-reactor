using System.Diagnostics;
using Microsoft.UI.Windowing;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Abstraction over <see cref="AppWindow.SetIcon(string)"/> and
/// <see cref="AppWindow.SetIcon(IconId)"/>. Pass to <see cref="WindowSpec.Icon"/>,
/// to a tray icon, or to a taskbar overlay. (spec 036 §4.1)
/// </summary>
/// <remarks>
/// Two source kinds are supported: a filesystem path (<see cref="FromPath"/>) for
/// <c>.ico</c> resources alongside an unpackaged app, and an
/// <c>ms-appx:///</c>-style packaged-app resource URI (<see cref="FromResource"/>).
/// Empty strings are rejected at construction so a malformed icon never reaches
/// the WinUI APIs.
/// <para>Consumers differ in what they can accept, because most of them need a raw
/// Win32 <c>HICON</c>:</para>
/// <list type="bullet">
/// <item><description><see cref="WindowSpec.Icon"/> — accepts both kinds. The source is
/// handed to <c>AppWindow.SetIcon</c> verbatim, so the platform resolves
/// <c>ms-appx:</c> URIs (including MRT scale/target-size qualifiers)
/// itself.</description></item>
/// <item><description>Tray icons, taskbar overlays, and thumbnail-toolbar buttons —
/// <see cref="FromPath"/> only. They load through <c>LoadImageW</c>, which cannot read a
/// packaged resource URI.</description></item>
/// <item><description>Jump lists — <see cref="FromResource"/> on the packaged path
/// (the WinRT API takes the URI directly) and <see cref="FromPath"/> on the unpackaged
/// one. Each silently skips the other kind.</description></item>
/// </list>
/// </remarks>
public sealed class WindowIcon
{
    private readonly string _source;
    private readonly bool _isResource;

    private WindowIcon(string source, bool isResource)
    {
        _source = source;
        _isResource = isResource;
    }

    /// <summary>The path or resource URI this icon was constructed from.</summary>
    public string Source => _source;

    /// <summary>True when constructed via <see cref="FromResource"/>.</summary>
    public bool IsResource => _isResource;

    /// <summary>
    /// Create an icon from a filesystem path (typically a <c>.ico</c>) for an
    /// unpackaged app. Throws on null/empty input.
    /// <para>An unrecognised extension or missing file is logged via
    /// <c>System.Diagnostics.Debug.WriteLine</c> so misconfiguration is
    /// diagnosable, but no exception is raised — apps that deploy assets
    /// asynchronously, or icons that exist as a sidecar to a relocated
    /// executable, still construct successfully and surface the underlying
    /// load failure on <see cref="Apply"/>. (W-4 hardening.)</para>
    /// </summary>
    public static WindowIcon FromPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("WindowIcon path must be non-empty.", nameof(path));

        WarnIfUnrecognisedExtension(path);
        WarnIfMissing(path);

        return new WindowIcon(path, isResource: false);
    }

    private static readonly string[] s_recognisedIconExtensions =
        { ".ico", ".png", ".bmp", ".jpg", ".jpeg" };

    private static void WarnIfUnrecognisedExtension(string path)
    {
        var ext = global::System.IO.Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return;
        for (int i = 0; i < s_recognisedIconExtensions.Length; i++)
        {
            if (string.Equals(ext, s_recognisedIconExtensions[i], StringComparison.OrdinalIgnoreCase))
                return;
        }
        Debug.WriteLine(
            $"[Reactor] WindowIcon.FromPath: unrecognised icon extension '{ext}' on '{path}'. " +
            "Recognised extensions: .ico, .png, .bmp, .jpg, .jpeg.");
    }

    private static void WarnIfMissing(string path)
    {
        try
        {
            // Resolve relative paths against the running app's base directory
            // so an unpackaged icon shipped next to the .exe is found even
            // when the working directory was changed by a launcher.
            var resolved = global::System.IO.Path.IsPathRooted(path)
                ? path
                : global::System.IO.Path.Combine(AppContext.BaseDirectory, path);
            if (!global::System.IO.File.Exists(resolved))
            {
                Debug.WriteLine(
                    $"[Reactor] WindowIcon.FromPath: icon file not found at '{resolved}' (input: '{path}'). " +
                    "Apply will fall back to whatever the platform default is.");
            }
        }
        catch (Exception ex)
        {
            // File-system access can throw on locked-down hosts or invalid
            // characters in the path — don't let diagnostics block construction.
            Debug.WriteLine($"[Reactor] WindowIcon.FromPath: existence check failed for '{path}': {ex.Message}");
        }
    }

    /// <summary>
    /// Create an icon from a packaged-app resource URI
    /// (e.g. <c>ms-appx:///Assets/AppIcon.ico</c>). Throws on null/empty input.
    /// <para>The URI is handed to the platform unchanged, so MRT qualifier resolution
    /// applies. Manifest visual assets (<c>Square44x44Logo</c> and friends) are named by
    /// resource identifier rather than filename and are not addressable this way.</para>
    /// </summary>
    public static WindowIcon FromResource(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            throw new ArgumentException("WindowIcon resource URI must be non-empty.", nameof(uri));
        return new WindowIcon(uri, isResource: true);
    }

    /// <summary>
    /// Apply the icon to the given <see cref="AppWindow"/>. Best-effort: any
    /// failure inside the WinUI call is logged via
    /// <c>System.Diagnostics.Debug.WriteLine</c> and swallowed so that a
    /// missing icon never crashes window construction.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the icon was handed to the platform. <c>false</c> only when the
    /// source is a filesystem path that demonstrably does not exist — the caller then
    /// falls back rather than leaving the window with no icon at all.
    /// </returns>
    /// <remarks>
    /// The source is passed to <c>AppWindow.SetIcon</c> verbatim. Measured against
    /// Windows App SDK 2.1, that API accepts more than its documentation describes — both
    /// <c>ms-appx:///</c> URIs and non-<c>.ico</c> images work — so Reactor deliberately
    /// does not pre-validate the format or rewrite the URI. Rewriting an
    /// <c>ms-appx:</c> URI to a plain path would also bypass the platform's MRT
    /// scale/target-size qualifier resolution, breaking packaged apps that ship
    /// qualified variants. Only a resource URI is left entirely to the platform;
    /// a plain path is existence-checked first, because <c>SetIcon</c> reports a bad
    /// path by silently doing nothing.
    /// </remarks>
    internal bool Apply(AppWindow appWindow)
    {
        if (appWindow is null) return false;

        if (!_isResource && !PathExists(_source))
        {
            Debug.WriteLine($"[Reactor] WindowIcon.Apply: no icon file at '{_source}'.");
            return false;
        }

        try
        {
            appWindow.SetIcon(_source);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] WindowIcon.Apply failed for '{_source}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// True when a filesystem source resolves to a real file, either as given or relative
    /// to the app's base directory (which is the package root for a packaged app). Errors
    /// resolve to <c>true</c> so a probe failure never suppresses an icon that would have
    /// worked.
    /// </summary>
    private static bool PathExists(string path)
    {
        try
        {
            if (global::System.IO.File.Exists(path)) return true;
            if (global::System.IO.Path.IsPathRooted(path)) return false;
            return global::System.IO.File.Exists(
                global::System.IO.Path.Combine(AppContext.BaseDirectory, path));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] WindowIcon.Apply: existence check failed for '{path}': {ex.Message}");
            return true;
        }
    }
}
