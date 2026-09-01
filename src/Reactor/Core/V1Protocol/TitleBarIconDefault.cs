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
/// value compares equal across renders and <see cref="Apply"/> can skip the rewrite by
/// comparing it against what it last wrote. Handing back a fresh <c>IconSource</c>
/// instead would rebuild a <c>BitmapImage</c> — and re-run its decode — on every single
/// render, because two freshly-built ones never compare equal.</para>
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

    internal static void ResetForTests() => InvalidateCaches();

    /// <summary>
    /// Drops <em>both</em> resolved-path caches so the next resolve re-probes the
    /// filesystem. Test-only; the resync clears just the declared entry.
    /// </summary>
    /// <remarks>
    /// <para>The convention memo is deliberately never invalidated in production, because
    /// the window chain it mirrors does not re-probe either:
    /// <c>ReactorWindow.TryApplyExeIconFallback</c> caches <em>both</em> outcomes for the
    /// window's lifetime — a hit via <c>_exeFallbackHIcon</c> and a miss via
    /// <c>_exeFallbackAttempted</c> — on the stated assumption that "neither source can
    /// appear while the window is alive". Re-probing it would <em>create</em> a
    /// divergence: an <c>AppIcon.ico</c> appearing mid-run would light up the title bar
    /// while the caption kept serving its cached miss.</para>
    /// <para>The declared entry is the opposite case, which is why
    /// <see cref="ResyncInheritedIcon"/> clears it: <c>ApplyChrome</c> re-runs
    /// <c>spec.Icon.Apply(...)</c> on every application with no memo at all, so caching it
    /// here would strand the title bar when a same-instance <see cref="WindowIcon"/>
    /// starts or stops resolving. Both directions are the same rule — match the caching
    /// of whichever window arm produced the value — and the arms simply differ.</para>
    /// <para>One residue: the convention memo is process-wide while the window's is
    /// per-window, so a second window opened after an asset appeared would re-probe for
    /// its caption while this memo stays cold. Accepted rather than engineered away — it
    /// needs the asset to materialize while the app runs, which both layers already
    /// document as out of scope.</para>
    /// </remarks>
    private static void InvalidateCaches()
    {
        Volatile.Write(ref s_declared, null);
        Volatile.Write(ref s_convention, null);
    }

    /// <summary>
    /// The icon a <c>TitleBar</c> element should actually show: nothing when it opted
    /// out, its own declaration when it has one, otherwise the app's icon.
    /// </summary>
    /// <remarks>
    /// <see cref="TitleBarElement.SuppressIcon"/> is checked <em>first</em>. Both it and
    /// <see cref="TitleBarElement.Icon"/> are public <c>init</c> properties, so a record
    /// initializer or <c>with</c> expression can set both — and the documented contract
    /// for <c>SuppressIcon</c> is that it suppresses the icon entirely. The fluent
    /// <c>.Icon()</c> / <c>.NoIcon()</c> pair normalizes the other field, so this
    /// ordering only matters for the directly-constructed contradictory case, where
    /// honouring the suppression is what the property says it does.
    /// </remarks>
    internal static IconData? Project(TitleBarElement element)
    {
        if (element.SuppressIcon) return null;
        if (element.Icon is not null) return element.Icon;
        return ResolveDefault();
    }

    /// <summary>
    /// What was last written to a control, and whether the element owned the slot at
    /// the time. Recording ownership here rather than reading it back off the element
    /// is deliberate: a callback-free <c>TitleBar</c> is never tagged by
    /// <c>Reconciler.SetElementTagIfNeeded</c>, so <c>GetElementTag</c> returns null for
    /// exactly the common case this feature exists to serve.
    /// </summary>
    private sealed class AppliedIcon(
        IconData? value,
        bool elementOwned,
        bool authorOwned,
        Microsoft.UI.Xaml.Controls.IconSource? source)
    {
        internal readonly IconData? Value = value;
        internal readonly bool ElementOwned = elementOwned;

        /// <summary>
        /// Set once the control was observed carrying an <c>IconSource</c> this type did
        /// not write — i.e. a raw <c>.Set(...)</c> setter claimed the slot. Recorded from
        /// ground truth after the setters have actually run, rather than guessed from
        /// whether the element declares any setters at all: the common
        /// <c>.Set(b =&gt; capture = b)</c> idiom has nothing to do with the icon, and
        /// treating it as ownership would disable inheritance wholesale.
        /// </summary>
        internal readonly bool AuthorOwned = authorOwned;

        /// <summary>
        /// The exact <c>IconSource</c> instance this type wrote. Used by the out-of-band
        /// resync to tell "still mine" from "someone else has since written here".
        /// </summary>
        internal readonly Microsoft.UI.Xaml.Controls.IconSource? Source = source;
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
    /// <para>The fast path is deliberately bypassed for an author-owned record. Once
    /// <see cref="ObserveAfterSetters"/> has seen a setter claim the slot, the control no
    /// longer holds what this type wrote — so "the projection did not change" says
    /// nothing about whether the control is correct. Skipping there would strand the
    /// setter's icon forever once the setter was removed, because <c>owned</c> and
    /// <c>projected</c> still compare equal on the setter-free render. Writing instead
    /// costs one redundant assignment per render while a setter owns the slot, which is
    /// exactly what any descriptor prop under a setter already pays: the write lands
    /// first and the setter overwrites it immediately afterwards.</para>
    /// </remarks>
    internal static void Apply(Microsoft.UI.Xaml.Controls.TitleBar control, TitleBarElement element, bool force)
    {
        var owned = OwnsIconSlot(element);
        var projected = Project(element);
        if (!force
            && s_applied.TryGetValue(control, out var last)
            && !last.AuthorOwned
            && last.ElementOwned == owned
            && EqualityComparer<IconData?>.Default.Equals(last.Value, projected))
        {
            return;
        }

        var written = IconResolver.ResolveIconSource(projected);
        control.IconSource = written;
        s_applied.AddOrUpdate(control, new AppliedIcon(
            projected, owned, authorOwned: false, written));
    }

    /// <summary>
    /// Records whether a raw <c>.Set(...)</c> setter claimed the icon slot. Called by the
    /// reconciler after modifiers and setters have run, on both mount and update.
    /// </summary>
    /// <remarks>
    /// <para>Setters run <em>after</em> every descriptor prop — the documented "setters
    /// apply last / win" rule (spec 058) — so this is the first point at which the truth
    /// is observable. Comparing the control's actual <c>IconSource</c> against the
    /// instance <see cref="Apply"/> wrote answers "did a setter take the slot?" exactly,
    /// with no need to guess from <c>Setters.Length</c>.</para>
    /// <para>It also refreshes the record on the render where <see cref="Apply"/> took
    /// its equality fast path and wrote nothing, so ownership can never go stale behind a
    /// skipped write.</para>
    /// <para>One case stays ambiguous by construction: a setter writing
    /// <c>IconSource = null</c> when the projection was <em>also</em> null holds the same
    /// reference this type wrote, so it is invisible here. The out-of-band resync may then
    /// write an inherited icon over it once; the next render re-runs the setter, this
    /// observation sees the divergence, and ownership latches. That trade is deliberate —
    /// the alternative (treating any setter-bearing element as owning the slot) blocks
    /// inheritance for the common capture-only idiom indefinitely, because
    /// <c>ReactorWindow.Update</c> does not schedule a render.</para>
    /// </remarks>
    internal static void ObserveAfterSetters(Microsoft.UI.Xaml.Controls.TitleBar control)
    {
        if (!s_applied.TryGetValue(control, out var last)) return;
        if (ReferenceEquals(control.IconSource, last.Source)) return;

        s_applied.AddOrUpdate(control, new AppliedIcon(
            last.Value, last.ElementOwned, authorOwned: true, control.IconSource));
    }

    /// <summary>
    /// Re-resolves the inherited icon for an already-mounted title bar, against the
    /// spec of the window that owns it. Called by <see cref="ReactorWindow"/> when the
    /// window's own icon changes, because that is ambient state no element diff can see.
    /// </summary>
    /// <param name="control">The mounted WinUI <c>TitleBar</c> to refresh.</param>
    /// <param name="spec">
    /// The owning window's spec. Passed in rather than read from
    /// <c>ReactorApp.ActiveHostInternal</c> on purpose: <c>ReactorHost</c> scopes that
    /// static around a <em>render</em>, but this runs from <c>ApplyChrome</c>, which an
    /// app can reach at any time via <c>ReactorWindow.Update</c>. With two windows open,
    /// resolving ambient state here hands the updating window whichever host happened to
    /// be active — so updating the window that is <em>not</em> currently ambient would
    /// write the other window's icon into its title bar.
    /// </param>
    /// <param name="declaredIconApplied">
    /// The owning window's <c>DeclaredIconApplied</c> verdict, forwarded to
    /// <see cref="ResolveForSpec(WindowSpec?, bool?, out bool)"/> so the resync agrees with
    /// the window about which
    /// <em>source</em> won and not merely about which file the declaration names.
    /// </param>
    /// <remarks>
    /// No-op for a control this type never wrote to, and for a title bar whose element
    /// declared its own icon or opted out with <c>.NoIcon()</c> — those own the slot.
    /// <para>
    /// Also a no-op when a raw <c>.Set(...)</c> setter was <em>observed</em> to claim the
    /// icon slot, or when the control no longer holds the <c>IconSource</c> this type
    /// last wrote. Setters run <em>after</em> every descriptor prop — the documented
    /// "setters apply last / win" rule (spec 058, <c>DescriptorHandler.ApplySetters</c>)
    /// — so <c>.Set(b =&gt; b.IconSource = ...)</c> legitimately owns the slot even
    /// though the element declares no <c>Icon</c>. This push runs out of band from
    /// <c>ApplyChrome</c> with no setters to replay, so it must not clobber a value it
    /// did not write. Mirrors <c>ReactorWindow._reactorAppliedIcon</c>, which gates the
    /// window's own icon teardown the same way.
    /// </para>
    /// <para>
    /// "Observed" is the operative word, and the distinction is load-bearing: merely
    /// <em>carrying</em> setters does not claim the slot. <see cref="ObserveAfterSetters"/>
    /// compares the control's actual <c>IconSource</c> against the instance
    /// <see cref="Apply"/> wrote, so a capture-only <c>.Set(b =&gt; captured = b)</c> —
    /// which touches no icon — leaves ownership with Reactor and keeps inheriting. An
    /// earlier revision inferred ownership from <c>Setters.Length</c> instead and broke
    /// exactly that case; do not reintroduce it.
    /// </para>
    /// <para>
    /// Ownership is ground truth, not a guess. <see cref="ObserveAfterSetters"/> runs
    /// after the setters and records whether the control ended up carrying an
    /// <c>IconSource</c> this type did not write; that flag, plus the identity check
    /// below, is what stops the push clobbering an author's value. Deriving it from
    /// <c>Setters.Length</c> instead would be wrong in both directions — the common
    /// <c>.Set(b =&gt; capture = b)</c> idiom does not own the icon, and a setter added on
    /// a later render would not be reflected on a skipped write.
    /// </para>
    /// <para>
    /// Unlike <see cref="Apply"/>, this does <em>not</em> skip when the projected value is
    /// unchanged. The value is a <see cref="Uri"/>, so it compares equal even when the
    /// bytes at that path have been replaced — and <c>ApplyChrome</c> has just reloaded
    /// the caption's <c>HICON</c> from disk, so skipping here would leave the title bar
    /// showing a stale decode of a file the caption has already refreshed. That is exactly
    /// the divergence sharing the resolver exists to prevent. The rebuild is bounded to
    /// spec changes; the per-render fast path in <see cref="Apply"/> is untouched.
    /// </para>
    /// </remarks>
    internal static void ResyncInheritedIcon(
        Microsoft.UI.Xaml.Controls.TitleBar control, WindowSpec spec, bool? declaredIconApplied)
    {
        if (!s_applied.TryGetValue(control, out var last)) return;
        if (last.ElementOwned || last.AuthorOwned) return;
        if (!ReferenceEquals(control.IconSource, last.Source)) return;

        // Mirror ApplyChrome's caching arm by arm, because the two arms differ. Its
        // declared arm re-resolves on every application — `spec.Icon is { } icon &&
        // icon.Apply(_appWindow)` with no memo — so drop the declared entry here: the
        // same WindowIcon instance whose file appears or disappears would otherwise move
        // the caption while the title bar stayed put. Its convention/PE arm caches both
        // outcomes for the window's lifetime, so that memo is deliberately left standing.
        // See InvalidateCaches.
        Volatile.Write(ref s_declared, null);

        var projected = ResolveForSpec(spec, declaredIconApplied, out var fromDeclaredIcon);
        var written = ResolveForResync(projected, fromDeclaredIcon);
        control.IconSource = written;
        s_applied.AddOrUpdate(control, new AppliedIcon(
            projected, elementOwned: false, authorOwned: false, written));
    }

    /// <summary>
    /// Builds the <c>IconSource</c> for the out-of-band resync, bypassing the XAML image
    /// cache when — and only when — the window itself would have re-read the file.
    /// </summary>
    /// <remarks>
    /// A plain <c>BitmapImage</c> is keyed on its URI, so re-creating one for a path whose
    /// bytes changed can serve the previous decode. <c>IgnoreImageCache</c> forces the
    /// re-read. Only this path pays for it: normal renders go through
    /// <see cref="IconResolver.ResolveIconSource(IconData?)"/> and keep the cache.
    /// <para>Gated on <paramref name="fromDeclaredIcon"/> for the same reason the cache
    /// invalidation is gated — the two window arms differ. <c>ApplyChrome</c> re-runs
    /// <c>spec.Icon.Apply(...)</c> every time, so a declared file replaced in place does
    /// reach the caption and the title bar must follow. The convention/PE arm returns its
    /// cached <c>HICON</c> from <c>TryApplyExeIconFallback</c> forever, so forcing a
    /// re-decode there would both burn a decode on every unrelated <c>WindowSpec</c>
    /// update and make the title bar adopt bytes the caption never sees.</para>
    /// </remarks>
    /// <param name="projected">The projected icon, or <c>null</c> for none.</param>
    /// <param name="fromDeclaredIcon">
    /// <c>true</c> when <paramref name="projected"/> came from <see cref="WindowSpec.Icon"/>.
    /// </param>
    private static Microsoft.UI.Xaml.Controls.IconSource? ResolveForResync(
        IconData? projected, bool fromDeclaredIcon)
    {
        if (!fromDeclaredIcon || projected is not ImageIconData image)
            return IconResolver.ResolveIconSource(projected);

        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage
        {
            CreateOptions = Microsoft.UI.Xaml.Media.Imaging.BitmapCreateOptions.IgnoreImageCache,
        };
        bitmap.UriSource = image.Source;
        return new Microsoft.UI.Xaml.Controls.ImageIconSource { ImageSource = bitmap };
    }

    /// <summary>
    /// The app's icon as an <see cref="IconData"/>, or <c>null</c> when none is
    /// resolvable. See the type-level remarks for precedence and the one divergence
    /// from the window chain.
    /// </summary>
    internal static IconData? ResolveDefault()
    {
        var window = ReactorApp.ActiveHostInternal?.OwningWindow;
        return ResolveForSpec(window?.Spec, window?.DeclaredIconApplied);
    }

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
    /// <param name="declaredIconApplied">
    /// <c>ReactorWindow.DeclaredIconApplied</c> — whether <c>ApplyChrome</c> saw the
    /// declared icon actually reach the window. <c>false</c> forces the convention arm
    /// even when the declaration resolves to a real file, because the window itself fell
    /// through to its fallback; <c>null</c> (no decision yet, or no live window) keeps the
    /// optimistic behaviour of trusting the declaration.
    /// <para>In practice <c>false</c> almost always means the path did not resolve, which
    /// <see cref="TryResolveDeclared"/> would have caught anyway. The resolve-succeeded
    /// case is the defensive one: <c>AppWindow.SetIcon</c> was measured on Windows App SDK
    /// 2.1 to accept both a text file named <c>.ico</c> and a real <c>.ico</c> held open
    /// with <c>FileShare.None</c> — it silently applies a default rather than throwing —
    /// so no end-to-end selftest could stage it. Carrying the window's verdict rather than
    /// re-deriving a proxy for it is still the right shape, and costs nothing.</para>
    /// </param>
    internal static IconData? ResolveForSpec(WindowSpec? spec, bool? declaredIconApplied = null)
        => ResolveForSpec(spec, declaredIconApplied, out _);

    /// <summary>
    /// As <see cref="ResolveForSpec(WindowSpec?, bool?)"/>, additionally reporting which
    /// arm produced the value so the caller can match that arm's cache policy.
    /// </summary>
    /// <param name="spec">The owning window's spec.</param>
    /// <param name="declaredIconApplied">The window's declared-icon verdict.</param>
    /// <param name="fromDeclaredIcon">
    /// <c>true</c> when the result came from <see cref="WindowSpec.Icon"/> rather than the
    /// convention asset. Reported rather than re-derived by the caller: deriving the same
    /// fact a second way is how two copies drift apart.
    /// </param>
    internal static IconData? ResolveForSpec(
        WindowSpec? spec, bool? declaredIconApplied, out bool fromDeclaredIcon)
    {
        fromDeclaredIcon = false;

        // Mirror ApplyChrome's `spec.Embed is null` guard: an embedded window never gets
        // a window icon, so there is no window icon for its title bar to inherit.
        if (spec?.Embed is not null) return null;

        // A declared icon that resolves to no file falls through to the convention,
        // exactly as it does for the window itself in ApplyChrome. So does one the window
        // resolved but SetIcon rejected — TryResolveDeclared proves only that a file
        // exists, and agreeing with the window means agreeing about the source that won.
        if (declaredIconApplied is not false
            && spec?.Icon is { } declared
            && TryResolveDeclared(declared) is { } fromSpec)
        {
            fromDeclaredIcon = true;
            return fromSpec;
        }

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
    private static ImageIconData? BuildIconData(string resolvedPath)
        => BuildUri(resolvedPath) is { } uri ? new ImageIconData(uri) : null;

    /// <summary>
    /// Prefers <c>ms-appx:///</c> for anything under the app root, falling back to a
    /// <c>file:///</c> URI for an icon that lives elsewhere.
    /// </summary>
    /// <remarks>
    /// <para>Both forms were measured to load unpackaged. <c>ms-appx:</c> is preferred
    /// for package content because it is XAML's native form and goes through MRT, so a
    /// packaged app can serve a scale-qualified variant. The packaged case is measured
    /// too: <c>Reactor.PackagedTests.Host</c> links every fixture source from
    /// <c>Reactor.AppTests.Host</c>, and these fixtures declare no tier (so
    /// <c>SelfTestTier.Any</c>), which puts the <c>FromResource("ms-appx:///...")</c> arm
    /// of <c>TitleBarIcon_WindowSpec</c> under real package identity.</para>
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
    /// <para>Returns <c>null</c> rather than throwing when the path will not form a URI.
    /// <c>WindowIcon.TryResolvePath</c> is permissive by design at one edge: when its
    /// filesystem probe itself fails (a locked-down or malformed path) it reports success
    /// with the original, unverified source, so that a filesystem which merely refuses
    /// the probe never suppresses an icon that would otherwise have worked. That is
    /// sound for the window, whose consumer hands the value to <c>SetIcon</c> inside a
    /// catch — but this projection runs inside a render, where an escaping
    /// <c>UriFormatException</c> would take the frame down over a cosmetic icon. A path
    /// that cannot become a URI degrades to no title-bar icon, matching what the rest of
    /// this type does when nothing is resolvable.</para>
    /// </remarks>
    private static Uri? BuildUri(string resolvedPath)
    {
        if (TryGetAppRelativeSegments(resolvedPath, out var relative)
            && Uri.TryCreate("ms-appx:///" + relative, UriKind.Absolute, out var packaged))
        {
            return packaged;
        }

        return Uri.TryCreate(resolvedPath, UriKind.Absolute, out var file) ? file : null;
    }

    /// <summary>Test seam for <see cref="BuildUri"/> — the URI form is a decision worth
    /// asserting on directly, without staging a whole window to observe it. Null when the
    /// path will not form a URI at all.</summary>
    internal static Uri? BuildUriForTests(string resolvedPath) => BuildUri(resolvedPath);

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
