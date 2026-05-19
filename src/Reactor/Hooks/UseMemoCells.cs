using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Hooks;

/// <summary>
/// Cell-level memoization hook for high-frequency list / grid bodies.
/// Reuses element references for cells whose item value (and declared
/// dependencies) haven't changed since the previous render. The reconciler
/// short-circuits on <see cref="object.ReferenceEquals(object?, object?)"/>,
/// so reused cells skip diffing entirely.
/// </summary>
/// <remarks>
/// <para>
/// Spec 034 §C. The signature deliberately matches <c>UseMemo</c> /
/// <c>UseEffect</c> / <c>UseCallback</c>: deps are trailing
/// <c>params</c>. The closure-capture correctness problem (a builder that
/// closes over <c>theme</c> / <c>selection</c> without listing them as
/// deps and silently renders stale) is caught at compile time by the
/// <c>REACTOR_HOOKS_007</c> Roslyn analyzer that ships with the framework.
/// Indirect captures through helper methods are a documented blind spot —
/// no static fix is available without whole-program analysis.
/// </para>
/// <para>
/// <b>When to use:</b> tickers, log tables, observability dashboards, file
/// lists, and other large readonly grids whose cell content is a pure
/// function of each item value plus a small set of declared
/// deps. <b>When not to use:</b> rows whose chrome depends on focus /
/// drag / selection / hover state that you aren't capturing in deps.
/// </para>
/// <para>
/// <b>gen2 trade-off:</b> memo trades short-lived gen0 churn for
/// longer-lived gen1/gen2 retention. Many memoized lists across an app
/// can compound gen2 pressure. Profile before deciding.
/// </para>
/// </remarks>
public static class UseMemoCellsExtensions
{
    /// <summary>
    /// Memoize cell construction for <paramref name="items"/>. On the first
    /// render the builder runs for every index; on subsequent renders, an
    /// item that compares <see cref="object.Equals(object?, object?)"/>
    /// against the previous render's value at the same index reuses the
    /// previous element. Any change to <paramref name="dependencies"/>
    /// invalidates the entire cache and rebuilds every cell.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="ctx">The render context.</param>
    /// <param name="items">Source items, one cell per item.</param>
    /// <param name="builder">Builder for a single cell. Must be a pure
    /// function of <c>(item, index)</c> plus <paramref name="dependencies"/>.
    /// Closure captures missing from the deps list are flagged by the
    /// <c>REACTOR_HOOKS_007</c> analyzer.</param>
    /// <param name="dependencies">Trailing-<c>params</c> list of values
    /// the builder closes over. Equivalent semantics to <c>UseMemo</c>:
    /// any change invalidates the entire memo.</param>
    /// <example>
    /// <code>
    /// var theme = ctx.UseTheme();
    /// var children = ctx.UseMemoCells(
    ///     stocks,
    ///     (item, i) =&gt; Cell(item, theme),
    ///     theme);   // ← deps; framework invalidates on change
    /// </code>
    /// </example>
    /// <remarks>Spec 034 §C.</remarks>
    public static Element[] UseMemoCells<T>(
        this RenderContext ctx,
        IReadOnlyList<T> items,
        Func<T, int, Element> builder,
        params object[] dependencies)
        where T : notnull
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));

        var stateRef = ctx.UseRef<MemoCellsState<T>?>(null);
        var prev = stateRef.Current;
        var depsChanged = prev is null || !DepsEqual(prev.Deps, dependencies);
        var count = items.Count;
        var children = new Element[count];

        if (depsChanged)
        {
            for (int i = 0; i < count; i++)
                children[i] = builder(items[i], i);
        }
        else
        {
            var prevItems = prev!.Items;
            var prevChildren = prev.Children;
            var prevLen = prevItems.Length;
            for (int i = 0; i < count; i++)
            {
                var item = items[i];
                if (i < prevLen && Equals(item, prevItems[i]))
                    children[i] = prevChildren[i];
                else
                    children[i] = builder(item, i);
            }
        }

        stateRef.Current = new MemoCellsState<T>(SnapshotItems(items), children, SnapshotDeps(dependencies));
        return children;
    }

    /// <summary>
    /// Memoize cell construction keyed by <paramref name="keySelector"/>.
    /// Cells are reused when both the item's key and value match the
    /// previous render. Keys that recur with mutated content rebuild that
    /// cell only. Reordered keys reuse cells (the reconciler's keyed-
    /// children path keeps the underlying control without unmount/remount).
    /// </summary>
    /// <param name="ctx">The render context.</param>
    /// <param name="items">Source items.</param>
    /// <param name="keySelector">Stable identity per item. Duplicate
    /// keys collapse to last-write-wins (later items overwrite earlier
    /// items in the lookup table).</param>
    /// <param name="builder">Cell builder; same contract as
    /// <see cref="UseMemoCells{T}"/>.</param>
    /// <param name="dependencies">Trailing-<c>params</c> deps.</param>
    /// <remarks>Spec 034 §C.</remarks>
    public static Element[] UseMemoCellsByKey<T, TKey>(
        this RenderContext ctx,
        IReadOnlyList<T> items,
        Func<T, TKey> keySelector,
        Func<T, int, Element> builder,
        params object[] dependencies)
        where T : notnull
        where TKey : notnull
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));

        var stateRef = ctx.UseRef<MemoCellsByKeyState<T, TKey>?>(null);
        var prev = stateRef.Current;
        var depsChanged = prev is null || !DepsEqual(prev.Deps, dependencies);
        var count = items.Count;
        var children = new Element[count];
        var keyToIndex = depsChanged ? null : prev!.KeyToIndex;

        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            if (keyToIndex is not null
                && keyToIndex.TryGetValue(keySelector(item), out var prevIdx)
                && Equals(item, prev!.Items[prevIdx]))
            {
                children[i] = prev!.Children[prevIdx];
            }
            else
            {
                children[i] = builder(item, i);
            }
        }

        var snapshotItems = SnapshotItems(items);
        var snapshotKeyMap = new Dictionary<TKey, int>(count);
        for (int i = 0; i < count; i++)
        {
            // Last-write-wins on duplicate keys.
            snapshotKeyMap[keySelector(snapshotItems[i])] = i;
        }
        stateRef.Current = new MemoCellsByKeyState<T, TKey>(snapshotItems, children, SnapshotDeps(dependencies), snapshotKeyMap);
        return children;
    }

    /// <summary>
    /// Memoize cell construction when the data source already knows which
    /// indices changed. Skips the per-cell <see cref="object.Equals(object?, object?)"/>
    /// scan entirely; the builder runs only for indices in
    /// <paramref name="changedIndices"/>. When the item count changes
    /// between renders the overload falls back to a full rebuild
    /// (<paramref name="changedIndices"/> is treated as
    /// "rebuild everything") because the index space no longer matches
    /// the prior render. Callers whose lists grow or shrink frequently
    /// will get better incremental reuse from <see cref="UseMemoCells{T}"/>
    /// or <see cref="UseMemoCellsByKey{T,TKey}"/>, both of which can
    /// short-circuit per-cell on value or key equality across length
    /// changes.
    /// </summary>
    /// <param name="ctx">The render context.</param>
    /// <param name="items">Source items.</param>
    /// <param name="changedIndices">Indices whose item differs from the
    /// previous render. Negative indices and indices >= <c>items.Count</c>
    /// throw <see cref="ArgumentOutOfRangeException"/>.</param>
    /// <param name="builder">Cell builder; same contract as
    /// <see cref="UseMemoCells{T}"/>.</param>
    /// <param name="dependencies">Trailing-<c>params</c> deps.</param>
    /// <remarks>Spec 034 §C.</remarks>
    public static Element[] UseMemoCellsByIndex<T>(
        this RenderContext ctx,
        IReadOnlyList<T> items,
        IReadOnlyList<int> changedIndices,
        Func<T, int, Element> builder,
        params object[] dependencies)
        where T : notnull
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (changedIndices is null) throw new ArgumentNullException(nameof(changedIndices));
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));

        var stateRef = ctx.UseRef<MemoCellsState<T>?>(null);
        var prev = stateRef.Current;
        var depsChanged = prev is null || !DepsEqual(prev.Deps, dependencies);
        var count = items.Count;
        var children = new Element[count];

        if (depsChanged || prev!.Children.Length != count)
        {
            // First render or count changed: rebuild every cell.
            for (int i = 0; i < count; i++)
                children[i] = builder(items[i], i);
        }
        else
        {
            // Start with full reuse from prev, then rebuild only the named
            // indices. Validate bounds first so a bad caller can't half-update
            // the array before throwing.
            for (int k = 0; k < changedIndices.Count; k++)
            {
                int idx = changedIndices[k];
                if ((uint)idx >= (uint)count)
                    throw new ArgumentOutOfRangeException(nameof(changedIndices),
                        $"Index {idx} is out of range for items list of length {count}.");
            }

            var prevChildren = prev!.Children;
            for (int i = 0; i < count; i++)
                children[i] = prevChildren[i];
            for (int k = 0; k < changedIndices.Count; k++)
            {
                int idx = changedIndices[k];
                children[idx] = builder(items[idx], idx);
            }
        }

        stateRef.Current = new MemoCellsState<T>(SnapshotItems(items), children, SnapshotDeps(dependencies));
        return children;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    // <snippet:demo>
    // Memo snapshot: copy the caller's items into a private buffer so a
    // subsequent mutation by the caller can't corrupt the memo's view of
    // "what we showed last time".
    private static T[] SnapshotItems<T>(IReadOnlyList<T> items)
    {
        var snapshot = new T[items.Count];
        for (int i = 0; i < snapshot.Length; i++)
            snapshot[i] = items[i];
        return snapshot;
    }
    // </snippet:demo>

    private static object[] SnapshotDeps(object[] deps)
    {
        if (deps.Length == 0) return Array.Empty<object>();
        var copy = new object[deps.Length];
        Array.Copy(deps, copy, deps.Length);
        return copy;
    }

    private static bool DepsEqual(object[] prev, object[] next)
    {
        if (prev.Length != next.Length) return false;
        for (int i = 0; i < prev.Length; i++)
        {
            if (!Equals(prev[i], next[i])) return false;
        }
        return true;
    }

    private sealed record MemoCellsState<T>(T[] Items, Element[] Children, object[] Deps);

    private sealed record MemoCellsByKeyState<T, TKey>(T[] Items, Element[] Children, object[] Deps, Dictionary<TKey, int> KeyToIndex)
        where TKey : notnull;
}

