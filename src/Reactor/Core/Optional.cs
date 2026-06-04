namespace Microsoft.UI.Reactor;

/// <summary>
/// Represents either an explicit value or an unset state.
/// </summary>
/// <typeparam name="T">The type of value stored when the optional has a value.</typeparam>
public readonly struct Optional<T> : IEquatable<Optional<T>>
{
    private readonly bool _hasValue;
    private readonly T _value;

    /// <summary>
    /// Gets a value indicating whether this optional contains an explicit value.
    /// </summary>
    public bool HasValue => _hasValue;

    /// <summary>
    /// Gets the stored value when <see cref="HasValue"/> is true.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when this optional is unset.</exception>
    public T Value => _hasValue ? _value : throw new InvalidOperationException("Optional<T> is Unset");

    /// <summary>
    /// Gets the stored value, or <c>default</c> when this optional is unset.
    /// </summary>
    /// <returns>The stored value when set; otherwise <c>default</c>.</returns>
    public T GetValueOrDefault() => _value;

    /// <summary>
    /// Gets the stored value, or the supplied fallback when this optional is unset.
    /// </summary>
    /// <param name="fallback">The value to return when this optional is unset.</param>
    /// <returns>The stored value when set; otherwise <paramref name="fallback"/>.</returns>
    public T GetValueOrDefault(T fallback) => _hasValue ? _value : fallback;

    /// <summary>
    /// Gets the unset value. Equivalent to <c>default(Optional&lt;T&gt;)</c>.
    /// </summary>
    public static Optional<T> Unset => default;

    /// <summary>
    /// Creates an optional that contains an explicit value.
    /// </summary>
    /// <param name="value">The value to store. For reference types, <c>null</c> is an explicit value.</param>
    /// <returns>An optional containing <paramref name="value"/>.</returns>
    public static Optional<T> Of(T value) => new(value);

    private Optional(T value)
    {
        _hasValue = true;
        _value = value;
    }

    /// <summary>
    /// Converts a value to an optional that explicitly contains that value.
    /// For reference-type optionals, <c>with { Background = null }</c> becomes
    /// <c>Optional.Of(null)</c>, not <see cref="Unset"/>; use <see cref="Unset"/>
    /// to express the unset state.
    /// </summary>
    /// <param name="value">The value to store. For reference types, <c>null</c> is an explicit value.</param>
    public static implicit operator Optional<T>(T value) => new(value);

    /// <summary>
    /// Determines whether this optional and another optional represent the same state and value.
    /// </summary>
    /// <param name="other">The optional to compare with this optional.</param>
    /// <returns><c>true</c> when both optionals are unset or both contain equal values; otherwise <c>false</c>.</returns>
    public bool Equals(Optional<T> other)
    {
        if (_hasValue != other._hasValue)
            return false;
        return !_hasValue || EqualityComparer<T>.Default.Equals(_value, other._value);
    }

    /// <summary>
    /// Determines whether this optional and another object represent the same state and value.
    /// </summary>
    /// <param name="obj">The object to compare with this optional.</param>
    /// <returns><c>true</c> when <paramref name="obj"/> is an equal optional; otherwise <c>false</c>.</returns>
    public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

    /// <summary>
    /// Returns the hash code for this optional.
    /// </summary>
    /// <returns>A hash code that distinguishes unset from an explicit default value.</returns>
    public override int GetHashCode()
    {
        if (!_hasValue)
            return 0;

        var valueHash = _value is null ? 0 : EqualityComparer<T>.Default.GetHashCode(_value);
        return unchecked((valueHash * 397) ^ 1);
    }

    /// <summary>
    /// Returns a string representation of this optional.
    /// </summary>
    /// <returns><c>Unset</c> when unset; otherwise the stored value's string representation or <c>null</c>.</returns>
    public override string ToString() => _hasValue ? _value?.ToString() ?? "null" : "Unset";

    /// <summary>
    /// Determines whether two optionals represent the same state and value.
    /// </summary>
    /// <param name="left">The left optional.</param>
    /// <param name="right">The right optional.</param>
    /// <returns><c>true</c> when the optionals are equal; otherwise <c>false</c>.</returns>
    public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);

    /// <summary>
    /// Determines whether two optionals represent different states or values.
    /// </summary>
    /// <param name="left">The left optional.</param>
    /// <param name="right">The right optional.</param>
    /// <returns><c>true</c> when the optionals are not equal; otherwise <c>false</c>.</returns>
    public static bool operator !=(Optional<T> left, Optional<T> right) => !left.Equals(right);
}
