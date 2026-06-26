using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// A reference to a WinUI theme resource that resolves at render time.
/// Use via <see cref="Theme"/> tokens or <see cref="Theme.Ref"/> for custom keys.
/// </summary>
// <snippet:theme-ref>
public readonly record struct ThemeRef(string ResourceKey)
{
    public override string ToString() => $"ThemeRef({ResourceKey})";

    /// <summary>
    /// Resolves this theme reference using the element's actual theme.
    /// Walks the ThemeDictionaries in Application.Resources and MergedDictionaries
    /// to find the brush matching the element's effective theme (which respects
    /// per-element RequestedTheme overrides, not just the app-level theme).
    /// </summary>
    internal static Brush? Resolve(string resourceKey, FrameworkElement fe)
    {
        var themeName = GetEffectiveThemeName(fe);
        return ResolveForTheme(resourceKey, themeName);
    }
// </snippet:theme-ref>

    /// <summary>
    /// Resolves a theme resource using an explicit isDark flag.
    /// Useful for resolving during Render() before controls are in the tree.
    /// </summary>
    public static Brush? Resolve(string resourceKey, bool isDark)
    {
        // Public overload — intentionally UNCACHED. The (key, theme) brush cache
        // (#86) is dropped only by the internal InvalidateCache the host wires to
        // ActualThemeChanged / UISettings.ColorValuesChanged; this overload has no
        // associated element or theme listener, and there is no public invalidation
        // API, so a cached entry could never be dropped for a caller that swaps
        // Application.Current.Resources / ThemeDictionaries at runtime. Scanning on
        // every call preserves the pre-#86 semantics (runtime dictionary swaps
        // observed immediately); the cache stays on the internal FrameworkElement
        // Resolve hot path that the data-grid stress workload actually exercises.
        return ResolveForThemeUncached(resourceKey, isDark ? "Dark" : "Light");
    }

    // ── Resolution caches (perf #85/#86) ─────────────────────────────
    // The data-grid stress path resolves dozens of theme tokens per cell per
    // render. Both the per-element effective-theme tree walk (#85) and the
    // recursive MergedDictionaries scan (#86) were uncached and re-run on every
    // ThemeRef.Resolve. These caches collapse that to one tree walk per element
    // per reconcile pass and one dictionary scan per (key, theme) pair.

    // #86: resolved brush per (resourceKey, themeName), stamped with the theme
    // generation it was resolved under. The themeName is part of the key, so
    // Light/Dark entries coexist; cleared on theme/colour change so a runtime
    // theme-dictionary swap is observed. Only non-null hits are cached, so a
    // genuinely-absent key is re-resolved (and picked up if added later) exactly
    // as before. Both publication and hits are generation-guarded (see
    // ResolveForTheme) so a resolve racing an InvalidateCache can neither
    // republish a pre-invalidation brush into the just-cleared cache nor return a
    // stale entry during the bump-then-clear window.
    private static readonly ConcurrentDictionary<(string Key, string Theme), (int Gen, Brush Brush)> _brushCache = new();

    // Bumped by InvalidateCache on every theme / system-colour change. Stamped
    // into each cached entry (Gen) and validated on every hit, so an entry that
    // predates an invalidation is treated as a miss and re-resolved — closing the
    // window between InvalidateCache's generation bump and its _brushCache.Clear()
    // where a concurrent hit could otherwise return a pre-invalidation brush
    // (PR-review L). Also guards publication against a racing invalidation.
    private static int _themeGeneration;

    // #85: effective theme name per element, scoped to a single reconcile pass.
    // The effective theme of a Default-themed element is inherited from the
    // nearest ANCESTOR with an explicit RequestedTheme — a value none of the
    // element's own dependency properties reflect until WinUI propagates
    // ActualTheme, which is not guaranteed within the synchronous reconcile that
    // applied the ancestor override. Caching the name across reconciles would
    // therefore let a subtree-local RequestedTheme flip serve a stale theme
    // (PR-review H1). Instead the cache is reconcile-scoped: the host bumps
    // _reconcilePass once per render (BeginReconcilePass), so each element walks
    // at most once per pass — collapsing the dozens of per-token resolves on one
    // element into a single walk (the #85 win) while matching the original
    // always-walk result exactly (ancestors are applied top-down before the
    // descendant is reconciled, so the per-pass walk observes the current theme).
    // The cheap per-element RequestedTheme / ActualTheme comparison additionally
    // re-walks mid-pass if an app-level theme flip propagates to this element.
    private sealed class ThemeNameBox
    {
        public int Pass = -1;
        public ElementTheme RequestedTheme = (ElementTheme)(-1);
        public ElementTheme ActualTheme = (ElementTheme)(-1);
        public string? Name;
    }
    private static readonly ConditionalWeakTable<FrameworkElement, ThemeNameBox> _themeNameCache = new();
    private static int _reconcilePass;

    /// <summary>
    /// Opens a new reconcile pass for the per-element effective-theme-name cache,
    /// so every element recomputes its effective theme at most once this pass.
    /// Called by the host at the start of each render (before reconciliation) so a
    /// subtree-local <see cref="FrameworkElement.RequestedTheme"/> change applied
    /// during the pass is observed by descendants resolved later in the same pass.
    /// Cheap and allocation-free; safe to call from any thread.
    /// </summary>
    internal static void BeginReconcilePass()
    {
        Interlocked.Increment(ref _reconcilePass);
    }

    /// <summary>
    /// Drops all cached theme resolution (effective theme names and resolved
    /// brushes). Called by the host when the effective theme or system colours
    /// change (ActualThemeChanged / UISettings.ColorValuesChanged) so the next
    /// <see cref="Resolve(string, FrameworkElement)"/> recomputes against the
    /// new theme. Cheap and allocation-free; safe to call from any thread.
    /// </summary>
    internal static void InvalidateCache()
    {
        Interlocked.Increment(ref _themeGeneration);
        // Also open a new name-cache pass so a theme change invalidates cached
        // effective-theme names immediately, independent of render timing.
        Interlocked.Increment(ref _reconcilePass);
        _brushCache.Clear();
    }

    private static Brush? ResolveForTheme(string resourceKey, string themeName)
    {
        var cacheKey = (resourceKey, themeName);
        // Capture the generation BEFORE the cache probe and the uncached scan. A
        // cached entry counts as a hit only when its stamp matches this generation
        // both before AND after the probe — the same re-read the publish path below
        // performs. An entry left behind by a not-yet-completed InvalidateCache
        // (which bumps the generation before it clears the cache) carries an older
        // Gen, and the post-probe re-read also rejects the case where the bump lands
        // between our generation read and the dictionary lookup, so neither arm can
        // hand back a pre-invalidation brush (PR-review L). An invalidation ordered
        // entirely after the second read is linearisably "after" this resolve and is
        // corrected by the RequestRender the host pairs with every InvalidateCache.
        var gen = Volatile.Read(ref _themeGeneration);
        if (_brushCache.TryGetValue(cacheKey, out var cached) && cached.Gen == gen
            && Volatile.Read(ref _themeGeneration) == gen)
            return cached.Brush;

        // If an InvalidateCache() races us — it bumps the generation and clears
        // the cache, e.g. from UISettings.ColorValuesChanged on a WinRT pool thread
        // while we resolve on the UI thread — `resolved` may predate the theme
        // change, so writing it back into the just-cleared cache would serve a
        // stale brush until the next invalidation. Publish only when no
        // invalidation raced us; otherwise return the value but leave the cache
        // empty so the next resolve re-scans.
        var resolved = ResolveForThemeUncached(resourceKey, themeName);
        // Only cache successful resolves so an absent key stays re-resolvable
        // (matches the pre-cache behaviour where each call re-scanned).
        if (resolved is not null && Volatile.Read(ref _themeGeneration) == gen)
            _brushCache[cacheKey] = (gen, resolved);
        return resolved;
    }

    private static Brush? ResolveForThemeUncached(string resourceKey, string themeName)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return null;

        // WinUI's XamlControlsResources ThemeDictionary keys vary by app configuration:
        //   Keys observed: "Default", "Light", "HighContrast" (no "Dark")
        // "Default" contains the base/dark brushes; "Light" contains light overrides.
        // Try the exact theme name first, then "Default" as the universal fallback.
        if (TryResolveFromThemeDictionaries(resources, resourceKey, themeName, out var brush))
            return brush;
        if (TryResolveFromThemeDictionaries(resources, resourceKey, "Default", out brush))
            return brush;

        // Fallback: non-themed resource lookup (including MergedDictionaries)
        if (TryResolveNonThemed(resources, resourceKey, out var fb))
            return fb;

        return null;
    }

    /// <summary>
    /// Determines the effective theme by walking up the visual tree looking for
    /// the nearest explicit RequestedTheme. This is more reliable than ActualTheme
    /// during reconciliation, because ActualTheme is a dependency property that
    /// may not have propagated yet within the same synchronous update pass.
    /// </summary>
    private static string GetEffectiveThemeName(FrameworkElement fe)
    {
        // #85: the effective theme depends on fe + its ancestors' RequestedTheme
        // / ActualTheme. Within a single reconcile pass these are fixed once fe is
        // reached (ancestors are applied top-down first), so cache the walked name
        // per element for the duration of the pass and reuse it across the dozens
        // of token resolves on this element. A new pass (BeginReconcilePass —
        // bumped by the host once per render, and by InvalidateCache on a theme
        // change) forces a re-walk, so an ancestor RequestedTheme flip is
        // reflected on the very next render, matching the original always-walk
        // behaviour exactly. The cheap per-element RequestedTheme / ActualTheme
        // comparison additionally catches an app-level theme flip that propagates
        // to this element mid-pass.
        int pass = Volatile.Read(ref _reconcilePass);
        var requested = fe.RequestedTheme;
        var actual = fe.ActualTheme;
        var box = _themeNameCache.GetValue(fe, static _ => new ThemeNameBox());
        if (box.Pass == pass
            && box.Name is not null
            && box.RequestedTheme == requested
            && box.ActualTheme == actual)
            return box.Name;

        var name = ComputeEffectiveThemeName(fe);
        box.Name = name;
        box.Pass = pass;
        box.RequestedTheme = requested;
        box.ActualTheme = actual;
        return name;
    }

    private static string ComputeEffectiveThemeName(FrameworkElement fe)
    {
        // Check the element's own RequestedTheme first
        if (fe.RequestedTheme != ElementTheme.Default)
            return fe.RequestedTheme == ElementTheme.Dark ? "Dark" : "Light";

        // Walk up the visual tree for the nearest override
        var parent = VisualTreeHelper.GetParent(fe) as FrameworkElement;
        while (parent is not null)
        {
            if (parent.RequestedTheme != ElementTheme.Default)
                return parent.RequestedTheme == ElementTheme.Dark ? "Dark" : "Light";
            parent = VisualTreeHelper.GetParent(parent) as FrameworkElement;
        }

        // No override found — check ActualTheme (reliable for elements already in the tree)
        if (fe.ActualTheme != ElementTheme.Default)
            return fe.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";

        // Final fallback: application theme
        return Application.Current?.RequestedTheme == ApplicationTheme.Dark ? "Dark" : "Light";
    }

    private static bool TryResolveFromThemeDictionaries(
        ResourceDictionary resources, string key, string themeName, out Brush? brush)
    {
        // Check this dictionary's ThemeDictionaries
        if (resources.ThemeDictionaries.TryGetValue(themeName, out var themeObj)
            && themeObj is ResourceDictionary themeDict
            && themeDict.TryGetValue(key, out var themed)
            && themed is Brush themedBrush)
        {
            brush = themedBrush;
            return true;
        }

        // Check MergedDictionaries (XamlControlsResources is added here)
        foreach (var merged in resources.MergedDictionaries)
        {
            if (TryResolveFromThemeDictionaries(merged, key, themeName, out brush))
                return true;
        }

        brush = null;
        return false;
    }

    private static bool TryResolveNonThemed(ResourceDictionary resources, string key, out Brush? brush)
    {
        if (resources.TryGetValue(key, out var value) && value is Brush b)
        {
            brush = b;
            return true;
        }

        foreach (var merged in resources.MergedDictionaries)
        {
            if (TryResolveNonThemed(merged, key, out brush))
                return true;
        }

        brush = null;
        return false;
    }
}

