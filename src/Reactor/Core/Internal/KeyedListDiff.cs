using System.Buffers;
using Microsoft.Extensions.Logging;

namespace Microsoft.UI.Reactor.Core.Internal;

/// <summary>
/// Where a keyed-list diagnostic came from: the component whose render produced the list
/// (known while an inspector is attached) and the list's C# call site (when source-mapped).
/// </summary>
internal readonly record struct KeyedListAttribution(string? ComponentName, SourceLocation? CallSite)
{
    /// <summary>An element's call site, looking through modifier wrappers.</summary>
    internal static SourceLocation? CallSiteOf(Element? element)
    {
        var site = element?.CallSite;
        for (var e = element; site is null && e is ModifiedElement modified; e = modified.Inner)
            site = modified.Inner.CallSite;
        return site;
    }
}

/// <summary>
/// React-style keyed-children diff that maps an immutable
/// <c>IReadOnlyList&lt;T&gt;</c> of user items onto the internal
/// <see cref="ReactorListState.Source"/> by emitting the minimal-shape
/// sequence of <c>Insert</c> / <c>Move</c> / <c>RemoveAt</c> operations
/// WinUI needs to animate row containers incrementally. See spec 042 §4.3
/// for the algorithm.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm is single-pass O(n) with one auxiliary hash map (pooled
/// on <see cref="ReactorListState.Scratch"/> so per-render allocation is
/// avoided in the steady state):
/// </para>
/// <list type="number">
///   <item><description>Lockstep prefix walk: while old and new keys match
///   index-for-index, advance both. Skips the entire diff in the common
///   "nothing structural changed" case.</description></item>
///   <item><description>Build a key→row map of the remaining old rows.</description></item>
///   <item><description>Walk the remaining new keys: survivor → emit
///   <c>Source.Move</c> if current index differs from desired; otherwise
///   emit <c>Source.Insert</c> with a new <see cref="ReactorRow"/>.</description></item>
///   <item><description>Whatever stayed in the map is unmatched old rows →
///   emit <c>Source.RemoveAt</c> in descending index order so earlier
///   indices stay stable.</description></item>
/// </list>
/// <para>
/// Fast paths (no map allocation) short-circuit the common cases of
/// append-one, prepend-one, remove-front, remove-back.
/// </para>
/// <para>
/// Bailout: if churn exceeds 25% or the new key list contains duplicates,
/// the diff falls back to <see cref="ReactorListState.Reset"/> — animation
/// is degraded (entire list re-realizes) but data correctness is preserved.
/// Returned <see cref="DiffStats.Bailout"/> tells the caller to reset the
/// WinUI <c>ItemsSource</c> binding.
/// </para>
/// </remarks>
internal static class KeyedListDiff
{
    // Cached sample for the null-key bailout diagnostic so the slow path
    // doesn't allocate a fresh single-element array every time (#41).
    private static readonly string[] NullKeySample = { "<null>" };

    [ThreadStatic] private static HashSet<string>? t_siblingKeys;
    [ThreadStatic] private static HashSet<string>? t_seedKeys;

    // Largest pooled key set kept for reuse (see SeedKeys / ReportDuplicateSiblingKeys).
    private const int PooledKeySetLimit = 1024;

    // Descending-by-index comparer used to remove doomed rows from the tail
    // first so earlier OC indices stay stable. Singleton — avoids capturing a
    // fresh comparison delegate on every diff that has deletions.
    private sealed class RowIndexDescendingComparer : IComparer<ReactorRow>
    {
        public static readonly RowIndexDescendingComparer Instance = new();
        public int Compare(ReactorRow? a, ReactorRow? b) => b!.Index.CompareTo(a!.Index);
    }

    /// <summary>
    /// Per-diff bookkeeping returned to callers (tests, telemetry, the
    /// Phase 3 animation pipeline) so they can observe the op shape
    /// without walking the OC. When the caller passes a non-null ambient
    /// animation, <see cref="MovedRows"/> is populated with the survivors
    /// whose index changed so the caller can drive per-container offset
    /// animations on the matching realized containers.
    /// </summary>
    internal readonly record struct DiffStats(
        int Inserts,
        int Removes,
        int Moves,
        int Survivors,
        bool Bailout,
        IReadOnlyList<ReactorRow>? MovedRows = null)
    {
        public static readonly DiffStats Empty = new(0, 0, 0, 0, false, null);

        /// <summary>True if any structural op was emitted.</summary>
        public bool AnyOps => Inserts > 0 || Removes > 0 || Moves > 0;
    }

