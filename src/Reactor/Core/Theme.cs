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
        return ResolveForTheme(resourceKey, isDark ? "Dark" : "Light");
    }

    // ── Resolution caches (perf #85/#86) ─────────────────────────────
    // The data-grid stress path resolves dozens of theme tokens per cell per
    // render. Both the per-element effective-theme tree walk (#85) and the
    // recursive MergedDictionaries scan (#86) were uncached and re-run on every
    // ThemeRef.Resolve. These caches collapse that to one tree walk per element
    // per theme generation and one dictionary scan per (key, theme) pair.

    // #86: resolved brush per (resourceKey, themeName). The themeName is part of
    // the key, so Light/Dark entries coexist; cleared on theme/colour change so
    // a runtime theme-dictionary swap is observed. Only non-null hits are
    // cached, so a genuinely-absent key is re-resolved (and picked up if added
    // later) exactly as before.
    private static readonly ConcurrentDictionary<(string Key, string Theme), Brush> _brushCache = new();

    // #85: effective theme name per element, validated by a global generation
    // stamp. A theme change bumps the generation (see InvalidateCache), so every
    // element's cached name is lazily recomputed on its next resolve.
    private sealed class ThemeNameBox { public int Generation = -1; public string? Name; }
    private static readonly ConditionalWeakTable<FrameworkElement, ThemeNameBox> _themeNameCache = new();
    private static int _themeGeneration;

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
        _brushCache.Clear();
    }

    private static Brush? ResolveForTheme(string resourceKey, string themeName)
    {
        var cacheKey = (resourceKey, themeName);
        if (_brushCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var resolved = ResolveForThemeUncached(resourceKey, themeName);
        // Only cache successful resolves so an absent key stays re-resolvable
        // (matches the pre-cache behaviour where each call re-scanned).
        if (resolved is not null)
            _brushCache[cacheKey] = resolved;
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
        // / ActualTheme, which only change on a theme switch. Cache it per
        // element and validate against the global generation stamp so repeated
        // resolves (per colour token, per render) reuse one tree walk until the
        // next theme change bumps the generation.
        int gen = Volatile.Read(ref _themeGeneration);
        var box = _themeNameCache.GetValue(fe, static _ => new ThemeNameBox());
        if (box.Generation == gen && box.Name is not null)
            return box.Name;

        var name = ComputeEffectiveThemeName(fe);
        box.Name = name;
        box.Generation = gen;
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
