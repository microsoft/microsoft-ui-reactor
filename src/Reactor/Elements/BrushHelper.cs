using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Color and brush parsing utilities.
/// Supports named colors, hex (#RRGGBB, #AARRGGBB), and direct Color values.
/// Colors are cached by string, and the resulting <see cref="SolidColorBrush"/>
/// is cached per-thread keyed by ARGB so hot fluent chains
/// (<c>.Foreground("#color")</c>, <c>.Background("#color")</c>) reuse one brush
/// instance per color instead of allocating a WinRT DependencyObject per cell
/// per render. The cache is <see cref="ThreadStaticAttribute">thread-static</see>
/// so a brush is only ever handed back on the same thread that created it,
/// honoring DependencyObject thread affinity.
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
    /// Parses a color string into a SolidColorBrush.
    /// Supports named colors (red, green, blue, white, black, gray, lightgray, transparent)
    /// and hex codes (#RRGGBB or #AARRGGBB).
    /// Both the parsed color and the resulting brush are cached, so repeated calls
    /// with the same color on the same thread return the same brush instance.
    /// </summary>
    public static SolidColorBrush Parse(string color)
    {
        var parsed = _colorCache.GetOrAdd(color, static c =>
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
        return GetBrush(parsed);
    }

    /// <summary>
    /// Returns a cached <see cref="SolidColorBrush"/> for the given color, creating
    /// and caching one on first use for the current thread. The brush must be
    /// treated as immutable by callers (Reactor never mutates modifier brushes).
    /// </summary>
    public static SolidColorBrush GetBrush(global::Windows.UI.Color color)
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