    /// <summary>
    /// Apply the keyed diff. Mutates <paramref name="state"/>.<see cref="ReactorListState.Source"/>
    /// via the OC's <c>Insert</c> / <c>Move</c> / <c>RemoveAt</c> operations
    /// so that the resulting sequence matches <paramref name="newItems"/>'s
    /// projection through <paramref name="keySelector"/>.
    /// </summary>
    /// <param name="state">Per-control diff state. The caller must have
    /// either called <see cref="ReactorListState.Reset"/> on mount or
    /// invoked <see cref="Apply{T}"/> on every prior update for the same
    /// control, so <see cref="ReactorListState.LastKeys"/> is in sync with
    /// <see cref="ReactorListState.Source"/>.</param>
    /// <param name="newItems">New immutable user-visible list.</param>
    /// <param name="keySelector">Maps each item to a stable identity string.
    /// Should produce non-null keys: a null result is replaced by its positional
    /// <see cref="NullKeySentinel"/> (the item is matched by position) and reported once
    /// per diff, which stays incremental.</param>
    /// <param name="logger">Optional logger used to surface the
    /// duplicate-key / null-key bailout diagnostic.</param>
    /// <param name="diagnosticContext">Human-readable label used in the
    /// diagnostic message — typically the host control type name.
    /// Hidden behind <c>?.</c> calls so the cost is paid only when a
    /// logger is attached.</param>
    /// <param name="controlInstance">Optional reference to the host control
    /// driving the diff. Used as the dedup key for the
    /// <c>ReactorDiagnostics</c> bailout collector so the first occurrence
    /// of a (control, kind, sample-set) triple lands a fresh entry and
    /// subsequent occurrences increment its counter in place. May be
    /// <see langword="null"/> for unit-test / standalone invocations, in
    /// which case dedup falls back to a global context-keyed ledger.</param>
    /// <param name="ambient">Active <see cref="Animations.Animate"/>
    /// transaction, or <see langword="null"/> when the diff runs outside one.
    /// When non-null and <see cref="AmbientAnimation.HasEffect"/> is true,
    /// inserted rows are tagged with the kind so the templated control's
    /// container-realization path can apply a per-container enter animation,
    /// and survivor rows that moved are reported via
    /// <see cref="DiffStats.MovedRows"/> so the caller can drive offset
    /// animations on the corresponding realized containers.
    /// (spec 042 §6.)</param>
    /// <param name="attribution">The component and call site the diagnostic names, when known.</param>
    /// <returns>Op-shape statistics. <see cref="DiffStats.Bailout"/> is
    /// true when the caller must reset its WinUI <c>ItemsSource</c>
    /// binding to <see cref="ReactorListState.Source"/> (because Reset
    /// replaced the collection contents in bulk).</returns>
    internal static DiffStats Apply<T>(
        ReactorListState state,
        IReadOnlyList<T> newItems,
        Func<T, int, string> keySelector,
        ILogger? logger = null,
        string? diagnosticContext = null,
        AmbientAnimation? ambient = null,
        object? controlInstance = null,
        KeyedListAttribution attribution = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(newItems);
        ArgumentNullException.ThrowIfNull(keySelector);

        int newCount = newItems.Count;

        // Rent the new-keys buffer (#2) rather than allocating a fresh array
        // every diff. The rented array may be larger than newCount, so the
        // explicit length is threaded to every consumer below — never
        // newKeys.Length. The buffer is cleared + returned on every exit
        // (including exceptions and the early bailouts) via the finally.
        string[] newKeys = newCount == 0
            ? global::System.Array.Empty<string>()
            : ArrayPool<string>.Shared.Rent(newCount);
        bool rentedKeys = newCount != 0;
        try
        {
            return ApplyCore(state, newItems, keySelector, logger, diagnosticContext, ambient, controlInstance, attribution, newKeys, newCount);
        }
        finally
        {
            if (rentedKeys)
                ArrayPool<string>.Shared.Return(newKeys, clearArray: true);
        }
    }

