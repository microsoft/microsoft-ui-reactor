using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Supplies <c>TitleBar</c>'s icon when the app did not declare one, so that
/// <c>TitleBar("MyApp")</c> shows the application's own mark instead of nothing.
/// </summary>
/// <remarks>
/// <para><b>Precedence mirrors <c>ReactorWindow.ApplyChrome</c></b>: the declared
/// <see cref="WindowSpec.Icon"/> first, then the <c>Assets\AppIcon.ico</c> convention.
/// A declared icon that resolves to no file falls through to the convention, exactly
/// as it does for the window itself. Note the practically-exercised order is the
/// inverse of how that reads: real apps overwhelmingly ship the convention asset and
/// pass no icon at all, so the convention is the common path and a declared
/// <see cref="WindowSpec.Icon"/> is the explicit opt-in.</para>
/// <para><b>One deliberate divergence.</b> The window chain has a third stage — the
/// icon embedded in the executable's PE resources, via <c>ExtractIconExW</c>. That
/// stage yields a raw <c>HICON</c> and no path, and a XAML <c>IconSource</c> needs an
/// <c>ImageSource</c>. Bridging the two means <c>GetIconInfo</c> + <c>GetDIBits</c>
/// into a BGRA8 <c>SoftwareBitmap</c> behind an async <c>SoftwareBitmapSource</c>,
/// which is a lot of interop for the case where the app shipped no icon asset at all.
/// It is not implemented: an app whose only icon is the PE resource gets a title bar
/// with no icon, which is exactly the behaviour it had before this feature existed.</para>
/// <para><b>Why <c>ImageIconData</c> and not a live <c>IconSource</c>.</b>
/// <see cref="ImageIconData"/> is a record over a <see cref="Uri"/>, so the projected
/// value compares equal across renders and the descriptor's <c>OneWay</c> diff skips
/// the rewrite. Handing back a fresh <c>IconSource</c> instead would rebuild a
/// <c>BitmapImage</c> — and re-run its decode — on every single render.</para>
/// </remarks>
internal static class TitleBarIconDefault
{
    /// <summary>Cached resolution of one declared <see cref="WindowIcon"/>.</summary>
    private sealed class DeclaredEntry(WindowIcon key, IconData? value)
    {
        internal readonly WindowIcon Key = key;
        internal readonly IconData? Value = value;
    }

    // Both caches are read/written as single references so a torn read is impossible;
    // the worst a race can do is recompute, never observe a half-built pair. Rendering
    // is per-UI-thread, so contention here is theoretical rather than expected.
    private static DeclaredEntry? s_declared;
    private static StrongBox<IconData?>? s_convention;
    private static string? s_baseDirectoryOverride;

    /// <summary>
    /// Root the convention probe searches. Overridable so tests can point it at a
    /// temporary directory instead of writing a real <c>Assets\AppIcon.ico</c> into the
    /// test host's own output directory, which would give every other windowing fixture
    /// a window icon.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> used to decide whether a path is expressible as
    /// <c>ms-appx:</c> — that question is about the real package root and a test cannot
    /// move it. See <see cref="TryGetAppRelativeSegments"/>.
    /// </remarks>
    internal static string ConventionProbeRoot => s_baseDirectoryOverride ?? AppContext.BaseDirectory;

    internal static void SetBaseDirectoryForTests(string? directory)
    {
        s_baseDirectoryOverride = directory;
        ResetForTests();
    }

    internal static void ResetForTests()
    {
        Volatile.Write(ref s_declared, null);
        Volatile.Write(ref s_convention, null);
    }

    /// <summary>
    /// The icon a <c>TitleBar</c> element should actually show: its own declaration
    /// when it has one, nothing when it opted out, otherwise the app's icon.
    /// </summary>
    internal static IconData? Project(TitleBarElement element)
    {
        if (element.Icon is not null) return element.Icon;
        if (element.SuppressIcon) return null;
        return ResolveDefault();
    }

    /// <summary>
    /// The app's icon as an <see cref="IconData"/>, or <c>null</c> when none is
    /// resolvable. See the type-level remarks for precedence and the one divergence
    /// from the window chain.
    /// </summary>
    internal static IconData? ResolveDefault()
    {
        var window = ReactorApp.ActiveHostInternal?.OwningWindow;

        // Mirror ApplyChrome's `spec.Embed is null` guard: an embedded window never
        // gets a window icon, so there is no window icon for its title bar to inherit.
        // A bare ReactorHost with no owning window is not an embed — it still resolves
        // the app-level convention asset below.
        if (window?.Spec.Embed is not null) return null;

        if (window?.Spec.Icon is { } declared && TryResolveDeclared(declared) is { } fromSpec)
            return fromSpec;

        return ResolveConvention();
    }

    private static IconData? TryResolveDeclared(WindowIcon declared)
    {
        var cached = Volatile.Read(ref s_declared);
        if (cached is not null && ReferenceEquals(cached.Key, declared)) return cached.Value;

        // WindowIcon.TryResolvePath is the same resolver AppWindow.SetIcon goes
        // through, so the title bar and the window caption cannot disagree about which
        // file a declared icon names.
        var value = declared.TryResolvePath(out var path) ? BuildIconData(path) : null;
        Volatile.Write(ref s_declared, new DeclaredEntry(declared, value));
        return value;
    }

