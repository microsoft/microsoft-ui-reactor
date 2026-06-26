using System;
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
    internal ChildDiffHint(int[] changedIndices, int themeSensitiveCount, Element[] previousChildren)
    {
        ChangedIndices = changedIndices;
        ThemeSensitiveCount = themeSensitiveCount;
        PreviousChildren = new WeakReference<Element[]>(previousChildren);
    }

    /// <summary>
    /// Indices whose new cell differs (by reference) from the previous render.
    /// Every OTHER index is guaranteed reference-equal to the prior array by
    /// <c>UseMemoCellsByIndex</c> construction (it reuses <c>prevChildren[i]</c>
    /// for unchanged indices and only rebuilds these).
    /// </summary>
    internal int[] ChangedIndices { get; }

    /// <summary>
    /// Weak handle to the EXACT previous-render array the <see cref="ChangedIndices"/>
    /// were diffed against. The positional fast path engages only when the
    /// reconciler's old-children array IS this array — a cheap, self-documenting
    /// sufficient condition for the real invariant (every unchanged index is
    /// reference-equal between old and new). Holds in steady state by construction
    /// (the reconciler reconciles consecutive committed trees, and the V1 panel
    /// descriptor surfaces the producer's <c>Element[]</c> by reference for both
    /// sides); a mismatch (any defensive copy) safely falls back to the full walk.
    /// Weak on purpose: a strong reference here would chain
    /// <c>children_N → hint_N → children_(N-1) → hint_(N-1) → …</c> through the
    /// reference-keyed <see cref="ChildDiffHints"/> table and pin every past
    /// render's array for the lifetime of the live one.
    /// </summary>
    internal WeakReference<Element[]> PreviousChildren { get; }

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
    /// A <c>null</c> cell is treated as non-theme-sensitive: child arrays may
    /// legitimately contain nulls (a builder may return null; <c>ChildReconciler.Filter</c>
    /// drops them downstream), and a null has no bindings to re-resolve — so the
    /// theme tally must tolerate it rather than throw.
    /// </summary>
    internal static bool IsThemeSensitive(Element? element)
        => element is not null
           && (element.ThemeBindings is not null
               || element.ResourceOverrides is { ThemeRefs.Count: > 0 });
}
