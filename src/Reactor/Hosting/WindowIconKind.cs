namespace Microsoft.UI.Reactor;

/// <summary>
/// Which kind of source a <see cref="WindowIcon"/> was constructed from.
/// (spec 036 §4.1)
/// </summary>
/// <remarks>
/// Consumers differ in what they can accept, because most need a raw Win32
/// <c>HICON</c> while a few need a <see cref="Uri"/>. See the remarks on
/// <see cref="WindowIcon"/> for the per-surface support matrix.
/// </remarks>
public enum WindowIconKind
{
    /// <summary>A filesystem path, from <see cref="WindowIcon.FromPath(string)"/>.</summary>
    Path,

    /// <summary>An <c>ms-appx:///</c> resource URI, from <see cref="WindowIcon.FromResource(string)"/>.</summary>
    Resource,

    /// <summary>
    /// Icon data held in memory, from <see cref="WindowIcon.FromBytes(ReadOnlySpan{byte})"/>
    /// or <see cref="WindowIcon.FromRgba(ReadOnlySpan{byte}, int, int)"/>.
    /// </summary>
    Binary,
}