    /// <summary>
    /// Core keyed diff over a pre-materialized <paramref name="newKeys"/> buffer
    /// (valid length <paramref name="newCount"/>; the array may be larger). Split
    /// out from <see cref="Apply{T}"/> so the rented key buffer is released on
    /// every exit path via a single try/finally in the caller.
    /// </summary>
    private static DiffStats ApplyCore<T>(
        ReactorListState state,
        IReadOnlyList<T> newItems,
        Func<T, int, string> keySelector,
        ILogger? logger,
        string? diagnosticContext,
        AmbientAnimation? ambient,
        object? controlInstance,
        KeyedListAttribution attribution,
        string[] newKeys,
        int newCount)
    {
        // Resolve the animation intent for this diff once up front. A null
        // result means "no per-container animation work this diff" — every
        // op path below shortcuts on this rather than re-checking the kind.
        AnimationKind? enterKind = (ambient is { HasEffect: true }) ? ambient.Kind : null;

        int oldCount = state.LastKeys.Count;

        // Materialize new keys into the rented buffer. We read them twice
        // (lockstep walk + survivor pass), so projecting once up front avoids
        // the user callback observing each item more than once per diff.
        int firstNull = -1;
        for (int i = 0; i < newCount; i++)
        {
            var k = keySelector(newItems[i], i);
            if (k is null)
            {
                // A KeySelector bug (e.g. `t => t.Id` on a nullable property). The item is
                // matched by position and the diff stays incremental — a bailout would reset and
                // re-realize the whole list on every render while the key stays null. Reported
                // once per diff below, not once per null item.
                if (firstNull < 0) firstNull = i;
                newKeys[i] = NullKeySentinel(i);
                continue;
            }
            newKeys[i] = StateKey(k, i);
        }
        if (firstNull >= 0)
            ReportNullKey(controlInstance, diagnosticContext, logger, firstNull, attribution);

        // ── Fast path 1: no change (FLAGSHIP #1) ───────────────────────
        // Most renders don't touch the list shape — Reactor's reducer model
        // means immutable list replacement, but the *contents* (and thus the
        // keys) usually match. This now runs ABOVE the duplicate scan so the
        // steady-state grid case (keys never change) never allocates or scans
        // a dup set. Skip the diff entirely and let the caller's
        // RefreshRealizedContainers handle per-row content reconciliation.
        //
        // The `ByKey.Count == oldCount` guard keeps this fast path strictly for
        // the duplicate-free steady state. A prior duplicate bailout leaves
        // Source/LastKeys still holding the dup keys but ByKey collapsed to
        // first occurrences (ReactorListState.Reset), so ByKey.Count < oldCount
        // there. Without the guard an *unchanged* duplicate list would match
        // LastKeys and return Empty, silently skipping the duplicate
        // bailout/diagnostic the remarks at the top of this file promise. The
        // check is O(1) and short-circuits before the O(n) SequenceEqualOrdinal,
        // so the common unique-key path pays nothing extra.
        if (oldCount == newCount
            && state.ByKey.Count == oldCount
            && SequenceEqualOrdinal(state.LastKeys, newKeys, newCount))
            return DiffStats.Empty;

        // ── Fast path 2: empty ↔ empty ─────────────────────────────────
        if (oldCount == 0 && newCount == 0)
            return DiffStats.Empty;

        // Detect duplicate keys. Runs after the no-op fast path (#1) so an
        // unchanged list never pays for it, and reuses the pooled Scratch dict
        // instead of allocating a HashSet (#11). Duplicates inside newKeys
        // would otherwise silently corrupt the diff (the second occurrence
        // would never find a survivor because the first one consumed the map
        // entry, so it would emit an Insert with a duplicate key — and then a
        // later diff would have two rows with the same key in ByKey), so they
        // force a correctness-preserving bailout.
        if (HasDuplicates(newKeys, newCount, state))
        {
            ReportBailout(
                controlInstance, diagnosticContext, logger,
                Microsoft.UI.Reactor.Core.Diagnostics.KeyedListDiagnosticKind.DuplicateKey,
                CollectDuplicates(newKeys, newCount),
                indexHint: null,
                attribution);
            return Bailout(state, newItems, keySelector);
        }

        // ── Fast path 3: empty → non-empty (mount-shaped diff) ─────────
        if (oldCount == 0)
        {
            // We could call Reset, but that would clear Source via a bulk
            // change and require the caller to re-bind ItemsSource. Emitting
            // Insert ops preserves the OC binding (the WinUI ListView reacts
            // to the inserts as it would on a normal mount-time fill).
            for (int i = 0; i < newCount; i++)
            {
                var row = new ReactorRow { Index = i, Key = newKeys[i], PendingEnterAnimation = enterKind };
                state.Source.Add(row);
                state.ByKey[newKeys[i]] = row;
                state.LastKeys.Add(newKeys[i]);
            }
            return new DiffStats(Inserts: newCount, Removes: 0, Moves: 0, Survivors: 0, Bailout: false);
        }

        // ── Fast path 4: non-empty → empty ─────────────────────────────
        if (newCount == 0)
        {
            int removes = oldCount;
            // Descending RemoveAt keeps earlier indices stable.
            for (int i = oldCount - 1; i >= 0; i--)
                state.Source.RemoveAt(i);
            state.ByKey.Clear();
            state.LastKeys.Clear();
            return new DiffStats(Inserts: 0, Removes: removes, Moves: 0, Survivors: 0, Bailout: false);
        }

        // ── Fast path 5: single append ─────────────────────────────────
        // Single-op shapes run *before* the churn-bailout check so that
        // tiny lists (where 1 op is already >25%) never get punished by
        // the heuristic. We also save the scratch-dict allocation here.
        if (newCount == oldCount + 1
            && SequenceEqualOrdinalPrefix(state.LastKeys, newKeys, oldCount))
        {
            var row = new ReactorRow { Index = oldCount, Key = newKeys[oldCount], PendingEnterAnimation = enterKind };
            state.Source.Insert(oldCount, row);
            state.ByKey[newKeys[oldCount]] = row;
            state.LastKeys.Add(newKeys[oldCount]);
            return new DiffStats(Inserts: 1, Removes: 0, Moves: 0, Survivors: oldCount, Bailout: false);
        }

        // ── Fast path 6: single prepend ────────────────────────────────
        if (newCount == oldCount + 1
            && SequenceEqualOrdinalSuffix(state.LastKeys, newKeys, oldCount, newOffset: 1))
        {
            var row = new ReactorRow { Index = 0, Key = newKeys[0], PendingEnterAnimation = enterKind };
            state.Source.Insert(0, row);
            // Shift remembered indices forward by one.
            for (int i = 1; i < state.Source.Count; i++) state.Source[i].Index = i;
            state.ByKey[newKeys[0]] = row;
            state.LastKeys.Insert(0, newKeys[0]);
            return new DiffStats(Inserts: 1, Removes: 0, Moves: 0, Survivors: oldCount, Bailout: false);
        }

        // ── Fast path 7: single remove from end ────────────────────────
        if (newCount == oldCount - 1
            && SequenceEqualOrdinalPrefix(state.LastKeys, newKeys, newCount))
        {
            var removedKey = state.LastKeys[oldCount - 1];
            state.Source.RemoveAt(oldCount - 1);
            state.ByKey.Remove(removedKey);
            state.LastKeys.RemoveAt(oldCount - 1);
            return new DiffStats(Inserts: 0, Removes: 1, Moves: 0, Survivors: newCount, Bailout: false);
        }

        // ── Fast path 8: single remove from front ──────────────────────
        if (newCount == oldCount - 1
            && SequenceEqualOrdinalSuffix(state.LastKeys, newKeys, newCount, oldOffset: 1))
        {
            var removedKey = state.LastKeys[0];
            state.Source.RemoveAt(0);
            for (int i = 0; i < state.Source.Count; i++) state.Source[i].Index = i;
            state.ByKey.Remove(removedKey);
            state.LastKeys.RemoveAt(0);
            return new DiffStats(Inserts: 0, Removes: 1, Moves: 0, Survivors: newCount, Bailout: false);
        }

        // ── General case: React-style keyed diff ───────────────────────
        // The bulk-replace (churn) bailout decision now lives INSIDE
        // ApplyGeneral (#34): it is computed from the same scratch map the
        // general walk builds over the post-prefix/suffix diff range, so we
        // no longer pay a separate O(oldCount) null-marker pre-pass + a second
        // Scratch.Clear/repopulate. Churn over the diff range is provably
        // equal to full-range churn because the shared prefix/suffix keys
        // cancel on both sides (duplicates are already ruled out above).
        return ApplyGeneral(state, newItems, keySelector, newKeys, newCount, oldCount, enterKind);
    }

