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
    /// Number of cells carrying a ThemeRef-backed <c>ResourceOverrides</c> (a
    /// concrete brush that is NOT self-healing). Carried forward incrementally
    /// across reuse renders so the producer stays O(changed) in steady state.
    /// </summary>
    internal int ThemeSensitiveCount { get; }

    /// <summary>
    /// True when ANY cell re-resolves a NON-self-healing theme value. The positional
    /// fast path must then fall back to the full walk so <c>ApplyResourceOverrides</c>
    /// re-resolves the concrete brush against the (possibly changed) effective theme
    /// even on untouched, reference-equal cells — a parent <c>RequestedTheme</c> toggle
    /// changes the effective theme WITHOUT touching the element tree, so a structural
    /// skip would otherwise leave a resolved-brush override stale. (<c>ThemeBindings</c>
    /// are excluded: their <c>{ThemeResource}</c> setters self-heal natively — see
    /// <see cref="ChildDiffHints.IsThemeSensitive"/>.)
    /// </summary>
    /// <remarks>
    /// Tested with <c>!= 0</c> rather than <c>&gt; 0</c> as a fail-safe. The only
    /// producer (<c>UseMemoCellsByIndex</c>) already clamps its incremental tally
    /// to a non-negative floor before publishing (see <c>UseMemoCells</c>), so a
    /// negative count is unreachable — and <c>!= 0</c> is therefore behaviorally
    /// identical to <c>&gt; 0</c> for every value the producer can actually emit
    /// (all <c>&gt;= 0</c>). The difference is purely defensive: were an anomalous
    /// negative ever published, this still reports theme-sensitive and BLOCKS the
    /// skip, forcing the always-correct full walk. For a correctness gate the safe
    /// fail direction is to re-resolve (do more), never to silently skip (do less).
    /// </remarks>
    internal bool AnyThemeSensitive => ThemeSensitiveCount != 0;
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
    /// True when the element re-resolves a NON-self-healing theme value on each
    /// update — i.e. a ThemeRef-backed <c>ResourceOverrides</c>, which
    /// <c>ApplyResourceOverrides</c> resolves to a CONCRETE brush at reconcile
    /// (and which therefore goes stale on an effective-theme change unless
    /// <c>Update</c> re-runs). <c>ThemeBindings</c> are deliberately EXCLUDED
    /// (narrowed per #758): <c>.Foreground(Theme.X)</c> compiles to a
    /// <c>{ThemeResource}</c> Style setter that WinUI re-resolves NATIVELY on the
    /// control's effective-theme change (app theme OR an ancestor
    /// <c>RequestedTheme</c>) — self-healing whether or not Reactor recurses into the
    /// cell, so a ThemeBindings-only cell is safe to structurally skip. Mirrors
    /// <see cref="Element.CanSkipUpdate"/>.
    /// A <c>null</c> cell is treated as non-theme-sensitive: child arrays may
    /// legitimately contain nulls (a builder may return null; <c>ChildReconciler.Filter</c>
    /// drops them downstream), and a null has no override to re-resolve — so the
    /// theme tally must tolerate it rather than throw.
    /// </summary>
    internal static bool IsThemeSensitive(Element? element)
        => element is not null
           && element.ResourceOverrides is { ThemeRefs.Count: > 0 };
}
