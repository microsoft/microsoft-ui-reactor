using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>What is wrong with a color string that <c>BrushHelper</c> cannot parse.</summary>
internal enum ColorStringIssue
{
    /// <summary>A font name passed to <c>ResourceBuilder.Set</c> under a <c>*FontFamily</c> key.</summary>
    FontFamilyKey,
    /// <summary><c>#RGB</c> / <c>#ARGB</c> shorthand, which BrushHelper does not expand.</summary>
    HexShorthand,
    /// <summary>Six or eight hex digits without the leading <c>#</c>.</summary>
    MissingHash,
    /// <summary>Starts with <c>#</c> but is not <c>#RRGGBB</c> / <c>#AARRGGBB</c>.</summary>
    MalformedHex,
    /// <summary>A standard CSS / WinUI named color that BrushHelper's 8-name table lacks.</summary>
    NamedColor,
    /// <summary>Anything else.</summary>
    Unknown,
}

/// <summary>
/// The did-you-mean for one unparseable color string. <see cref="Replacement"/> is the new string
/// literal value for the literal fixes, the family name for <see cref="ColorStringIssue.FontFamilyKey"/>
/// (only when the <c>FontFamily</c> overload exists), or <see langword="null"/> when there is no fix.
/// </summary>
internal readonly struct ColorStringFinding
{
    public ColorStringFinding(ColorStringIssue issue, string? replacement, string detail)
    {
        Issue = issue;
        Replacement = replacement;
        Detail = detail;
    }

    public ColorStringIssue Issue { get; }
    public string? Replacement { get; }
    public string Detail { get; }
}

/// <summary>
/// Classifies color strings for REACTOR_DYM_006 (<see cref="ColorStringLiteralAnalyzer"/>).
/// </summary>
/// <remarks>
/// <see cref="TryParse"/> is a <b>netstandard2.0 copy</b> of <c>Microsoft.UI.Reactor.BrushHelper.TryParseColor</c>
/// (the analyzer cannot reference <c>src/Reactor</c>); <c>ColorStringParityTests</c> locks the two
/// together — <b>edit them in lockstep</b>. Every fixable <see cref="ColorStringFinding.Detail"/> ends in
/// <c>did you mean X?</c>: the <c>mur check</c> <c>ColorStringLiteralRule</c> lifts <c>X</c> out of the
/// diagnostic message, so keep that suffix stable.
/// </remarks>
internal static class ColorStringClassifier
{
    /// <summary>The names BrushHelper's switch recognizes (case-insensitive).</summary>
    public const string SupportedNames = "red, green, blue, white, black, gray/grey, lightgray/lightgrey, transparent";

    /// <summary>The suffix that marks a WinUI FontFamily resource key (e.g. <c>ContentControlThemeFontFamily</c>).</summary>
    public const string FontFamilyKeySuffix = "FontFamily";