    /// <summary>
    /// General keyed diff over the post-prefix/suffix range. Builds the
    /// survivor map on <see cref="ReactorListState.Scratch"/> once, folds the
    /// churn-bailout decision into that same map, then emits the minimal
    /// Insert/Move/Remove op sequence. <paramref name="newKeys"/> is valid for
    /// <paramref name="newCount"/> entries (the buffer may be larger).
    /// </summary>
    private static DiffStats ApplyGeneral<T>(
        ReactorListState state,
        IReadOnlyList<T> newItems,
        Func<T, int, string> keySelector,
        string[] newKeys,
        int newCount,
        int oldCount,
        AnimationKind? enterKind)
    {
        // 1) Lockstep prefix walk — find where the lists first diverge.
        int prefix = 0;
        int sharedFromStart = global::System.Math.Min(oldCount, newCount);
        while (prefix < sharedFromStart && string.Equals(state.LastKeys[prefix], newKeys[prefix], global::System.StringComparison.Ordinal))
            prefix++;

        // 2) Lockstep suffix walk from the end. Bounded by `prefix` so we
        // never overlap into already-matched rows. Without this walk a
        // pure middle-remove like [a,b,c,d] → [a,b,d] would emit a
        // spurious Move("d", 3→2) followed by a Remove("c") — the suffix
        // walk catches "d" as already in place from the end and only
        // emits the Remove.
        int suffix = 0;
        while (suffix < oldCount - prefix
            && suffix < newCount - prefix
            && string.Equals(state.LastKeys[oldCount - 1 - suffix], newKeys[newCount - 1 - suffix], global::System.StringComparison.Ordinal))
            suffix++;

        // The diff range is [prefix .. (count - suffix)) on both sides.
        int oldDiffEnd = oldCount - suffix;
        int newDiffEnd = newCount - suffix;

        // 3) Build the remaining-old map for the diff range only.
        state.Scratch.Clear();
        for (int i = prefix; i < oldDiffEnd; i++)
            state.Scratch[state.LastKeys[i]] = state.Source[i];

        // 3a) Bulk-replace bailout (#34, folded in). Count how many new
        // diff-range keys survive (are present in the old diff-range map);
        // additions/removals follow without a second populate pass.
        int retained = 0;
        for (int i = prefix; i < newDiffEnd; i++)
            if (state.Scratch.ContainsKey(newKeys[i])) retained++;

        int additions = (newDiffEnd - prefix) - retained;
        int removals = (oldDiffEnd - prefix) - retained;
        int churn = additions + removals;
        const int BailoutFloor = 8;
        // Bailout if (churn / max(oldCount, 1)) > 0.25 AND churn >= floor.
        // Compute the ratio without floats: churn * 4 > oldCount.
        if (churn >= BailoutFloor && churn * 4 > global::System.Math.Max(oldCount, 1))
        {
            state.Scratch.Clear();
            return Bailout(state, newItems, keySelector);
        }

        int inserts = 0;
        int moves = 0;
        int survivors = prefix + suffix;
        // Collected lazily — only allocated when an ambient animation is
        // active AND at least one survivor actually moves (#35). Consumers
        // treat null as "no moved rows" (they gate on `is { Count: > 0 }`).
        List<ReactorRow>? movedRows = null;

        // 4) Walk new keys in the diff range.
        for (int desired = prefix; desired < newDiffEnd; desired++)
        {
            string key = newKeys[desired];
            if (state.Scratch.TryGetValue(key, out var survivor))
            {
                state.Scratch.Remove(key);
                int currentIndex = survivor.Index;
                if (currentIndex != desired)
                {
                    state.Source.Move(currentIndex, desired);
                    RefreshIndices(state, global::System.Math.Min(desired, currentIndex), global::System.Math.Max(desired, currentIndex));
                    moves++;
                    if (enterKind is not null)
                        (movedRows ??= new List<ReactorRow>()).Add(survivor);
                }
                survivors++;
            }
            else
            {
                var row = new ReactorRow { Index = desired, Key = key, PendingEnterAnimation = enterKind };
                state.Source.Insert(desired, row);
                state.ByKey[key] = row;
                RefreshIndices(state, desired, state.Source.Count - 1);
                inserts++;
            }
        }

        // 5) Whatever remains in the scratch map are removed rows. Remove
        // them in descending OC-index order so earlier indices stay stable.
        // The doomed buffer is rented from the pool and cleared+returned on
        // every exit (#3) so it never pins removed rows across frames.
        int removes = state.Scratch.Count;
        if (removes > 0)
        {
            ReactorRow[] doomed = ArrayPool<ReactorRow>.Shared.Rent(removes);
            try
            {
                int j = 0;
                foreach (var row in state.Scratch.Values) doomed[j++] = row;
                global::System.Array.Sort(doomed, 0, removes, RowIndexDescendingComparer.Instance);
                for (int i = 0; i < removes; i++)
                {
                    state.Source.RemoveAt(doomed[i].Index);
                    state.ByKey.Remove(doomed[i].Key);
                }
            }
            finally
            {
                ArrayPool<ReactorRow>.Shared.Return(doomed, clearArray: true);
            }
            RefreshIndices(state, 0, state.Source.Count - 1);
        }

        state.Scratch.Clear();

        // 6) Sync LastKeys to match the final Source order in place (#10),
        // overwriting shared slots and trimming/extending the tail rather
        // than Clear()+Add — which would null the whole backing array and
        // regrow it every diff.
        SyncLastKeysToSource(state);

        return new DiffStats(
            Inserts: inserts,
            Removes: removes,
            Moves: moves,
            Survivors: survivors,
            Bailout: false,
            MovedRows: movedRows);
    }

