using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Shared typed lookup over a <see cref="ResourceDictionary"/>.
///
/// <para>
/// <see cref="ResourceDictionary.TryGetValue"/> performs the XAML resource walk
/// itself — it traverses <see cref="ResourceDictionary.MergedDictionaries"/>
/// (which is how <c>XamlControlsResources</c> and app-merged dictionaries
/// resolve) and honours their last-merged-wins precedence. So this helper does
/// not re-implement that walk; it exists to add the typed check that both
/// theme-brush resolution and named-style resolution need, in one place rather
/// than re-derived per call site.
/// </para>
///
/// <para>
/// That traversal is an assumption about WinUI rather than something this code
/// controls, so it is pinned by the <c>NamedStyle_ResourceLookupHonoursPrecedence</c>
/// selftest (a key reachable only through <c>MergedDictionaries</c>) and, for
/// the brush path, by <c>Theme_BrushCacheNullMissNotCached</c>. If a future
/// WinUI stops traversing, those fail and a manual own-entry-first,
/// reverse-order walk has to come back here.
/// </para>
/// </summary>
internal static class ResourceLookup
{
    /// <summary>
    /// Resolves <paramref name="key"/> to a <typeparamref name="T"/> in
    /// <paramref name="resources"/>.
    ///
    /// <para>
    /// <see cref="ResourceDictionary.TryGetValue"/> already performs the full
    /// XAML resource walk: it traverses <see cref="ResourceDictionary.MergedDictionaries"/>
    /// and honours their reverse, last-merged-wins precedence, and a dictionary's
    /// own entries outrank what it merges. So this helper does not re-implement
    /// that walk — re-implementing it forward would actively *break* precedence,
    /// and re-implementing it in reverse would be unreachable code. All this adds
    /// is the typed check.
    /// </para>
    ///
    /// <para>
    /// That traversal is an assumption, not a guarantee we control, so the
    /// <c>NamedStyle_ResourceLookupHonoursPrecedence</c> selftest pins it: if a
    /// future WinUI stops traversing merged dictionaries, that fixture fails and
    /// tells you to reinstate a manual reverse walk here.
    /// </para>
    ///
    /// <para>
    /// Returning <see langword="false"/> for a key that resolves to the wrong
    /// type (rather than continuing to search) is what lets <c>ApplyStyle</c>
    /// honour its "found but not a Style → warn" contract.
    /// </para>
    /// </summary>
    internal static bool TryFind<T>(ResourceDictionary? resources, string key, [NotNullWhen(true)] out T? value)
        where T : class
    {
        value = null;
        if (resources is null)
            return false;

        if (!resources.TryGetValue(key, out var found))
            return false;

        value = found as T;
        return value is not null;
    }
}
