using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Microsoft.UI.Reactor.Core;

// AI-HINT: Structural-skip side-channel for positional child reconciliation.
//   Published per render by UseMemoCellsByIndex; read by ChildReconciler's
//   positional fast path. Keyed on the hook's fresh-per-render Element[] by
//   reference identity, mirroring #681's _dirtyAncestorPath bridge: no
//   Element-record widening, AOT-safe (ConditionalWeakTable, no reflection).

/// <summary>
/// Spec 034 §C — a hint describing which positional cells changed since the
/// previous render, plus whether any cell re-resolves a theme-keyed value.
/// Lets <see cref="ChildReconciler"/> update only the changed indices and skip
/// the (provably reference-equal) untouched range — turning the positional
/// skip-walk from O(count) into O(changed).
/// </summary>
internal sealed class ChildDiffHint
{
    internal ChildDiffHint(int[] changedIndices, int themeSensitiveCount)
    {
        ChangedIndices = changedIndices;
        ThemeSensitiveCount = themeSensitiveCount;
    }

    /// <summary>
    /// Indices whose new cell differs (by reference) from the previous render.
    /// Every OTHER index is guaranteed reference-equal to the prior array by
    /// <c>UseMemoCellsByIndex</c> construction (it reuses <c>prevChildren[i]</c>
    /// for unchanged indices and only rebuilds these).
    /// </summary>
    internal int[] ChangedIndices { get; }

    /// <summary>
    /// Number of cells carrying <c>ThemeBindings</c> or a ThemeRef-backed
    /// <c>ResourceOverrides</c>. Carried forward incrementally across reuse
    /// renders so the producer stays O(changed) in steady state.
    /// </summary>
    internal int ThemeSensitiveCount { get; }

    /// <summary>
    /// True when ANY cell re-resolves a theme-keyed value. The positional fast
    /// path must then fall back to the full walk so <c>ApplyThemeBindings</c> /
    /// <c>ApplyResourceOverrides</c> re-resolve against the (possibly changed)
    /// effective theme even on untouched, reference-equal cells — a parent
    /// <c>RequestedTheme</c> toggle changes the effective theme WITHOUT touching
    /// the element tree, so a structural skip would otherwise leave brushes stale.
    /// </summary>
    internal bool AnyThemeSensitive => ThemeSensitiveCount > 0;
}

/// <summary>
/// Reference-keyed registry mapping a render's child <see cref="Element"/>[] to
/// its <see cref="ChildDiffHint"/>. Weak keys: entries evict automatically when
/// the per-render array is collected, so no explicit cleanup is needed.
/// </summary>
internal static class ChildDiffHints
{
    private static readonly ConditionalWeakTable<Element[], ChildDiffHint> s_hints = new();

    internal static void Publish(Element[] children, ChildDiffHint hint)
        => s_hints.AddOrUpdate(children, hint);

    internal static bool TryGet(Element[] children, [NotNullWhen(true)] out ChildDiffHint? hint)
        => s_hints.TryGetValue(children, out hint);

    /// <summary>
    /// True when the element re-resolves a theme-keyed value on each update —
    /// either <c>ThemeBindings</c> brushes or a ThemeRef-backed resource override.
    /// Mirrors the theme arms in <c>Reconciler.Update</c> (the only work the full
    /// walk performs for an untouched cell that a structural skip would drop).
    /// </summary>
    internal static bool IsThemeSensitive(Element element)
        => element.ThemeBindings is not null
           || element.ResourceOverrides is { ThemeRefs.Count: > 0 };
}
