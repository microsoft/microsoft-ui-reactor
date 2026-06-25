using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Color and brush parsing utilities.
/// Supports named colors, hex (#RRGGBB, #AARRGGBB), and direct Color values.
/// Parsed colors are cached by string. The public <see cref="Parse(string)"/>
/// returns a fresh, caller-owned brush (unchanged historical behavior — safe to
/// mutate). Reactor's own hot fluent chains (<c>.Foreground("#color")</c>,
/// <c>.Background("#color")</c>) instead route through an internal shared-cache
/// path (<c>ParseShared</c>) so thousands of identical cells reuse one brush
/// instead of allocating a WinRT DependencyObject per cell per render. That
/// brush cache is <see cref="ThreadStaticAttribute">thread-static</see> so a
/// brush is only ever handed back on the same thread that created it, honoring
/// DependencyObject thread affinity.
/// </summary>
public static class BrushHelper
{
    private static readonly ConcurrentDictionary<string, global::Windows.UI.Color> _colorCache = new();

    // #168 — per-thread brush cache. SolidColorBrush is a DependencyObject with
    // thread affinity, so the cache is thread-static: each thread that parses a
    // color gets (and reuses) its own brush instance for that ARGB value. Sharing
    // an immutable brush across many cells is safe — Reactor treats modifier
    // brushes as read-only and compares them structurally (Color/Opacity).
    [ThreadStatic]
    private static Dictionary<global::Windows.UI.Color, SolidColorBrush>? _brushCache;

    /// <summary>
    /// Parses a color string into a <b>fresh</b> SolidColorBrush owned by the caller.
    /// Supports named colors (red, green, blue, white, black, gray, lightgray, transparent)
    /// and hex codes (#RRGGBB or #AARRGGBB).
    /// The parsed color is cached, but a new brush instance is returned on every call,
    /// so callers may safely mutate it. Reactor's internal fluent modifiers use a
    /// shared-cache path (<c>ParseShared</c>) instead, since they treat the brush
    /// as immutable.
    /// </summary>
    public static SolidColorBrush Parse(string color) => new(ParseColor(color));

    /// <summary>
    /// Parses a color string and returns the <b>shared</b>, read-only brush for it
    /// from the per-thread ARGB cache. Internal hot-path equivalent of
    /// <see cref="Parse(string)"/> for Reactor's own fluent modifiers, which never
    /// mutate the brush. Callers must treat the result as immutable.
    /// </summary>
    internal static SolidColorBrush ParseShared(string color) => GetBrush(ParseColor(color));

    /// <summary>
    /// Parses a color string into a <see cref="global::Windows.UI.Color"/>.
    /// The result is cached by string so repeated parses are allocation-free.
    /// </summary>
    internal static global::Windows.UI.Color ParseColor(string color) =>
        _colorCache.GetOrAdd(color, static c =>
            c.ToLowerInvariant() switch
            {
                "red" => global::Windows.UI.Color.FromArgb(255, 255, 0, 0),
                "green" => global::Windows.UI.Color.FromArgb(255, 0, 128, 0),
                "blue" => global::Windows.UI.Color.FromArgb(255, 0, 0, 255),
                "white" => global::Windows.UI.Color.FromArgb(255, 255, 255, 255),
                "black" => global::Windows.UI.Color.FromArgb(255, 0, 0, 0),
                "gray" or "grey" => global::Windows.UI.Color.FromArgb(255, 128, 128, 128),
                "lightgray" or "lightgrey" => global::Windows.UI.Color.FromArgb(255, 211, 211, 211),
                "transparent" => global::Windows.UI.Color.FromArgb(0, 0, 0, 0),
                _ when c.StartsWith('#') => ParseHex(c),
                _ => global::Windows.UI.Color.FromArgb(255, 128, 128, 128),
            });

    /// <summary>
    /// Returns a cached <see cref="SolidColorBrush"/> for the given color, creating
    /// and caching one on first use for the current thread. The brush must be
    /// treated as immutable by callers (Reactor never mutates modifier brushes).
    /// Internal: this is the shared-cache primitive behind <c>ParseShared</c>;
    /// public callers that want a color brush use <see cref="Parse(string)"/>,
    /// which returns a fresh, caller-owned (mutable) instance.
    /// </summary>
    internal static SolidColorBrush GetBrush(global::Windows.UI.Color color)
    {
        var cache = _brushCache ??= new Dictionary<global::Windows.UI.Color, SolidColorBrush>();
        if (!cache.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            cache[color] = brush;
        }
        return brush;
    }

    internal static global::Windows.UI.Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6
            && byte.TryParse(hex[0..2], global::System.Globalization.NumberStyles.HexNumber, null, out var r6)
            && byte.TryParse(hex[2..4], global::System.Globalization.NumberStyles.HexNumber, null, out var g6)
            && byte.TryParse(hex[4..6], global::System.Globalization.NumberStyles.HexNumber, null, out var b6))
        {
            return global::Windows.UI.Color.FromArgb(255, r6, g6, b6);
        }
        if (hex.Length == 8
            && byte.TryParse(hex[0..2], global::System.Globalization.NumberStyles.HexNumber, null, out var a8)
            && byte.TryParse(hex[2..4], global::System.Globalization.NumberStyles.HexNumber, null, out var r8)
            && byte.TryParse(hex[4..6], global::System.Globalization.NumberStyles.HexNumber, null, out var g8)
            && byte.TryParse(hex[6..8], global::System.Globalization.NumberStyles.HexNumber, null, out var b8))
        {
            return global::Windows.UI.Color.FromArgb(a8, r8, g8, b8);
        }
        // Fallback to gray for malformed hex, consistent with named color fallback
        return global::Windows.UI.Color.FromArgb(255, 128, 128, 128);
    }
}
