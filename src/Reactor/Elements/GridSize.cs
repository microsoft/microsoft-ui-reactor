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
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public readonly record struct GridSize(double Value, GridUnitType Type)
{
    /// <summary>The minimum size for the track.</summary>
    public double? Min { get; init; }

    /// <summary>Optional maximum size for the track.</summary>
    public double? Max { get; init; }

    /// <summary>The auto-sized track. Equivalent to the WinUI <c>Auto</c> length.</summary>
    public static GridSize Auto { get; } = new(1, GridUnitType.Auto);

    /// <summary>
    /// A star-weighted track. <paramref name="weight"/> defaults to 1 (i.e. <c>"*"</c>);
    /// 1.5 produces <c>"1.5*"</c>, 0.33 produces <c>"0.33*"</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="weight"/> is &lt;= 0.</exception>
    public static GridSize Star(double weight = 1)
    {
        if (!(weight > 0))
            throw new ArgumentOutOfRangeException(nameof(weight), weight, "Star weight must be > 0.");
        return new GridSize(weight, GridUnitType.Star);
    }

    /// <summary>A pixel-sized track. <paramref name="pixels"/> must be &gt;= 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pixels"/> is &lt; 0.</exception>
    public static GridSize Px(double pixels)
    {
        if (pixels < 0)
            throw new ArgumentOutOfRangeException(nameof(pixels), pixels, "Pixel size must be >= 0.");
        return new GridSize(pixels, GridUnitType.Pixel);
    }

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
    public override string ToString()
    {
        var size = Type switch
        {
            GridUnitType.Auto => "Auto",
            GridUnitType.Star when Value == 1 => "*",
            GridUnitType.Star => Value.ToString("R", CultureInfo.InvariantCulture) + "*",
            GridUnitType.Pixel => Value.ToString("R", CultureInfo.InvariantCulture),
            _ => $"GridSize({Value}, {Type})",
        };

        if (Min is { } min)
            size += ";min=" + min.ToString("R", CultureInfo.InvariantCulture);
        if (Max is { } max)
            size += ";max=" + max.ToString("R", CultureInfo.InvariantCulture);
        return size;
    }

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
}

/// <summary>
/// Parsed grid track information. The string form is the regular track size
/// followed by optional <c>;min=&lt;value&gt;</c> and <c>;max=&lt;value&gt;</c> clauses.
/// </summary>
internal readonly record struct GridTrackDefinition(GridSize Size, double? Min, double? Max)
{
    public static bool TryParse(string value, out GridTrackDefinition definition)
    {
        definition = default;
        if (value is null) return false;

        var parts = value.Split(';');
        if (parts.Length == 0) return false;

        GridSize size;
        try
        {
            size = GridSize.Parse(parts[0]);
        }
        catch (FormatException)
        {
            return false;
        }

        double? min = null;
        double? max = null;
        for (var i = 1; i < parts.Length; i++)
        {
            var clause = parts[i].Trim();
            var separator = clause.IndexOf('=');
            if (separator <= 0 || separator == clause.Length - 1) return false;

            var name = clause[..separator].Trim();
            var number = clause[(separator + 1)..].Trim();
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                !double.IsFinite(parsed) || parsed < 0)
                return false;

            if (string.Equals(name, "min", StringComparison.OrdinalIgnoreCase) && min is null)
                min = parsed;
            else if (string.Equals(name, "max", StringComparison.OrdinalIgnoreCase) && max is null)
                max = parsed;
            else
                return false;
        }

        if (min is { } minimum && max is { } maximum && minimum > maximum)
            return false;

        definition = new GridTrackDefinition(size, min, max);
        return true;
    }
}