/// <summary>
/// <see cref="Component"/>-class shims for <see cref="UseMemoCellsExtensions"/>
/// so subclasses can call <c>UseMemoCells</c> without going through
/// <c>this.Context</c>. Same semantics as the <see cref="RenderContext"/>
/// extension methods.
/// </summary>
/// <remarks>Spec 034 §C.</remarks>
public static class ComponentUseMemoCellsExtensions
{
    /// <summary>
    /// Component-extension shim for <see cref="UseMemoCellsExtensions.UseMemoCells{T}(RenderContext, IReadOnlyList{T}, Func{T, int, Element}, object[])"/>.
    /// Same semantics as the <see cref="RenderContext"/>-extension form;
    /// dispatches against <c>component.Context</c>.
    /// </summary>
    /// <typeparam name="T">Cell item type.</typeparam>
    /// <param name="component">The component whose render context owns the hook slot.</param>
    /// <param name="items">Source items.</param>
    /// <param name="builder">Per-cell builder.</param>
    /// <param name="dependencies">Additional hook dependencies.</param>
    public static Element[] UseMemoCells<T>(
        this Component component,
        IReadOnlyList<T> items,
        Func<T, int, Element> builder,
        params object[] dependencies)
        where T : notnull
    {
        if (component is null) throw new ArgumentNullException(nameof(component));
        return ComponentContext(component).UseMemoCells(items, builder, dependencies);
    }