    private static IconData? ResolveConvention()
    {
        var cached = Volatile.Read(ref s_convention);
        if (cached is not null) return cached.Value;

        var value = AppIconConvention.TryGetAssetPath(ConventionProbeRoot, out var path)
            ? BuildIconData(path)
            : null;
        Volatile.Write(ref s_convention, new StrongBox<IconData?>(value));
        return value;
    }

    /// <summary>
    /// Wraps a resolved icon file as an <see cref="ImageIconData"/>.
    /// </summary>
    /// <remarks>
    /// <para><see cref="ImageIconData"/> rather than <see cref="BitmapIconData"/> on
    /// purpose: <c>BitmapIconSource.ShowAsMonochrome</c> defaults to <c>true</c> and
    /// would flatten a full-colour <c>.ico</c> into a single-tone silhouette. Choosing
    /// the projection that has no such property avoids the hazard by construction
    /// rather than by remembering to clear a flag.</para>
    /// <para>Measured on Windows App SDK 2.1, unpackaged: <c>ImageIconSource</c> over a
    /// <c>BitmapImage</c> renders both URI forms, and decodes the <em>largest</em>
    /// frame of a multi-resolution <c>.ico</c> (128x128 for the test asset) rather than
    /// a 16x16 frame 0 — so no <c>DecodePixelWidth</c> hint is needed, and the
    /// template's 16x16 <c>PART_Icon</c> viewbox downscales, which is the
    /// quality-preserving direction.</para>
    /// </remarks>
    private static ImageIconData BuildIconData(string resolvedPath) =>
        new(BuildUri(resolvedPath));

    /// <summary>
    /// Prefers <c>ms-appx:///</c> for anything under the app root, falling back to a
    /// <c>file:///</c> URI for an icon that lives elsewhere.
    /// </summary>
    /// <remarks>
    /// <para>Both forms were measured to load unpackaged. <c>ms-appx:</c> is preferred
    /// for package content because it is XAML's native form and goes through MRT, so a
    /// packaged app can serve a scale-qualified variant — and the packaged case is the
    /// one that could not be measured here (the self-test host is
    /// <c>WindowsPackageType=None</c>).</para>
    /// <para>Note this is the opposite choice from <c>WindowIcon.Apply</c>, which must
    /// hand <c>AppWindow.SetIcon</c> a filesystem path because that API silently applies
    /// a default icon when given a packaged-resource URI. The two APIs consume URIs
    /// differently; neither convention is safe to assume for the other.</para>
    /// <para>The <c>ms-appx:</c> form is a <em>re-derivation</em> — it discards the path
    /// whose existence was just verified and substitutes a string that has to map back
    /// to it. That is only sound while the relative path is computed against the real
    /// package root, which is why <see cref="TryGetAppRelativeSegments"/> reads
    /// <see cref="AppContext.BaseDirectory"/> and not the overridable
    /// <see cref="ConventionProbeRoot"/>. Anything outside that root takes the
    /// <c>file:</c> form, which is built straight from the verified path and needs no
    /// mapping assumption at all.</para>
    /// </remarks>
    private static Uri BuildUri(string resolvedPath)
    {
        if (TryGetAppRelativeSegments(resolvedPath, out var relative))
            return new Uri("ms-appx:///" + relative);

        return new Uri(resolvedPath);
    }

    /// <summary>Test seam for <see cref="BuildUri"/> — the URI form is a decision worth
    /// asserting on directly, without staging a whole window to observe it.</summary>
    internal static Uri BuildUriForTests(string resolvedPath) => BuildUri(resolvedPath);

    /// <summary>
    /// Expresses <paramref name="fullPath"/> as a slash-separated, percent-escaped path
    /// relative to the real package root, or returns false when it lives outside.
    /// </summary>
    private static bool TryGetAppRelativeSegments(string fullPath, out string relative)
    {
        relative = string.Empty;
        try
        {
            // AppContext.BaseDirectory, never ConventionProbeRoot: "ms-appx:///x" means
            // "x under the package install root", and a test override cannot change what
            // the platform resolves that to. Deriving the relative path from a relocated
            // root would emit a URI naming a file that does not exist.
            var rel = global::System.IO.Path.GetRelativePath(AppContext.BaseDirectory, fullPath);

            // GetRelativePath returns the input unchanged when the two share no root,
            // and a ".."-prefixed path when the target is above the base. Either way
            // the file is not addressable as a packaged resource.
            if (global::System.IO.Path.IsPathRooted(rel)) return false;
            var segments = rel.Split('/', '\\');
            if (global::System.Array.IndexOf(segments, "..") >= 0) return false;

            // Escape per segment: EscapeDataString would otherwise percent-encode the
            // separators themselves and collapse the path into a single name.
            relative = string.Join('/', segments.Select(Uri.EscapeDataString));
            return relative.Length > 0;
        }
        catch (Exception ex) when (ex is ArgumentException or global::System.IO.IOException)
        {
            // A malformed base directory or path shape. Fall back to the file: form,
            // which needs no relationship between the two.
            return false;
        }
    }
}