    /// <summary>
    /// Standard CSS / WinUI (<c>Microsoft.UI.Colors</c>) named colors that BrushHelper does <em>not</em>
    /// recognize, mapped to their exact <c>#RRGGBB</c>. The 8 names BrushHelper does support are excluded.
    /// </summary>
    public static readonly ImmutableDictionary<string, string> NamedColorHex =
        ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new[]
        {
            new KeyValuePair<string, string>("aliceblue", "#F0F8FF"),
            new KeyValuePair<string, string>("antiquewhite", "#FAEBD7"),
            new KeyValuePair<string, string>("aqua", "#00FFFF"),
            new KeyValuePair<string, string>("aquamarine", "#7FFFD4"),
            new KeyValuePair<string, string>("azure", "#F0FFFF"),
            new KeyValuePair<string, string>("beige", "#F5F5DC"),
            new KeyValuePair<string, string>("bisque", "#FFE4C4"),
            new KeyValuePair<string, string>("blanchedalmond", "#FFEBCD"),
            new KeyValuePair<string, string>("blueviolet", "#8A2BE2"),
            new KeyValuePair<string, string>("brown", "#A52A2A"),
            new KeyValuePair<string, string>("burlywood", "#DEB887"),
            new KeyValuePair<string, string>("cadetblue", "#5F9EA0"),
            new KeyValuePair<string, string>("chartreuse", "#7FFF00"),
            new KeyValuePair<string, string>("chocolate", "#D2691E"),
            new KeyValuePair<string, string>("coral", "#FF7F50"),
            new KeyValuePair<string, string>("cornflowerblue", "#6495ED"),
            new KeyValuePair<string, string>("cornsilk", "#FFF8DC"),
            new KeyValuePair<string, string>("crimson", "#DC143C"),
            new KeyValuePair<string, string>("cyan", "#00FFFF"),
            new KeyValuePair<string, string>("darkblue", "#00008B"),
            new KeyValuePair<string, string>("darkcyan", "#008B8B"),
            new KeyValuePair<string, string>("darkgoldenrod", "#B8860B"),
            new KeyValuePair<string, string>("darkgray", "#A9A9A9"),
            new KeyValuePair<string, string>("darkgreen", "#006400"),
            new KeyValuePair<string, string>("darkgrey", "#A9A9A9"),
            new KeyValuePair<string, string>("darkkhaki", "#BDB76B"),
            new KeyValuePair<string, string>("darkmagenta", "#8B008B"),
            new KeyValuePair<string, string>("darkolivegreen", "#556B2F"),
            new KeyValuePair<string, string>("darkorange", "#FF8C00"),
            new KeyValuePair<string, string>("darkorchid", "#9932CC"),
            new KeyValuePair<string, string>("darkred", "#8B0000"),
            new KeyValuePair<string, string>("darksalmon", "#E9967A"),
            new KeyValuePair<string, string>("darkseagreen", "#8FBC8F"),
            new KeyValuePair<string, string>("darkslateblue", "#483D8B"),
            new KeyValuePair<string, string>("darkslategray", "#2F4F4F"),
            new KeyValuePair<string, string>("darkslategrey", "#2F4F4F"),
            new KeyValuePair<string, string>("darkturquoise", "#00CED1"),
            new KeyValuePair<string, string>("darkviolet", "#9400D3"),
            new KeyValuePair<string, string>("deeppink", "#FF1493"),
            new KeyValuePair<string, string>("deepskyblue", "#00BFFF"),
            new KeyValuePair<string, string>("dimgray", "#696969"),
            new KeyValuePair<string, string>("dimgrey", "#696969"),
            new KeyValuePair<string, string>("dodgerblue", "#1E90FF"),
            new KeyValuePair<string, string>("firebrick", "#B22222"),
            new KeyValuePair<string, string>("floralwhite", "#FFFAF0"),
            new KeyValuePair<string, string>("forestgreen", "#228B22"),
            new KeyValuePair<string, string>("fuchsia", "#FF00FF"),
            new KeyValuePair<string, string>("gainsboro", "#DCDCDC"),
            new KeyValuePair<string, string>("ghostwhite", "#F8F8FF"),
            new KeyValuePair<string, string>("gold", "#FFD700"),
            new KeyValuePair<string, string>("goldenrod", "#DAA520"),
            new KeyValuePair<string, string>("greenyellow", "#ADFF2F"),
            new KeyValuePair<string, string>("honeydew", "#F0FFF0"),
            new KeyValuePair<string, string>("hotpink", "#FF69B4"),
            new KeyValuePair<string, string>("indianred", "#CD5C5C"),
            new KeyValuePair<string, string>("indigo", "#4B0082"),
            new KeyValuePair<string, string>("ivory", "#FFFFF0"),
            new KeyValuePair<string, string>("khaki", "#F0E68C"),
            new KeyValuePair<string, string>("lavender", "#E6E6FA"),
            new KeyValuePair<string, string>("lavenderblush", "#FFF0F5"),
            new KeyValuePair<string, string>("lawngreen", "#7CFC00"),
            new KeyValuePair<string, string>("lemonchiffon", "#FFFACD"),
            new KeyValuePair<string, string>("lightblue", "#ADD8E6"),
            new KeyValuePair<string, string>("lightcoral", "#F08080"),
            new KeyValuePair<string, string>("lightcyan", "#E0FFFF"),
            new KeyValuePair<string, string>("lightgoldenrodyellow", "#FAFAD2"),
            new KeyValuePair<string, string>("lightgreen", "#90EE90"),
            new KeyValuePair<string, string>("lightpink", "#FFB6C1"),
            new KeyValuePair<string, string>("lightsalmon", "#FFA07A"),
            new KeyValuePair<string, string>("lightseagreen", "#20B2AA"),
            new KeyValuePair<string, string>("lightskyblue", "#87CEFA"),
            new KeyValuePair<string, string>("lightslategray", "#778899"),
            new KeyValuePair<string, string>("lightslategrey", "#778899"),
            new KeyValuePair<string, string>("lightsteelblue", "#B0C4DE"),
            new KeyValuePair<string, string>("lightyellow", "#FFFFE0"),
            new KeyValuePair<string, string>("lime", "#00FF00"),
            new KeyValuePair<string, string>("limegreen", "#32CD32"),
            new KeyValuePair<string, string>("linen", "#FAF0E6"),
            new KeyValuePair<string, string>("magenta", "#FF00FF"),
            new KeyValuePair<string, string>("maroon", "#800000"),
            new KeyValuePair<string, string>("mediumaquamarine", "#66CDAA"),
            new KeyValuePair<string, string>("mediumblue", "#0000CD"),
            new KeyValuePair<string, string>("mediumorchid", "#BA55D3"),
            new KeyValuePair<string, string>("mediumpurple", "#9370DB"),
            new KeyValuePair<string, string>("mediumseagreen", "#3CB371"),
            new KeyValuePair<string, string>("mediumslateblue", "#7B68EE"),
            new KeyValuePair<string, string>("mediumspringgreen", "#00FA9A"),
            new KeyValuePair<string, string>("mediumturquoise", "#48D1CC"),
            new KeyValuePair<string, string>("mediumvioletred", "#C71585"),
            new KeyValuePair<string, string>("midnightblue", "#191970"),
            new KeyValuePair<string, string>("mintcream", "#F5FFFA"),
            new KeyValuePair<string, string>("mistyrose", "#FFE4E1"),
            new KeyValuePair<string, string>("moccasin", "#FFE4B5"),
            new KeyValuePair<string, string>("navajowhite", "#FFDEAD"),
            new KeyValuePair<string, string>("navy", "#000080"),
            new KeyValuePair<string, string>("oldlace", "#FDF5E6"),
            new KeyValuePair<string, string>("olive", "#808000"),
            new KeyValuePair<string, string>("olivedrab", "#6B8E23"),
            new KeyValuePair<string, string>("orange", "#FFA500"),
            new KeyValuePair<string, string>("orangered", "#FF4500"),
            new KeyValuePair<string, string>("orchid", "#DA70D6"),
            new KeyValuePair<string, string>("palegoldenrod", "#EEE8AA"),
            new KeyValuePair<string, string>("palegreen", "#98FB98"),
            new KeyValuePair<string, string>("paleturquoise", "#AFEEEE"),
            new KeyValuePair<string, string>("palevioletred", "#DB7093"),
            new KeyValuePair<string, string>("papayawhip", "#FFEFD5"),
            new KeyValuePair<string, string>("peachpuff", "#FFDAB9"),
            new KeyValuePair<string, string>("peru", "#CD853F"),
            new KeyValuePair<string, string>("pink", "#FFC0CB"),
            new KeyValuePair<string, string>("plum", "#DDA0DD"),
            new KeyValuePair<string, string>("powderblue", "#B0E0E6"),
            new KeyValuePair<string, string>("purple", "#800080"),
            new KeyValuePair<string, string>("rebeccapurple", "#663399"),
            new KeyValuePair<string, string>("rosybrown", "#BC8F8F"),
            new KeyValuePair<string, string>("royalblue", "#4169E1"),
            new KeyValuePair<string, string>("saddlebrown", "#8B4513"),
            new KeyValuePair<string, string>("salmon", "#FA8072"),
            new KeyValuePair<string, string>("sandybrown", "#F4A460"),
            new KeyValuePair<string, string>("seagreen", "#2E8B57"),
            new KeyValuePair<string, string>("seashell", "#FFF5EE"),
            new KeyValuePair<string, string>("sienna", "#A0522D"),
            new KeyValuePair<string, string>("silver", "#C0C0C0"),
            new KeyValuePair<string, string>("skyblue", "#87CEEB"),
            new KeyValuePair<string, string>("slateblue", "#6A5ACD"),
            new KeyValuePair<string, string>("slategray", "#708090"),
            new KeyValuePair<string, string>("slategrey", "#708090"),
            new KeyValuePair<string, string>("snow", "#FFFAFA"),
            new KeyValuePair<string, string>("springgreen", "#00FF7F"),
            new KeyValuePair<string, string>("steelblue", "#4682B4"),
            new KeyValuePair<string, string>("tan", "#D2B48C"),
            new KeyValuePair<string, string>("teal", "#008080"),
            new KeyValuePair<string, string>("thistle", "#D8BFD8"),
            new KeyValuePair<string, string>("tomato", "#FF6347"),
            new KeyValuePair<string, string>("turquoise", "#40E0D0"),
            new KeyValuePair<string, string>("violet", "#EE82EE"),
            new KeyValuePair<string, string>("wheat", "#F5DEB3"),
            new KeyValuePair<string, string>("whitesmoke", "#F5F5F5"),
            new KeyValuePair<string, string>("yellow", "#FFFF00"),
            new KeyValuePair<string, string>("yellowgreen", "#9ACD32"),
        });

    /// <summary>
    /// Mirrors <c>BrushHelper.TryParseColor</c>: <see langword="true"/> iff the string does not fall
    /// back to gray.
    /// </summary>
    public static bool TryParse(string color)
    {
        switch (color.ToLowerInvariant())
        {
            case "red":
            case "green":
            case "blue":
            case "white":
            case "black":
            case "gray":
            case "grey":
            case "lightgray":
            case "lightgrey":
            case "transparent":
                return true;
        }
        return color.StartsWith("#", StringComparison.Ordinal) && TryParseHex(color);
    }

    private static bool TryParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            return IsByte(hex, 0) && IsByte(hex, 2) && IsByte(hex, 4);
        if (hex.Length == 8)
            return IsByte(hex, 0) && IsByte(hex, 2) && IsByte(hex, 4) && IsByte(hex, 6);
        return false;
    }

    // Same call as BrushHelper (`byte.TryParse(hex[i..i+2], NumberStyles.HexNumber, null, out _)`),
    // including HexNumber's leading/trailing-whitespace allowance.
    private static bool IsByte(string hex, int start) =>
        byte.TryParse(hex.Substring(start, 2), NumberStyles.HexNumber, null, out _);

    /// <summary>
    /// Returns the finding for <paramref name="value"/>, or <see langword="null"/> when BrushHelper
    /// parses it. <paramref name="resourceKey"/> is the constant key of a <c>ResourceBuilder.Set</c>
    /// call (otherwise <see langword="null"/>); <paramref name="fontFamilyOverloadExists"/> says whether
    /// <c>ResourceBuilder.Set(string, FontFamily)</c> is available to rewrite to; <paramref name="fontFamilyArgumentName"/>
    /// is that overload's parameter name when the flagged argument is named (<c>color: "…"</c>) and must be renamed.
    /// </summary>
    public static ColorStringFinding? Classify(string value, string? resourceKey, bool fontFamilyOverloadExists, string? fontFamilyArgumentName = null)
    {
        if (TryParse(value))
            return null;

        if (resourceKey is not null && resourceKey.EndsWith(FontFamilyKeySuffix, StringComparison.Ordinal))
        {
            return fontFamilyOverloadExists
                ? new ColorStringFinding(
                    ColorStringIssue.FontFamilyKey,
                    value,
                    $"'{resourceKey}' is a FontFamily resource; did you mean {FontFamilySuggestion(value, fontFamilyArgumentName)}?")
                : new ColorStringFinding(
                    ColorStringIssue.FontFamilyKey,
                    null,
                    $"'{resourceKey}' is a FontFamily resource, but this Set overload only stores color brushes; to change the font, use .FontFamily({Literal(value)}) on the element");
        }

        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            var digits = value.Substring(1);
            if ((digits.Length == 3 || digits.Length == 4) && IsAllHex(digits))
            {
                var expanded = Expand(digits);
                return new ColorStringFinding(
                    ColorStringIssue.HexShorthand,
                    expanded,
                    $"#RGB/#ARGB shorthand isn't supported; did you mean \"{expanded}\"?");
            }
            return new ColorStringFinding(
                ColorStringIssue.MalformedHex,
                null,
                "hex colors must be #RRGGBB or #AARRGGBB");
        }

        if ((value.Length == 6 || value.Length == 8) && IsAllHex(value))
        {
            var hashed = "#" + value;
            return new ColorStringFinding(
                ColorStringIssue.MissingHash,
                hashed,
                $"hex colors need a leading '#'; did you mean \"{hashed}\"?");
        }

        if (NamedColorHex.TryGetValue(value, out var hex))
        {
            return new ColorStringFinding(
                ColorStringIssue.NamedColor,
                hex,
                $"the only built-in names are {SupportedNames}; did you mean \"{hex}\"?");
        }

        return new ColorStringFinding(
            ColorStringIssue.Unknown,
            null,
            $"use #RRGGBB or #AARRGGBB, a Theme token, or one of {SupportedNames}");
    }

    /// <summary>
    /// The C# source the FontFamily rewrite produces, e.g. <c>new Microsoft.UI.Xaml.Media.FontFamily("Consolas")</c>,
    /// prefixed with <c>name: </c> for a renamed argument. The type is namespace-qualified so the text compiles
    /// without a <c>using Microsoft.UI.Xaml.Media;</c> (<c>mur check</c> shows it verbatim), and the value is escaped
    /// as a regular C# string literal.
    /// </summary>
    public static string FontFamilySuggestion(string value, string? argumentName) =>
        (argumentName is null ? "" : argumentName + ": ") + "new Microsoft.UI.Xaml.Media.FontFamily(" + Literal(value) + ")";

    // A regular C# string literal for the value (quotes, backslashes and control characters escaped).
    private static string Literal(string value) =>
        Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);

    private static bool IsAllHex(string s)
    {
        foreach (var c in s)
        {
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex)
                return false;
        }
        return true;
    }

    private static string Expand(string digits)
    {
        var chars = new char[1 + digits.Length * 2];
        chars[0] = '#';
        for (var i = 0; i < digits.Length; i++)
        {
            chars[1 + i * 2] = digits[i];
            chars[2 + i * 2] = digits[i];
        }
        return new string(chars);
    }
}