/// <summary>
/// Provides semantic theme tokens and custom resource references.
/// All tokens resolve from WinUI's resource system and automatically
/// adapt when the theme changes (Light ↔ Dark).
/// <para>
/// Usage: <c>Button("Go").Background(Theme.Accent)</c>
/// </para>
/// </summary>
public static class Theme
{
    // ── Accent / Fill ────────────────────────────────────────────────
    public static ThemeRef Accent            => new("AccentFillColorDefaultBrush");
    public static ThemeRef AccentSecondary   => new("AccentFillColorSecondaryBrush");
    public static ThemeRef AccentTertiary    => new("AccentFillColorTertiaryBrush");
    public static ThemeRef AccentDisabled    => new("AccentFillColorDisabledBrush");

    // ── Text ─────────────────────────────────────────────────────────
    public static ThemeRef PrimaryText       => new("TextFillColorPrimaryBrush");
    public static ThemeRef SecondaryText     => new("TextFillColorSecondaryBrush");
    public static ThemeRef TertiaryText      => new("TextFillColorTertiaryBrush");
    public static ThemeRef DisabledText      => new("TextFillColorDisabledBrush");
    public static ThemeRef AccentText        => new("AccentTextFillColorPrimaryBrush");

    // ── Surfaces / Fill ──────────────────────────────────────────────
    public static ThemeRef SolidBackground   => new("SolidBackgroundFillColorBaseBrush");
    public static ThemeRef CardBackground    => new("CardBackgroundFillColorDefaultBrush");
    public static ThemeRef SmokeFill         => new("SmokeFillColorDefaultBrush");
    public static ThemeRef SubtleFill        => new("SubtleFillColorSecondaryBrush");
    public static ThemeRef LayerFill         => new("LayerFillColorDefaultBrush");

