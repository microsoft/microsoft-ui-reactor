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
        return ResolveForTheme(resourceKey, isDark ? "Dark" : "Light");
    }

    // Issue #660 (#86): cache the (resourceKey, themeName) -> Brush resolution.
    // TryResolveFromThemeDictionaries recurses Application.Resources.MergedDictionaries
    // (XamlControlsResources and its nested dictionaries) on EVERY ThemeRef.Resolve —
    // i.e. per cell x per color token x per render on the data-grid workload. The
    // resolved brush for a given (key, theme) is deterministic and stable until the
    // theme/palette changes, so cache it and clear the cache on a theme change
    // (InvalidateResolutionCache, wired from the hosts' theme-change handlers).
    // The cached value mirrors the uncached return EXACTLY, including a null result
    // for an unknown key (so missing keys aren't re-walked). Brushes resolved from a
    // ThemeDictionary are shared instances today, so returning the cached reference
    // is identical to the prior behavior.
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<(string Key, string Theme), Brush?> s_resolutionCache = new();

    /// <summary>
    /// Drops every cached <see cref="ResolveForTheme"/> result. Called by the host
    /// when the effective theme or the system palette changes so the next resolve
    /// re-reads the (now-updated) ThemeDictionaries.
    /// </summary>
    internal static void InvalidateResolutionCache() => s_resolutionCache.Clear();

    private static Brush? ResolveForTheme(string resourceKey, string themeName)
    {
        var resources = Application.Current?.Resources;
        // Don't consult or populate the cache while resources are transiently
        // unavailable (very early startup) — caching a null here would be stale
        // once XamlControlsResources loads. A theme change later clears the cache
        // anyway, but this avoids a poisoned entry in the first place.
        if (resources is null) return null;

        var cacheKey = (resourceKey, themeName);
        if (s_resolutionCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var resolved = ResolveForThemeUncached(resources, resourceKey, themeName);
        // Issue #660 review: cache ONLY a successful (non-null) resolution. A null
        // means "key not found right now" — caching it would go stale if a
        // ResourceDictionary defining that key is merged in after this first
        // resolve (a dictionary add fires no theme-change event, so the cache
        // wouldn't be cleared). Unknown keys are rare and re-walking them is the
        // same cost as before this cache existed, so this is strictly safe.
        if (resolved is not null)
            s_resolutionCache[cacheKey] = resolved;
        return resolved;
    }

    private static Brush? ResolveForThemeUncached(ResourceDictionary resources, string resourceKey, string themeName)
    {
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
