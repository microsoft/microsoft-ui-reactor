using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Shared depth-first lookup over a <see cref="ResourceDictionary"/> and its
/// <see cref="ResourceDictionary.MergedDictionaries"/>.
///
/// <para>
/// <see cref="ResourceDictionary"/> implements <c>IDictionary</c>, so
/// <c>TryGetValue</c> consults only the dictionary it is called on. WinUI's
/// own resources arrive as merged dictionaries — <c>XamlControlsResources</c>
/// merges the theme dictionaries that define the type ramp, and app authors
/// merge their own — so a single non-recursive probe can miss a key that the
/// XAML resource-lookup walk would find. Both theme-brush resolution and
/// named-style resolution need the recursive form, so it lives here rather
/// than being re-derived per call site.
/// </para>
/// </summary>
internal static class ResourceLookup
{
    /// <summary>
    /// Finds the first value keyed <paramref name="key"/> that is a
    /// <typeparamref name="T"/>, searching <paramref name="resources"/> first
    /// and then recursing its merged dictionaries in declaration order.
    /// </summary>
    /// <returns><see langword="true"/> if a <typeparamref name="T"/> was
    /// found; otherwise <see langword="false"/> with
    /// <paramref name="value"/> set to <see langword="null"/>.</returns>
    internal static bool TryFind<T>(ResourceDictionary? resources, string key, [NotNullWhen(true)] out T? value)
        where T : class
    {
        if (resources is not null)
        {
            if (resources.TryGetValue(key, out var found) && found is T typed)
            {
                value = typed;
                return true;
            }

            foreach (var merged in resources.MergedDictionaries)
            {
                if (TryFind(merged, key, out value))
                    return true;
            }
        }

        value = null;
        return false;
    }
}