    /// <summary>
    /// Overwrites <see cref="ReactorListState.LastKeys"/> to match the current
    /// <see cref="ReactorListState.Source"/> key order without the full
    /// Clear()+Add churn. Shared leading slots are overwritten; the tail is
    /// trimmed or extended as needed. Always leaves LastKeys exactly equal to
    /// the Source key sequence, so it cannot desync.
    /// </summary>
    private static void SyncLastKeysToSource(ReactorListState state)
    {
        var lastKeys = state.LastKeys;
        var source = state.Source;
        int srcCount = source.Count;
        int common = global::System.Math.Min(lastKeys.Count, srcCount);
        for (int i = 0; i < common; i++)
            lastKeys[i] = source[i].Key;
        if (lastKeys.Count > srcCount)
            lastKeys.RemoveRange(srcCount, lastKeys.Count - srcCount);
        else
            for (int i = lastKeys.Count; i < srcCount; i++)
                lastKeys.Add(source[i].Key);
    }

    private static void RefreshIndices(ReactorListState state, int fromInclusive, int toInclusive)
    {
        if (toInclusive >= state.Source.Count) toInclusive = state.Source.Count - 1;
        if (fromInclusive < 0) fromInclusive = 0;
        for (int i = fromInclusive; i <= toInclusive; i++)
            state.Source[i].Index = i;
    }

    private static DiffStats Bailout<T>(
        ReactorListState state,
        IReadOnlyList<T> newItems,
        Func<T, int, string> keySelector)
    {
        // Rebuild from scratch with a fresh Source. Caller is expected to
        // re-bind ItemsSource to the new Source instance — that is the only
        // way to surface the bulk change without leaving the WinUI ListView
        // in an inconsistent in-between state during recovery.
        var rebuilt = new List<(int, string)>(newItems.Count);
        for (int i = 0; i < newItems.Count; i++)
        {
            // KeySelector can still throw if user code is broken; let it
            // propagate so the broader error-boundary path handles it.
            var k = keySelector(newItems[i], i);
            // Synthesize a sentinel for null / duplicate to keep Reset
            // producing a coherent collection (Reset tolerates duplicates).
            rebuilt.Add((i, StateKey(k, i)));
        }
        state.Reset(rebuilt);
        return new DiffStats(
            Inserts: newItems.Count,
            Removes: 0,
            Moves: 0,
            Survivors: 0,
            Bailout: true);
    }

