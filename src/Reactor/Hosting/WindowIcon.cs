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
/// <item><description><see cref="WindowSpec.Icon"/> — accepts both kinds. An
/// <c>ms-appx:</c> source is mapped to a file beside the app before it reaches
/// <c>AppWindow.SetIcon</c>, which needs a filesystem path: handed the URI itself
/// inside a packaged app it silently applies a default icon.</description></item>
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
    /// <para>The URI is mapped to a file beside the app (the package install root when
    /// packaged) before it reaches <c>AppWindow.SetIcon</c>, which requires a filesystem
    /// path. Manifest visual assets (<c>Square44x44Logo</c> and friends) are named by
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
    /// <c>true</c> when the icon was handed to the platform. <c>false</c> when it was
    /// not: a filesystem path that demonstrably does not exist, a <c>null</c>
    /// <paramref name="appWindow"/>, or a <c>SetIcon</c> call that threw. The caller
    /// then falls back rather than leaving the window with no icon at all.
    /// </returns>
    /// <remarks>
    /// <para>A filesystem source is resolved to an absolute path first, preferring the
    /// app's base directory over the process working directory. That keeps the
    /// existence check and the value handed to <c>SetIcon</c> describing the same file,
    /// and stops a launcher-chosen working directory from substituting a different
    /// icon for a relative path.</para>
    /// <para>An <c>ms-appx:</c> source is translated to a filesystem path the same way.
    /// <c>AppWindow.SetIcon</c> takes "the fully qualified path to the .ico file"; handed
    /// a packaged-resource URI inside an MSIX app it does <b>not</b> load the asset — it
    /// silently applies a default icon instead. Measured on Windows App SDK 2.1: in a
    /// packaged process the URI form yields the same shared handle for every window,
    /// while the path form yields a real per-window icon. (It appears to work in an
    /// <i>unpackaged</i> process only because <c>ms-appx:</c> maps to the executable
    /// directory there, which is why this needs a packaged app to observe.)</para>
    /// <para>If the translated path does not exist the original URI is passed through
    /// unchanged, so an asset the platform can resolve some other way — an MRT
    /// scale/target-size qualified variant, say — still gets its chance.</para>
    /// </remarks>
    internal bool Apply(AppWindow appWindow)
    {
        if (appWindow is null) return false;

        var target = _source;
        if (_isResource)
        {
            // Best-effort: fall through with the raw URI when it cannot be mapped.
            if (TryResolveResourceUri(_source, out var fromResource)) target = fromResource;
        }
        else if (!TryResolveExistingPath(_source, out target))
        {
            Debug.WriteLine($"[Reactor] WindowIcon.Apply: no icon file at '{_source}'.");
            return false;
        }

        try
        {
            appWindow.SetIcon(target);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] WindowIcon.Apply failed for '{_source}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Maps an <c>ms-appx:///Assets/App.ico</c> style URI onto an existing file under
    /// <see cref="AppContext.BaseDirectory"/>, which is the install root for a packaged
    /// app and the executable directory otherwise.
    /// </summary>
    /// <returns>
    /// <c>false</c> when the URI is not <c>ms-appx:</c>, names no asset, or maps to a file
    /// that does not exist — the caller then passes the original URI to the platform
    /// rather than second-guessing it.
    /// </returns>
    internal static bool TryResolveResourceUri(string uri, out string resolved)
    {
        resolved = uri;
        const string scheme = "ms-appx:";
        if (string.IsNullOrEmpty(uri) ||
            !uri.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = uri.Substring(scheme.Length);

        // "ms-appx:///Assets/App.ico" has an empty authority; "ms-appx://pkg/Assets/App.ico"
        // names one. Drop the authority segment in both shapes.
        if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            rest = rest.Substring(2);
            var slash = rest.IndexOf('/');
            rest = slash >= 0 ? rest.Substring(slash + 1) : string.Empty;
        }

        rest = rest.TrimStart('/');
        if (rest.Length == 0) return false;

        try
        {
            var candidate = global::System.IO.Path.Join(
                AppContext.BaseDirectory,
                rest.Replace('/', global::System.IO.Path.DirectorySeparatorChar));
            if (!global::System.IO.File.Exists(candidate)) return false;
            resolved = candidate;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] WindowIcon: could not map '{uri}' to a path: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Resolves a filesystem source to an existing absolute path. Relative paths are
    /// tried against <see cref="AppContext.BaseDirectory"/> first (the package root for
    /// a packaged app) and only then against the process working directory, so a
    /// relative icon means "beside my app" rather than "wherever I happened to be
    /// launched from".
    /// </summary>
    /// <returns>
    /// <c>false</c> only when the file is proven absent. A probe that throws resolves to
    /// <c>true</c> with the original source, so a locked-down filesystem never suppresses
    /// an icon that would otherwise have worked.
    /// </returns>
    private static bool TryResolveExistingPath(string path, out string resolved)
    {
        resolved = path;
        try
        {
            if (global::System.IO.Path.IsPathRooted(path))
                return global::System.IO.File.Exists(path);

            // Path.Join rather than Path.Combine: Join always concatenates, whereas
            // Combine discards everything before a rooted segment.
            var beside = global::System.IO.Path.Join(AppContext.BaseDirectory, path);
            if (global::System.IO.File.Exists(beside))
            {
                resolved = beside;
                return true;
            }

            if (global::System.IO.File.Exists(path))
            {
                resolved = global::System.IO.Path.GetFullPath(path);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Reactor] WindowIcon.Apply: path probe failed for '{path}': {ex.Message}");
            resolved = path;
            return true;
        }
    }
}
