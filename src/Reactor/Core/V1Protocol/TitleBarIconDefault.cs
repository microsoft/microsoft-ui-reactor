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
    /// What was last written to a control, and whether the element owned the slot at
    /// the time. Recording ownership here rather than reading it back off the element
    /// is deliberate: a callback-free <c>TitleBar</c> is never tagged by
    /// <c>Reconciler.SetElementTagIfNeeded</c>, so <c>GetElementTag</c> returns null for
    /// exactly the common case this feature exists to serve.
    /// </summary>
    private sealed class AppliedIcon(IconData? value, bool elementOwned)
    {
        internal readonly IconData? Value = value;
        internal readonly bool ElementOwned = elementOwned;
    }

    private static readonly ConditionalWeakTable<Microsoft.UI.Xaml.Controls.TitleBar, AppliedIcon>
        s_applied = new();

    /// <summary>True when the element declares the icon itself, or opted out.</summary>
    private static bool OwnsIconSlot(TitleBarElement element)
        => element.Icon is not null || element.SuppressIcon;

    /// <summary>
    /// Writes the projected icon to <paramref name="control"/>, skipping the write when
    /// it already carries that exact projection.
    /// </summary>
    /// <param name="control">The mounted WinUI <c>TitleBar</c> to write to.</param>
    /// <param name="element">The element being mounted or updated.</param>
    /// <param name="force">
    /// <c>true</c> on mount. A pooled or recycled control may carry a record left by the
    /// element that previously used it, so mount always writes rather than trusting it.
    /// </param>
    /// <remarks>
    /// This is an <c>Imperative</c> entry rather than a <c>OneWay</c> one on purpose.
    /// <c>OneWayPropEntry.Update</c> decides whether to write by comparing
    /// <c>get(oldElement)</c> with <c>get(newElement)</c> — which works only for values
    /// derived purely from the element. The inherited default is <em>ambient</em>: it is
    /// read from the owning window, so after <c>WindowSpec.Icon</c> changes both the old
    /// and the new element project the same new value, the comparison finds them equal,
    /// and the control keeps the previous icon forever. Comparing against what was last
    /// written to the control instead of against the previous element is what makes an
    /// ambient change observable.
    /// </remarks>
    internal static void Apply(Microsoft.UI.Xaml.Controls.TitleBar control, TitleBarElement element, bool force)
    {
        var owned = OwnsIconSlot(element);
        var projected = Project(element);
        if (!force
            && s_applied.TryGetValue(control, out var last)
            && last.ElementOwned == owned
            && EqualityComparer<IconData?>.Default.Equals(last.Value, projected))
        {
            return;
        }

        control.IconSource = IconResolver.ResolveIconSource(projected);
        s_applied.AddOrUpdate(control, new AppliedIcon(projected, owned));
    }

    /// <summary>
    /// Re-resolves the inherited icon for an already-mounted title bar. Called by
    /// <see cref="ReactorWindow"/> when the window's own icon changes, because that is
    /// ambient state no element diff can see.
    /// </summary>
    /// <remarks>
    /// No-op for a control this type never wrote to, and for a title bar whose element
    /// declared its own icon or opted out with <c>.NoIcon()</c> — those own the slot.
    /// </remarks>
    internal static void ResyncInheritedIcon(Microsoft.UI.Xaml.Controls.TitleBar control)
    {
        if (!s_applied.TryGetValue(control, out var last) || last.ElementOwned) return;

        var projected = ResolveDefault();
        if (EqualityComparer<IconData?>.Default.Equals(last.Value, projected)) return;

        control.IconSource = IconResolver.ResolveIconSource(projected);
        s_applied.AddOrUpdate(control, new AppliedIcon(projected, elementOwned: false));
    }

    /// <summary>
    /// The app's icon as an <see cref="IconData"/>, or <c>null</c> when none is
    /// resolvable. See the type-level remarks for precedence and the one divergence
    /// from the window chain.
    /// </summary>
    /// <summary>
    /// The app's icon as an <see cref="IconData"/>, or <c>null</c> when none is
    /// resolvable. See the type-level remarks for precedence and the one divergence
    /// from the window chain.
    /// </summary>
    internal static IconData? ResolveDefault()
        => ResolveForSpec(ReactorApp.ActiveHostInternal?.OwningWindow?.Spec);

    /// <summary>
    /// The icon a window with <paramref name="spec"/> contributes to its title bar.
    /// Split out from <see cref="ResolveDefault"/> so the precedence rules are testable
    /// without staging a live window — the ambient lookup is the only part that needs one.
    /// </summary>
    /// <param name="spec">
    /// The owning window's spec, or <c>null</c> for a bare <c>ReactorHost</c> with no
    /// owning window. That is not an embed, so it still resolves the app-level
    /// convention asset.
    /// </param>
    internal static IconData? ResolveForSpec(WindowSpec? spec)
    {
        // Mirror ApplyChrome's `spec.Embed is null` guard: an embedded window never gets
        // a window icon, so there is no window icon for its title bar to inherit.
        if (spec?.Embed is not null) return null;

        // A declared icon that resolves to no file falls through to the convention,
        // exactly as it does for the window itself in ApplyChrome.
        if (spec?.Icon is { } declared && TryResolveDeclared(declared) is { } fromSpec)
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