    /// <summary>
    /// Component-extension shim for <see cref="UseMemoCellsExtensions.UseMemoCellsByKey{T, TKey}(RenderContext, IReadOnlyList{T}, Func{T, TKey}, Func{T, int, Element}, object[])"/>.
    /// Same semantics as the <see cref="RenderContext"/>-extension form;
    /// dispatches against <c>component.Context</c>.
    /// </summary>
    /// <typeparam name="T">Cell item type.</typeparam>
    /// <typeparam name="TKey">Stable cell key type.</typeparam>
    /// <param name="component">The component whose render context owns the hook slot.</param>
    /// <param name="items">Source items.</param>
    /// <param name="keySelector">Projection from item to stable key.</param>
    /// <param name="builder">Per-cell builder.</param>
    /// <param name="dependencies">Additional hook dependencies.</param>
    public static Element[] UseMemoCellsByKey<T, TKey>(
        this Component component,
        IReadOnlyList<T> items,
        Func<T, TKey> keySelector,
        Func<T, int, Element> builder,
        params object[] dependencies)
        where T : notnull
        where TKey : notnull
    {
        if (component is null) throw new ArgumentNullException(nameof(component));
        return ComponentContext(component).UseMemoCellsByKey(items, keySelector, builder, dependencies);
    }

    /// <summary>
    /// Component-extension shim for <see cref="UseMemoCellsExtensions.UseMemoCellsByIndex{T}(RenderContext, IReadOnlyList{T}, IReadOnlyList{int}, Func{T, int, Element}, object[])"/>.
    /// Same semantics as the <see cref="RenderContext"/>-extension form;
    /// dispatches against <c>component.Context</c>.
    /// </summary>
    /// <typeparam name="T">Cell item type.</typeparam>
    /// <param name="component">The component whose render context owns the hook slot.</param>
    /// <param name="items">Source items.</param>
    /// <param name="changedIndices">Indices whose builder output should re-run.</param>
    /// <param name="builder">Per-cell builder.</param>
    /// <param name="dependencies">Additional hook dependencies.</param>
    public static Element[] UseMemoCellsByIndex<T>(
        this Component component,
        IReadOnlyList<T> items,
        IReadOnlyList<int> changedIndices,
        Func<T, int, Element> builder,
        params object[] dependencies)
        where T : notnull
    {
        if (component is null) throw new ArgumentNullException(nameof(component));
        return ComponentContext(component).UseMemoCellsByIndex(items, changedIndices, builder, dependencies);
    }

    private static RenderContext ComponentContext(Component component)
    {
        // Component.Context is internal — but this assembly is the same as
        // Component, so the access is direct.
        return component.Context;
    }
}
