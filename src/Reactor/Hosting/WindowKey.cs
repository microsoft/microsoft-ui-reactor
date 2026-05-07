namespace Microsoft.UI.Reactor;

/// <summary>
/// Stable identity for a top-level surface within a process. Used by
/// <see cref="ReactorApp.FindWindow(WindowKey)"/>,
/// <see cref="WindowSpec.Key"/>, and the <c>UseOpenWindow</c> hook to dedupe
/// re-renders that should reuse a window rather than open a new one.
/// (spec 036 §4.4)
/// </summary>
/// <remarks>
/// Identity is by ordinal name within the current process. Implicit conversion
/// from <c>string</c> exists so call sites can pass a literal directly:
/// <code>ReactorApp.FindWindow("settings")</code>.
/// </remarks>
public readonly record struct WindowKey(string Name)
{
    /// <summary>Construct a key from a name. Throws when <paramref name="name"/> is null or empty.</summary>
    public static WindowKey Of(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("WindowKey name must be non-empty.", nameof(name));
        return new WindowKey(name);
    }

    /// <summary>Implicit conversion from <c>string</c> for ergonomic call sites.</summary>
    public static implicit operator WindowKey(string name) => Of(name);

    /// <inheritdoc />
    public override string ToString() => Name;
}
