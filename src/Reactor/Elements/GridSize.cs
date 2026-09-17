using System;
using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Strongly-typed grid track size, used by the typed
/// <c>Microsoft.UI.Reactor.Factories.Grid(GridSize[], GridSize[], Element?[])</c>
/// factory in place of the legacy string-form (<c>"Auto"</c>, <c>"*"</c>,
/// <c>"200"</c>, <c>"1.5*"</c>) tracks.
/// Optional <c>Min</c> and <c>Max</c> properties are available to constrain the track size.
/// </summary>
/// <remarks>
/// <para>
/// Spec 033 §1. The smart constructors <see cref="Auto"/>, <see cref="Star"/>,
/// and <see cref="Px"/> validate their inputs at the boundary; the implicit
/// conversion to <see cref="Microsoft.UI.Xaml.GridLength"/> lets the typed
/// form compose with anything that takes a WinUI <see cref="GridLength"/>.
/// </para>
/// <para>
/// The string parser (<see cref="Parse"/>) is invariant-culture only — track
/// strings are never localized. Whitespace is trimmed; <c>"Auto"</c> matches
/// case-insensitively; numeric forms use <see cref="CultureInfo.InvariantCulture"/>.
/// </para>
/// <para>
/// When Min or Max are specified, the track size is clamped to that range. For example,
/// <c>GridSize.Px(100) with { Min=50 and Max=200 }</c> will be set to 100 initially and depending on changes clamped to 50 and 200.
/// Min can be set higher as Max in which case the track size will be clamped to the Max value.
/// </para>
/// </remarks>
/// <exception cref="ArgumentOutOfRangeException">Thrown when <c>GridType</c> is set to <c>Pixel</c> and the <c>Value</c> is &lt; 0, or when the GridType is set to <c>Star</c> and the <c>Value</c> is &lt;= 0.</exception>
/// <exception cref="ArgumentOutOfRangeException">Thrown when the <c>Value</c>, <c>Min</c>, or <c>Max</c> properties are out of range.</exception>
/// <exception cref="ArgumentOutOfRangeException">Thrown when the <c>Min</c> is not a number or &lt; 0.</exception>
/// <exception cref="ArgumentOutOfRangeException">Thrown when the <c>Max</c> is not a number or &lt; 0.</exception>
[DebuggerDisplay("{ToString(),nq}")]
public readonly record struct GridSize
{
    public GridSize(double value, GridUnitType type, double? min = null, double? max = null)
    {
        // Validate the min and max values
        if(min.HasValue && !IsFiniteAndNonNegative(min.Value))
            throw new ArgumentOutOfRangeException(nameof(min), min, "Min size must be a number and >= 0.");
        if (max.HasValue && (max.Value < 0 || double.IsNaN(max.Value)))
            throw new ArgumentOutOfRangeException(nameof(max), max, "Max size must be a number and >= 0.");

        // Validate the value based on the type
        if (type == GridUnitType.Pixel && !IsFiniteAndNonNegative(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Pixel size must be a number and >= 0.");
        if (type == GridUnitType.Star && (!IsFiniteAndNonNegative(value) || value <= 0))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Star weight must be a number and > 0.");

        Value = value;
        Type = type;
        Min = min;
        Max = max;
    }

    /// <summary>
    /// The value of the track size. For <c>Pixel</c> types, this is the pixel size; for <c>Star</c> types, this is the star weight; for <c>Auto</c>, this is always 1.
    /// </summary>
    public double Value { get; }

    /// <summary>
    /// The type of the track size: <c>Auto</c>, <c>Star</c>, or <c>Pixel</c>.
    /// </summary>
    public GridUnitType Type { get; }

    /// <summary>
    /// The minimum size of the track. If specified, the track size will not be less than this value.
    /// </summary>
    public double? Min { get; }

    /// <summary>
    /// The maximum size of the track. If specified, the track size will not be greater than this value.
    /// </summary>
    public double? Max { get; }

    public void Deconstruct(out double value, out GridUnitType type) => (value, type) = (Value, Type);
    public void Deconstruct(
        out double value,
        out GridUnitType type,
        out double? min,
        out double? max
        ) => (value, type, min, max) = (Value, Type, Min, Max);

    /// <summary>The auto-sized track. Equivalent to the WinUI <c>Auto</c> length.</summary>
    public static GridSize Auto { get; } = new(1, GridUnitType.Auto);

    /// <summary>
    /// A star-weighted track. <paramref name="weight"/> defaults to 1 (i.e. <c>"*"</c>);
    /// 1.5 produces <c>"1.5*"</c>, 0.33 produces <c>"0.33*"</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="weight"/> is &lt;= 0.</exception>
    public static GridSize Star(double weight = 1) => new(weight, GridUnitType.Star);

    /// <summary>A pixel-sized track. <paramref name="pixels"/> must be &gt;= 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pixels"/> is &lt; 0.</exception>
    public static GridSize Px(double pixels) => new(pixels, GridUnitType.Pixel);

    /// <summary>
    /// Implicit conversion to <see cref="GridLength"/> so the typed form composes
    /// with any WinUI surface that takes a <see cref="GridLength"/>.
    /// </summary>
    public static implicit operator GridLength(GridSize s) => new(s.Value, s.Type);

    /// <summary>
    /// Canonical track-string form: <c>"Auto"</c>, <c>"*"</c>, <c>"&lt;n&gt;*"</c>, or
    /// <c>"&lt;n&gt;"</c>. Note that <c>Star(1)</c> round-trips to <c>"*"</c> (the implicit
    /// star-weight); explicit weights like 1.5 round-trip to <c>"1.5*"</c>.
    /// </summary>
    public override string ToString() => Type switch
    {
        GridUnitType.Auto => "Auto",
        GridUnitType.Star when Value == 1 => "*",
        GridUnitType.Star => Value.ToString("R", CultureInfo.InvariantCulture) + "*",
        GridUnitType.Pixel => Value.ToString("R", CultureInfo.InvariantCulture),
        _ => $"GridSize({Value}, {Type})",
    };

    /// <summary>
    /// Parses a track string into a <see cref="GridSize"/>. Accepts:
    /// <c>"Auto"</c> / <c>"auto"</c>, <c>"*"</c>, <c>"&lt;n&gt;*"</c>, <c>"&lt;n&gt;.&lt;m&gt;*"</c>,
    /// <c>"&lt;n&gt;"</c>, <c>"&lt;n&gt;.&lt;m&gt;"</c>. Whitespace is trimmed.
    /// </summary>
    /// <exception cref="FormatException">Thrown for any other input.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="s"/> is null.</exception>
    public static GridSize Parse(string s)
    {
        if (s is null) throw new ArgumentNullException(nameof(s));
        var trimmed = s.Trim();
        if (trimmed.Length == 0)
            throw new FormatException("Empty grid track string.");
        if (string.Equals(trimmed, "Auto", StringComparison.OrdinalIgnoreCase))
            return Auto;
        if (trimmed == "*")
            return Star();
        if (trimmed.EndsWith('*'))
        {
            var numeric = trimmed[..^1];
            if (numeric.Length == 0)
                throw new FormatException($"Invalid grid track '{s}'.");
            if (double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var stars) && stars > 0)
                return Star(stars);
            throw new FormatException($"Invalid grid track '{s}': could not parse star weight.");
        }
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var px) && px >= 0)
            return Px(px);
        throw new FormatException($"Invalid grid track '{s}'.");
    }

    private static bool IsFiniteAndNonNegative(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
}