    // ── Control Fill ─────────────────────────────────────────────────
    public static ThemeRef ControlFill              => new("ControlFillColorDefaultBrush");
    public static ThemeRef ControlFillSecondary     => new("ControlFillColorSecondaryBrush");
    public static ThemeRef ControlFillTertiary      => new("ControlFillColorTertiaryBrush");
    public static ThemeRef ControlFillDisabled      => new("ControlFillColorDisabledBrush");
    public static ThemeRef ControlFillInputActive   => new("ControlFillColorInputActiveBrush");

    // ── Stroke / Border ──────────────────────────────────────────────
    public static ThemeRef CardStroke        => new("CardStrokeColorDefaultBrush");
    public static ThemeRef SurfaceStroke     => new("SurfaceStrokeColorDefaultBrush");
    public static ThemeRef DividerStroke     => new("DividerStrokeColorDefaultBrush");
    public static ThemeRef ControlStroke     => new("ControlStrokeColorDefaultBrush");
    public static ThemeRef ControlStrokeSecondary => new("ControlStrokeColorSecondaryBrush");

    // ── Signal ───────────────────────────────────────────────────────
    public static ThemeRef SystemAttention   => new("SystemFillColorAttentionBrush");
    public static ThemeRef SystemSuccess     => new("SystemFillColorSuccessBrush");
    public static ThemeRef SystemCaution     => new("SystemFillColorCautionBrush");
    public static ThemeRef SystemCritical    => new("SystemFillColorCriticalBrush");
    public static ThemeRef SystemNeutral     => new("SystemFillColorNeutralBrush");
    public static ThemeRef SystemSolidNeutral => new("SystemFillColorSolidNeutralBrush");

    public static ThemeRef SystemAttentionBackground => new("SystemFillColorAttentionBackgroundBrush");
    public static ThemeRef SystemSuccessBackground   => new("SystemFillColorSuccessBackgroundBrush");
    public static ThemeRef SystemCautionBackground   => new("SystemFillColorCautionBackgroundBrush");
    public static ThemeRef SystemCriticalBackground  => new("SystemFillColorCriticalBackgroundBrush");
    public static ThemeRef SystemNeutralBackground   => new("SystemFillColorNeutralBackgroundBrush");
    public static ThemeRef SystemSolidAttention       => new("SystemFillColorSolidAttentionBackgroundBrush");

    // ── Custom resource reference ────────────────────────────────────
    /// <summary>
    /// Reference any WinUI theme resource by key name.
    /// The resource must exist in the WinUI resource tree
    /// (e.g., defined in XamlControlsResources or added via app resources).
    /// </summary>
    public static ThemeRef Ref(string resourceKey) => new(resourceKey);
}