    private static bool HasDuplicates(string[] keys, int count, ReactorListState state)
    {
        if (count < 2) return false;
        // Small-N optimization: 0/1 already handled, 2..3 is a quick scan
        // that never touches the shared scratch dict.
        if (count <= 3)
        {
            if (count == 2) return string.Equals(keys[0], keys[1], global::System.StringComparison.Ordinal);
            // count == 3
            return string.Equals(keys[0], keys[1], global::System.StringComparison.Ordinal)
                || string.Equals(keys[0], keys[2], global::System.StringComparison.Ordinal)
                || string.Equals(keys[1], keys[2], global::System.StringComparison.Ordinal);
        }
        // Reuse the pooled scratch dict (#11) instead of allocating a fresh
        // HashSet every diff with 4+ keys. Cleared on entry and on every exit
        // so it never leaks into the survivor-map build that follows.
        var scratch = state.Scratch;
        scratch.Clear();
        for (int i = 0; i < count; i++)
        {
            if (!scratch.TryAdd(keys[i], null!))
            {
                scratch.Clear();
                return true;
            }
        }
        scratch.Clear();
        return false;
    }

    private static bool SequenceEqualOrdinal(List<string> a, string[] b, int count)
    {
        if (a.Count != count) return false;
        for (int i = 0; i < count; i++)
            if (!string.Equals(a[i], b[i], global::System.StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool SequenceEqualOrdinalPrefix(List<string> a, string[] b, int count)
    {
        for (int i = 0; i < count; i++)
            if (!string.Equals(a[i], b[i], global::System.StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool SequenceEqualOrdinalSuffix(List<string> a, string[] b, int count, int newOffset)
    {
        for (int i = 0; i < count; i++)
            if (!string.Equals(a[i], b[newOffset + i], global::System.StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool SequenceEqualOrdinalSuffix(List<string> a, string[] b, int count, int oldOffset, int newOffset = 0)
    {
        for (int i = 0; i < count; i++)
            if (!string.Equals(a[oldOffset + i], b[newOffset + i], global::System.StringComparison.Ordinal)) return false;
        return true;
    }

    // ── Diagnostics ────────────────────────────────────────────────────
    // Bailouts feed both the structured `ReactorDiagnostics` collector
    // (the dev overlay reads this) and an optional `ILogger` (the unified
    // host log stream). Both paths dedup on the first occurrence of a
    // (controlInstance, kind, sample-set) triple — the collector tracks the
    // dedup ledger; the logger gate falls out of the collector's
    // `IsFirstOccurrence` check.
    private static void ReportBailout(
        object? controlInstance,
        string? diagnosticContext,
        ILogger? logger,
        Microsoft.UI.Reactor.Core.Diagnostics.KeyedListDiagnosticKind kind,
        IReadOnlyList<string> sampleKeys,
        int? indexHint,
        KeyedListAttribution attribution,
        bool bailedOut = true)
    {
        bool isFirst = Microsoft.UI.Reactor.Core.Diagnostics.ReactorDiagnostics
            .IsFirstOccurrence(controlInstance, kind, sampleKeys, diagnosticContext);

        Microsoft.UI.Reactor.Core.Diagnostics.ReactorDiagnostics
            .Record(controlInstance, diagnosticContext, kind, sampleKeys, attribution.ComponentName, attribution.CallSite);

        // Only emit the structured ILogger warning on the *first* occurrence
        // per triple. Subsequent occurrences still increment the collector's
        // Count so the dev overlay accurately tracks repeat frequency.
        if (!isFirst) return;
        if (logger is null || !logger.IsEnabled(LogLevel.Warning)) return;

        string reason = kind == Microsoft.UI.Reactor.Core.Diagnostics.KeyedListDiagnosticKind.NullKey
            ? (indexHint is { } i ? $"null key at index {i}" : "null key")
            : $"duplicate keys: {LogKeys(sampleKeys)}";

        if (!bailedOut)
        {
            logger.LogWarning(
                "Reactor: invalid list key — {Reason}. Context: {Context}. Component: {Component}. At: {CallSite}. " +
                "Keys must be non-null and unique: a null-keyed item is matched by position, and a list with " +
                "duplicate keys is rebuilt in full whenever it changes, so row state can move between items. " +
                "Check the KeySelector / IReactorKeyed.Key. See spec 042 §4.3.",
                reason,
                diagnosticContext ?? "<unknown>",
                attribution.ComponentName ?? "<unknown>",
                LogSite(attribution.CallSite));
            return;
        }

        logger.LogWarning(
            "Reactor: keyed-list diff bailed out — {Reason}. Context: {Context}. Component: {Component}. At: {CallSite}. " +
            "The list will be re-realized from scratch this render (animations degraded). " +
            "Check the KeySelector / IReactorKeyed.Key for stability and uniqueness. " +
            "See spec 042 §4.3.",
            reason,
            diagnosticContext ?? "<unknown>",
            attribution.ComponentName ?? "<unknown>",
            LogSite(attribution.CallSite));
    }

    /// <summary>
    /// A call site for a log line: file name and line only. Never the absolute compile-time
    /// path, which on a developer machine names local directories (the published
    /// <c>ReactorSource</c> value is relativized for the same reason).
    /// </summary>
    private static string LogSite(SourceLocation? site) => site is { } s ? s.ToShortString() : "<unknown>";

    /// <summary>At most <see cref="Diagnostics.ReactorDiagnostics.MaxSampleKeys"/> keys, for a log line.</summary>
    private static string LogKeys(IReadOnlyList<string> keys)
    {
        int max = Microsoft.UI.Reactor.Core.Diagnostics.ReactorDiagnostics.MaxSampleKeys;
        return keys.Count <= max
            ? string.Join(", ", keys)
            : string.Join(", ", keys.Take(max)) + $", … ({keys.Count - max} more)";
    }

    /// <summary>
    /// Reports a <c>null</c> key that is matched by position instead of bailing out (by
    /// <see cref="Apply{T}"/> and the mount-time seed). Not
    /// bailing out keeps the incremental diff for a list that permanently has a null key: a
    /// bailout would reset — and re-realize — the whole list on every render. Deduplicated per
    /// control like every keyed-list diagnostic.
    /// </summary>
    internal static void ReportNullKey(
        object? controlInstance, string? diagnosticContext, ILogger? logger, int index, KeyedListAttribution attribution)
        => ReportBailout(
            controlInstance, diagnosticContext, logger,
            Microsoft.UI.Reactor.Core.Diagnostics.KeyedListDiagnosticKind.NullKey,
            NullKeySample,
            indexHint: index,
            attribution,
            bailedOut: false);

    /// <summary>
    /// The positional placeholder key a null key is replaced by. Shared by the mount-time seed,
    /// the bailout rebuild and the substituting diff: if two of them disagreed, the first update
    /// would remove and re-add every null-keyed row. It starts with <c>'\0'</c> followed by a
    /// digit, a form <see cref="StateKey"/> never gives an authored key, so it cannot collide.
    /// </summary>
    internal static string NullKeySentinel(int index)
        => "\0" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The key a list state holds for an item: the authored key, or <see cref="NullKeySentinel"/>
    /// for a null one. An authored key that itself starts with <c>'\0'</c> gets a second
    /// <c>'\0'</c>, so no authored key can equal a placeholder (whose second character is a
    /// digit). Injective, so duplicates among authored keys are duplicates among state keys.
    /// </summary>
    internal static string StateKey(string? key, int index)
        => key is null ? NullKeySentinel(index)
            : key.Length > 0 && key[0] == '\0' ? "\0" + key
            : key;

    /// <summary>The authored key a non-placeholder <see cref="StateKey"/> stands for (for reports).</summary>
    internal static string AuthoredKey(string stateKey)
        => stateKey.Length > 1 && stateKey[0] == '\0' && stateKey[1] == '\0' ? stateKey.Substring(1) : stateKey;

    /// <summary>
    /// Seeds a fresh list state's keys (at mount, or when a list is rebound) and reports what
    /// <see cref="Apply{T}"/> would report for them: a <c>null</c> key — replaced by a
    /// positional sentinel so the state stays coherent — and duplicated keys. Without this a
    /// list born with a bad key, or one that never changes shape, is never diagnosed, because
    /// only an update runs the diff.
    /// </summary>
    internal static (int Index, string Key)[] SeedKeys<TSource>(
        TSource source,
        int count,
        Func<TSource, int, string?> keyAt,
        ILogger? logger,
        string? diagnosticContext,
        object? controlInstance,
        KeyedListAttribution attribution)
    {
        var seeded = new (int Index, string Key)[count];
        int firstNull = -1;
        HashSet<string>? dupes = null;
        // Per-thread and cleared after use: a mount seeds once per list, so a pooled set keeps
        // a large list's mount from allocating a set as large as the list.
        var seen = count > 1 ? (t_seedKeys ??= new HashSet<string>(StringComparer.Ordinal)) : null;
        if (seen is not null) t_seedKeys = null; // taken: a re-entrant seed allocates its own
        try
        {
            for (int i = 0; i < count; i++)
            {
                var key = keyAt(source, i);
                if (key is null)
                {
                    if (firstNull < 0) firstNull = i;
                    seeded[i] = (i, NullKeySentinel(i));
                    continue;
                }
                seeded[i] = (i, StateKey(key, i));
                if (seen is not null && !seen.Add(key))
                    (dupes ??= new(StringComparer.Ordinal)).Add(key);
            }
        }
        finally
        {
            // Keep only a modest set for reuse: Clear() does not shrink, so a set grown by one
            // very large list would otherwise stay that large for the thread's lifetime.
            if (seen is not null && seen.Count <= PooledKeySetLimit)
            {
                seen.Clear();
                t_seedKeys = seen;
            }
        }
        if (firstNull >= 0)
        {
            ReportBailout(
                controlInstance, diagnosticContext, logger,
                Microsoft.UI.Reactor.Core.Diagnostics.KeyedListDiagnosticKind.NullKey,
                NullKeySample,
                indexHint: firstNull,
                attribution,
                bailedOut: false);
        }
        if (dupes is not null)
        {
            ReportBailout(
                controlInstance, diagnosticContext, logger,
                Microsoft.UI.Reactor.Core.Diagnostics.KeyedListDiagnosticKind.DuplicateKey,
                SortedSample(dupes),
                indexHint: null,
                attribution,
                bailedOut: false);
        }
        return seeded;
    }

    /// <summary>
    /// Reports duplicated <see cref="Element.Key"/>s among one panel's children (for example
    /// two <c>.WithKey("same")</c> siblings in a <c>VStack</c>). The keyed child reconcile
    /// matches by key, so a duplicate silently pairs the wrong controls. Allocation-free for
    /// small panels; callers skip it when the sibling keys did not change.
    /// </summary>
    internal static void ReportDuplicateSiblingKeys(
        IReadOnlyList<Element?> children,
        Reconciler? reconciler,
        object? parentControl)
    {
        int count = children.Count;
        if (count < 2) return;
        HashSet<string>? dupes = null;
        Element? firstDuplicate = null;
        if (count <= 16)
        {
            for (int i = 1; i < count; i++)
            {
                if (children[i] is not { Key: { } key } child || child is EmptyElement) continue;
                for (int j = 0; j < i; j++)
                {
                    if (children[j] is { Key: { } other } prior && prior is not EmptyElement
                        && string.Equals(key, other, StringComparison.Ordinal))
                    {
                        firstDuplicate ??= child;
                        (dupes ??= new(StringComparer.Ordinal)).Add(key);
                        break;
                    }
                }
            }
        }
        else
        {
            // Not re-entrant (nothing below calls back into a reconcile), so one per-thread set
            // serves every call; it is cleared before it is returned to.
            var seen = t_siblingKeys ??= new HashSet<string>(StringComparer.Ordinal);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (children[i] is not { Key: { } key } child || child is EmptyElement) continue;
                    if (!seen.Add(key))
                    {
                        firstDuplicate ??= child;
                        (dupes ??= new(StringComparer.Ordinal)).Add(key);
                    }
                }
            }
            finally
            {
                if (seen.Count > PooledKeySetLimit) t_siblingKeys = null;
                else seen.Clear();
            }
        }
        if (dupes is null) return;
        var sample = SortedSample(dupes);

        var attribution = new KeyedListAttribution(reconciler?.DiagnosticComponent, KeyedListAttribution.CallSiteOf(firstDuplicate));
        var context = parentControl?.GetType().Name ?? "Panel";
        var kind = Microsoft.UI.Reactor.Core.Diagnostics.KeyedListDiagnosticKind.DuplicateKey;

        bool isFirst = Microsoft.UI.Reactor.Core.Diagnostics.ReactorDiagnostics
            .IsFirstOccurrence(parentControl, kind, sample, context);
        Microsoft.UI.Reactor.Core.Diagnostics.ReactorDiagnostics
            .Record(parentControl, context, kind, sample, attribution.ComponentName, attribution.CallSite);

        var logger = reconciler?._logger;
        if (!isFirst || logger is null || !logger.IsEnabled(LogLevel.Warning)) return;
        logger.LogWarning(
            "Reactor: duplicate child keys: {Keys}. Context: {Context}. Component: {Component}. At: {CallSite}. " +
            "Sibling keys must be unique; children with the same key are matched to the wrong controls " +
            "when the panel re-renders, so state and focus can move between them.",
            LogKeys(sample),
            context,
            attribution.ComponentName ?? "<unknown>",
            LogSite(attribution.CallSite));
    }
    /// <summary>
    /// The distinct duplicated keys as a report sample, sorted as <see cref="CollectDuplicates"/>
    /// sorts the diff's: the dedup ledger is order sensitive, so every report of the same
    /// duplicates (mount seed, update diff, sibling scan) must name the same sample.
    /// </summary>
    private static List<string> SortedSample(HashSet<string> dupes)
    {
        var sample = new List<string>(dupes);
        sample.Sort(StringComparer.Ordinal);
        return sample;
    }

    // Walk newKeys once collecting the *distinct* duplicate values
    // (not every occurrence). For a list with [a, b, a, c, b] returns
    // [a, b] — the dev overlay reader cares about which keys collided,
    // not the per-occurrence count.
    private static IReadOnlyList<string> CollectDuplicates(string[] keys, int count)
    {
        // Small set — duplicates in production are rare and the dev surface
        // caps the displayed list anyway. This is a slow (bailout) path so
        // the throwaway HashSets are acceptable.
        var seen = new HashSet<string>(global::System.StringComparer.Ordinal);
        var dupes = new HashSet<string>(global::System.StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            if (!seen.Add(keys[i])) dupes.Add(keys[i]);
        }
        if (dupes.Count == 0) return global::System.Array.Empty<string>();
        var arr = new string[dupes.Count];
        int j = 0;
        foreach (var d in dupes) arr[j++] = AuthoredKey(d);
        global::System.Array.Sort(arr, global::System.StringComparer.Ordinal);
        return arr;
    }
}
