using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Advanced;

/// <summary>
/// Diff-gated property writes for hand-coded handlers whose element properties are
/// nullable "leave the control default" values.
/// </summary>
internal static class PropertyDiff
{
    /// <summary>
    /// On mount, writes <paramref name="newValue"/> when it is set. On update, writes it
    /// only when it differs from <paramref name="oldValue"/>; a transition back to
    /// <c>null</c> clears the local value through <paramref name="dp"/> (restoring the
    /// control default), or assigns <c>null</c> when the property has no
    /// <see cref="DependencyProperty"/>. <paramref name="dp"/> is a getter so the WinRT
    /// static lookup is only paid on a clear.
    /// </summary>
    internal static void Write<T>(
        DependencyObject target,
        Func<DependencyProperty>? dp,
        T oldValue,
        T newValue,
        bool mount,
        Action<T> set)
    {
        if (!mount && EqualityComparer<T>.Default.Equals(oldValue, newValue)) return;
        if (newValue is null)
        {
            if (mount) return;
            if (dp is not null) target.ClearValue(dp());
            else set(newValue);
            return;
        }
        set(newValue);
    }
}
