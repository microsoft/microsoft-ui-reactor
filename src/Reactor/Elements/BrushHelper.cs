using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Color and brush parsing utilities.
/// Supports named colors, hex (#RRGGBB, #AARRGGBB), and direct Color values.
/// Parsed colors are cached by string (an immutable value type, safe to share),
/// but a fresh <see cref="SolidColorBrush"/> is created per call: a brush is a
/// DependencyObject with thread affinity and mutable Color/Opacity, so a single
/// instance cannot be safely shared across controls.
/// </summary>
public static class BrushHelper
{
    // OrdinalIgnoreCase so equivalent colors that differ only in casing
    // ("Red"/"red", "#FF0000"/"#ff0000") share one cache entry instead of
    // creating duplicates. Safe because ParseColor lowercases named colors
    // before matching and ParseHex is case-insensitive, so case-folded keying
    // yields identical results to the prior case-sensitive keying.
    private static readonly ConcurrentDictionary<string, global::Windows.UI.Color> _colorCache =
        new(global::System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses a color string into a fresh <see cref="SolidColorBrush"/> owned by the
    /// caller. Supports named colors (red, green, blue, white, black, gray, lightgray,
    /// transparent) and hex codes (#RRGGBB or #AARRGGBB). The parsed color is cached,
    /// but a new brush instance is returned on every call, so callers may safely mutate
    /// it and a brush is never shared between controls. Any other string silently
    /// yields a gray brush; the REACTOR_DYM_006 analyzer flags constant strings that do.
    /// </summary>
    public static SolidColorBrush Parse(string color) => new(ParseColor(color));

    /// <summary>
    /// Parses a color string into a <see cref="global::Windows.UI.Color"/>.
    /// The result is cached by string so repeated parses are allocation-free.
    /// </summary>
    internal static global::Windows.UI.Color ParseColor(string color) =>
        _colorCache.GetOrAdd(color, static c =>
            TryParseColor(c, out var parsed) ? parsed : FallbackGray);

    /// <summary>
    /// The color every unrecognized string silently falls back to.
    /// </summary>
    internal static readonly global::Windows.UI.Color FallbackGray = global::Windows.UI.Color.FromArgb(255, 128, 128, 128);

    /// <summary>
    /// Parses a color string without the gray fallback, so callers can tell a genuine
    /// <c>"gray"</c> from an unrecognized string. Uncached.
    /// </summary>
    /// <remarks>
    /// The REACTOR_DYM_006 analyzer (<c>ColorStringClassifier</c> in Reactor.Analyzers) and
    /// the <c>mur check</c> <c>ColorStringLiteralRule</c> carry byte-for-byte copies of this
    /// grammar; <c>ColorStringParityTests</c> locks all three together.
    /// </remarks>
    internal static bool TryParseColor(string color, out global::Windows.UI.Color parsed)
    {
        switch (color.ToLowerInvariant())
        {
            case "red": parsed = global::Windows.UI.Color.FromArgb(255, 255, 0, 0); return true;
            case "green": parsed = global::Windows.UI.Color.FromArgb(255, 0, 128, 0); return true;
            case "blue": parsed = global::Windows.UI.Color.FromArgb(255, 0, 0, 255); return true;
            case "white": parsed = global::Windows.UI.Color.FromArgb(255, 255, 255, 255); return true;
            case "black": parsed = global::Windows.UI.Color.FromArgb(255, 0, 0, 0); return true;
            case "gray" or "grey": parsed = global::Windows.UI.Color.FromArgb(255, 128, 128, 128); return true;
            case "lightgray" or "lightgrey": parsed = global::Windows.UI.Color.FromArgb(255, 211, 211, 211); return true;
            case "transparent": parsed = global::Windows.UI.Color.FromArgb(0, 0, 0, 0); return true;
        }
        if (color.StartsWith('#'))
            return TryParseHex(color, out parsed);
        parsed = FallbackGray;
        return false;
    }

    internal static global::Windows.UI.Color ParseHex(string hex) =>
        // Fallback to gray for malformed hex, consistent with named color fallback
        TryParseHex(hex, out var parsed) ? parsed : FallbackGray;

    private static bool TryParseHex(string hex, out global::Windows.UI.Color parsed)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6
            && byte.TryParse(hex[0..2], global::System.Globalization.NumberStyles.HexNumber, null, out var r6)
            && byte.TryParse(hex[2..4], global::System.Globalization.NumberStyles.HexNumber, null, out var g6)
            && byte.TryParse(hex[4..6], global::System.Globalization.NumberStyles.HexNumber, null, out var b6))
        {
            parsed = global::Windows.UI.Color.FromArgb(255, r6, g6, b6);
            return true;
        }
        if (hex.Length == 8
            && byte.TryParse(hex[0..2], global::System.Globalization.NumberStyles.HexNumber, null, out var a8)
            && byte.TryParse(hex[2..4], global::System.Globalization.NumberStyles.HexNumber, null, out var r8)
            && byte.TryParse(hex[4..6], global::System.Globalization.NumberStyles.HexNumber, null, out var g8)
            && byte.TryParse(hex[6..8], global::System.Globalization.NumberStyles.HexNumber, null, out var b8))
        {
            parsed = global::Windows.UI.Color.FromArgb(a8, r8, g8, b8);
            return true;
        }
        parsed = FallbackGray;
        return false;
    }
}
