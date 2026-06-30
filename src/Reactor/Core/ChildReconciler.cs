using System.Buffers;
using System.Collections.Concurrent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Reactor.Core.Internal;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

// AI-HINT: React-like child list diffing. Two strategies:
//   Unkeyed: O(n) positional match by index — simple but can't detect reorders.
//   Keyed:   4-phase algorithm — (1) common prefix, (2) common suffix,
//            (3) pure insert/remove middle, (4) LIS-based minimal moves.
//   LIS finds the longest subsequence of old children that are already in order
//   in the new list; only children NOT in the LIS need to be moved.
//   UnmountAndPool returns controls to ElementPool for reuse.

/// <summary>
/// Keyed child reconciliation using Longest Increasing Subsequence (LIS).
///
/// Strategies:
///   1. Unkeyed children: positional reconciliation (match by index)
///   2. Keyed children: prefix/suffix stripping + LIS for minimal moves
/// </summary>
internal static class ChildReconciler
{
    /// <summary>
    /// Reconciles old and new child element arrays against a Panel's Children collection.
    /// </summary>
    // <snippet:child-diff>
    internal static void Reconcile(
        Element[] oldChildren,
        Element[] newChildren,
        IChildCollection children,
        Reconciler reconciler,
        Action requestRerender,
        UIElement? parentControl = null)
    {
        // Filter out nulls and EmptyElements
        var oldFiltered = Filter(oldChildren);
        var newFiltered = Filter(newChildren);

        bool hasKeys = HasAnyKeys(oldFiltered) || HasAnyKeys(newFiltered);

        // Spec 042 §6 — read the active Animations.Animate ambient once
        // per reconcile so insert / move / unmount paths can apply the
        // same kind without re-reading AsyncLocal for every child. Stays
        // null in the overwhelmingly common no-ambient case.
        var ambient = AnimationAmbient.Current;
        AnimationKind? ambientKind = ambient is { HasEffect: true } ? ambient.Kind : null;

        if (hasKeys)
            ReconcileKeyed(oldFiltered, newFiltered, children, reconciler, requestRerender, ambientKind);
        else
            ReconcilePositional(oldFiltered, newFiltered, children, reconciler, requestRerender, ambientKind, parentControl);
    }
    // </snippet:child-diff>

    /// <summary>
    /// Positional reconciliation: match children by index.
    /// O(max(old, new)) — no reorder detection, but simple and fast.
    /// </summary>
    private static void ReconcilePositional(
        Element[] oldChildren,
        Element[] newChildren,
        IChildCollection children,
        Reconciler reconciler,
        Action requestRerender,
        AnimationKind? ambientKind,
        UIElement? parentControl)
    {
        int childCount = children.Count; // cache to avoid repeated COM calls
        int common = Math.Min(oldChildren.Length, newChildren.Length);

        // PR-C (Spec 034 §C) — structural skip of untouched child ranges.
        // When a memoizing producer (UseMemoCellsByIndex) publishes a hint, only
        // the named indices differ (by reference) from the previous render; every
        // other cell is provably reference-equal, so we update just those indices
        // and skip the O(count) walk. Required gates (each preserves correctness):
        //   • counts match on both element sides AND the live collection equals
        //     that count (no in-flight exit/enter animation has inflated it) — so
        //     every changed index maps 1:1 to an existing control;
        //   • no animation ambient (insert / move / exit reshape the child list);
        //   • a hint is present for THIS array — a CWT hit also proves Filter
        //     returned the same reference (no null/EmptyElement shifted the index
        //     space), so the hint's indices line up with both filtered arrays;
        //   • no hot-reload force pass is active — else an untouched, reference-equal
        //     WRAPPER cell (Component/Memo/Func) must still re-render through the
        //     wrapper to pick up an edited method body; a structural skip would
        //     swallow it (the full walk honours ForceRenderThroughWrapper per cell);
        //   • the reconciler's OLD array IS the array the hint's indices were diffed
        //     against — a cheap, self-documenting guard for the real invariant that
        //     every unchanged index is reference-equal old↔new (holds in steady state
        //     by construction; any defensive copy safely falls back to the full walk);
        //   • no cell is theme-sensitive — else an untouched, reference-equal cell
        //     could still need ApplyThemeBindings / ApplyResourceOverrides
        //     re-resolved against an effective theme that a parent RequestedTheme
        //     toggle changed without touching the element tree;
        //   • the container is not on #681's dirty-ancestor path — conservative
        //     defense-in-depth for a self-triggered descendant (e.g. a stateful
        //     memoized cell). NOTE: given the gates above this is behaviorally
        //     redundant — an untouched index is reference-equal old↔new, and a
        //     reference-equal cell is skipped IDENTICALLY by the full walk (via
        //     Element.CanSkipUpdate in UpdateCommonChild) and this fast path, so a
        //     self-triggered reused cell re-renders (or not) through the SAME
        //     top-level dirty descent either way. The gate is retained as cheap
        //     insurance so the fast path's early return can never short-circuit a
        //     dirty subtree if CanSkipUpdate's contract changes. It costs nothing on
        //     the target workload: a memoized grid's cell panel is a DESCENDANT of
        //     the self-triggered grid component, not an ancestor, so it is not on the
        //     ancestor-only dirty path and the fast path still engages.
        if (ambientKind is null
            && oldChildren.Length == newChildren.Length
            && childCount == newChildren.Length
            && !reconciler.ForceFullRenderActive
            && ChildDiffHints.TryGet(newChildren, out var hint)
            && !hint.AnyThemeSensitive
            && hint.PreviousChildren.TryGetTarget(out var hintPrev)
            && ReferenceEquals(oldChildren, hintPrev)
            && !reconciler.IsOnDirtyAncestorPath(parentControl))
        {
            var changed = hint.ChangedIndices;
            int visited = 0;
            for (int k = 0; k < changed.Length; k++)
            {
                int idx = changed[k];
                if ((uint)idx >= (uint)common) continue; // defensive against a bad hint
                UpdateCommonChild(idx, oldChildren, newChildren, children, reconciler, requestRerender);
                visited++;
            }
            // Untouched indices are reference-equal and skipped wholesale. Base the
            // skipped-element diagnostic on indices ACTUALLY visited (not the raw
            // hint length) so a defensively-ignored out-of-range index can't skew it
            // or drive it negative. The producer publishes deduped, in-range indices,
            // so visited == the real changed count in steady state; this only hardens
            // the directly-supplied-hint path.
            // #721 — no gesture/drag dispatch-state refresh is needed for the skipped
            // (untouched) indices: they are reference-equal old↔new, so their gesture/
            // drag closures are the SAME instances already cached — there is no stale
            // capture to refresh. Only the changed indices (handled by UpdateCommonChild
            // above, which refreshes on its own skip arm) can carry a new closure.
            reconciler.DebugElementsSkipped += common - visited;
            return;
        }

        // Update common children in place
        for (int i = 0; i < common; i++)
        {
            if (i >= childCount) break;
            UpdateCommonChild(i, oldChildren, newChildren, children, reconciler, requestRerender);
        }

        // Remove excess old children (from end to start to keep indices stable).
        // Use live children.Count — NOT the cached childCount — because
        // ReplaceChildWithExitTransition in the common loop may have re-inserted
        // elements for exit animation, increasing the actual count.
        for (int i = children.Count - 1; i >= common; i--)
        {
            reconciler.RemoveChildWithExitTransition(children, i);
        }

        // Insert new children beyond old count
        for (int i = common; i < newChildren.Length; i++)
        {
            var ctrl = reconciler.Mount(newChildren[i], requestRerender);
            if (ctrl is not null)
            {
                children.Insert(children.Count, ctrl);
                ApplyAmbientEnterIfActive(ctrl, newChildren[i], ambientKind);
            }
        }
    }

    /// <summary>
    /// Reconciles a single common (overlapping) child position in place. Shared by
    /// the full positional walk and the PR-C structural-skip fast path so both
    /// honour identical skip / update / type-mismatch semantics.
    /// </summary>
    private static void UpdateCommonChild(
        int i,
        Element[] oldChildren,
        Element[] newChildren,
        IChildCollection children,
        Reconciler reconciler,
        Action requestRerender)
    {
        // Early skip: if the element is structurally identical (and carries no
        // theme-reactive resources that need re-evaluation), we can avoid the
        // expensive children.Get(i) COM call entirely. This saves ~2 COM roundtrips
        // per unchanged child (IVector.GetAt + all the property diffing inside Update).
        //
        // Issue #675 — the skip contract lives entirely in Element.CanSkipUpdate
        // (shared by the positional + keyed prefix/suffix arms): it declines the skip
        // for any element carrying ThemeBindings OR ResourceOverrides.ThemeRefs, so a
        // theme-sensitive child falls through to UpdateChild → Update, whose
        // element-level shallow-skip re-resolves the themed value. Reconciler.Update
        // is therefore the single place that performs skip-path theme re-resolution.
        //
        // We deliberately do NOT also gate this arm on IsOnDirtyAncestorPath (the
        // element-level skip in Update.cs does). It is provably unnecessary here:
        // every container arm in Element.ShallowEquals compares its children/child BY
        // REFERENCE, and Component/Memo/Func wrappers return false — so any element
        // that is CanSkipUpdate-eligible is either a leaf or a reference-equal-children
        // container, neither of which can be a STRICT ANCESTOR of a freshly re-rendered
        // self-triggered descendant (that ancestor would carry a new children reference
        // and fail ShallowEquals). Adding a dirty-ancestor check would only force a
        // COM fetch on the hot skip-floor for a path that cannot be reached.
        var oldEl = oldChildren[i];
        var newEl = newChildren[i];
        if (Element.CanSkipUpdate(oldEl, newEl)
            && !reconciler.ForceRenderThroughWrapper(newEl))
        {
            reconciler.DebugElementsSkipped++;
            // Refresh Tag when the element carries callbacks. The skip short-
            // circuits Update, so without this the event trampoline keeps
            // dispatching through the previous render's closure — stale state
            // (e.g., Counter's `() => setCount(count + 1)` would keep capturing
            // the initial count). For callback-free elements we still avoid
            // the children.Get COM call.
            // #721 — the gesture/drag slots are excluded from the skip predicate,
            // so refresh their cached dispatch closures here too (same stale-closure
            // hazard as Tag). Fetch the control once for either need.
            if ((newEl.HasCallbacks
                    || Reconciler.HasGestureOrDragSlots(newEl.Modifiers)
                    || Reconciler.HasGestureOrDragSlots(oldEl.Modifiers))
                && children.Get(i) is FrameworkElement fe)
            {
                if (newEl.HasCallbacks)
                    Reconciler.SetElementTag(fe, newEl);
                Reconciler.RefreshGestureDragStateOnSkip(fe, oldEl.Modifiers, newEl.Modifiers);
            }
            return;
        }

        if (reconciler.CanUpdate(oldEl, newEl))
        {
            var existingControl = children.Get(i);
            var replacement = reconciler.UpdateChild(oldEl, newEl, existingControl, requestRerender);
            if (replacement is not null)
            {
                // Child type changed at runtime — replace in place
                reconciler.UnmountChild(existingControl);
                children.Replace(i, replacement);
            }
        }
        else
        {
            // Type mismatch — mount new, replace old with exit transition support
            var newControl = reconciler.Mount(newEl, requestRerender);
            if (newControl is not null)
                reconciler.ReplaceChildWithExitTransition(children, i, newControl);
            else
            {
                reconciler.UnmountChild(children.Get(i));
            }
        }
    }

    /// <summary>
    /// Keyed reconciliation using prefix/suffix stripping + LIS.
    /// Minimizes DOM operations for reordered lists.
    /// </summary>
    private static void ReconcileKeyed(
        Element[] oldChildren,
        Element[] newChildren,
        IChildCollection children,
        Reconciler reconciler,
        Action requestRerender,
        AnimationKind? ambientKind)
    {
        int oldLen = oldChildren.Length;
        int newLen = newChildren.Length;

        // Cache the child count once (#37). Both the prefix and suffix loops
        // only ever Replace (RemoveAt+Insert — count-neutral) or skip, so the
        // panel count is stable across them. This avoids re-reading the COM
        // IVector.get_Size on every iteration of the suffix loop.
        int childCount = children.Count;

        // Phase 1: Common prefix
        int prefixLen = 0;
        while (prefixLen < oldLen && prefixLen < newLen &&
               KeyMatch(oldChildren[prefixLen], newChildren[prefixLen]) &&
               reconciler.CanUpdate(oldChildren[prefixLen], newChildren[prefixLen]))
        {
            var oldEl = oldChildren[prefixLen];
            var newEl = newChildren[prefixLen];

            // Early skip (#30): when the element is structurally identical we
            // can avoid the children.Get COM call and the full property diff
            // inside UpdateChild — exactly mirroring the positional path. The
            // keyed prefix is the steady-state path for stable lists (e.g. grid
            // rows whose keys never change), so without this they re-diff every
            // tick.
            // Issue #675 — shares Element.CanSkipUpdate with the positional arm, so a
            // ThemeBindings/ResourceOverrides.ThemeRefs child likewise declines the
            // skip and re-resolves through Update (see UpdateCommonChild's contract note).
            if (Element.CanSkipUpdate(oldEl, newEl)
                && !reconciler.ForceRenderThroughWrapper(newEl))
            {
                reconciler.DebugElementsSkipped++;
                // Refresh the Tag when the element carries callbacks so the
                // event trampoline dispatches through the latest closure.
                // #721 — likewise refresh the cached gesture/drag dispatch closures
                // (excluded from the skip predicate). Fetch the control once.
                if ((newEl.HasCallbacks
                        || Reconciler.HasGestureOrDragSlots(newEl.Modifiers)
                        || Reconciler.HasGestureOrDragSlots(oldEl.Modifiers))
                    && prefixLen < childCount && children.Get(prefixLen) is FrameworkElement fe)
                {
                    if (newEl.HasCallbacks)
                        Reconciler.SetElementTag(fe, newEl);
                    Reconciler.RefreshGestureDragStateOnSkip(fe, oldEl.Modifiers, newEl.Modifiers);
                }
                prefixLen++;
                continue;
            }

            // Update in place
            if (prefixLen < childCount)
            {
                var replacement = reconciler.UpdateChild(oldEl, newEl, children.Get(prefixLen), requestRerender);
                if (replacement is not null)
                {
                    reconciler.UnmountChild(children.Get(prefixLen));
                    children.Replace(prefixLen, replacement);
                }
            }
            prefixLen++;
        }

        // Phase 2: Common suffix
        int suffixLen = 0;
        while (suffixLen < (oldLen - prefixLen) && suffixLen < (newLen - prefixLen) &&
               KeyMatch(oldChildren[oldLen - 1 - suffixLen], newChildren[newLen - 1 - suffixLen]) &&
               reconciler.CanUpdate(oldChildren[oldLen - 1 - suffixLen], newChildren[newLen - 1 - suffixLen]))
        {
            // Update in place (from end)
            int oldIdx = oldLen - 1 - suffixLen;
            int newIdx = newLen - 1 - suffixLen;
            var oldEl = oldChildren[oldIdx];
            var newEl = newChildren[newIdx];
            int panelIdx = childCount - 1 - suffixLen;

            // Early skip (#30): same fast-path as the prefix loop, applied from
            // the end of the list.
            if (Element.CanSkipUpdate(oldEl, newEl)
                && !reconciler.ForceRenderThroughWrapper(newEl))
            {
                reconciler.DebugElementsSkipped++;
                // #721 — refresh Tag + cached gesture/drag dispatch closures on skip.
                if ((newEl.HasCallbacks
                        || Reconciler.HasGestureOrDragSlots(newEl.Modifiers)
                        || Reconciler.HasGestureOrDragSlots(oldEl.Modifiers))
                    && panelIdx >= 0 && panelIdx < childCount && children.Get(panelIdx) is FrameworkElement fe)
                {
                    if (newEl.HasCallbacks)
                        Reconciler.SetElementTag(fe, newEl);
                    Reconciler.RefreshGestureDragStateOnSkip(fe, oldEl.Modifiers, newEl.Modifiers);
                }
                suffixLen++;
                continue;
            }

            if (panelIdx >= 0 && panelIdx < childCount)
            {
                var replacement = reconciler.UpdateChild(oldEl, newEl, children.Get(panelIdx), requestRerender);
                if (replacement is not null)
                {
                    reconciler.UnmountChild(children.Get(panelIdx));
                    children.Replace(panelIdx, replacement);
                }
            }
            suffixLen++;
        }

        // Phase 3: Middle section
        int oldStart = prefixLen;
        int oldEnd = oldLen - suffixLen;
        int newStart = prefixLen;
        int newEnd = newLen - suffixLen;

        int oldMidLen = oldEnd - oldStart;
        int newMidLen = newEnd - newStart;

        if (oldMidLen == 0 && newMidLen == 0)
            return; // Prefix + suffix covered everything

        if (oldMidLen == 0)
        {
            // Only insertions
            for (int i = 0; i < newMidLen; i++)
            {
                var ctrl = reconciler.Mount(newChildren[newStart + i], requestRerender);
                if (ctrl is not null)
                {
                    children.Insert(prefixLen + i, ctrl);
                    ApplyAmbientEnterIfActive(ctrl, newChildren[newStart + i], ambientKind);
                }
            }
            return;
        }

        if (newMidLen == 0)
        {
            // Only removals (from end to start)
            for (int i = oldMidLen - 1; i >= 0; i--)
            {
                int panelIdx = prefixLen + i;
                if (panelIdx < children.Count)
                {
                    reconciler.RemoveChildWithExitTransition(children, panelIdx);
                }
            }
            return;
        }

        // Middle section requires key mapping + LIS
        ReconcileKeyedMiddle(oldChildren, newChildren, oldStart, oldMidLen, newStart, newMidLen,
            prefixLen, suffixLen, children, reconciler, requestRerender, ambientKind);
    }

    /// <summary>
    /// Keyed middle section reconciliation using LIS for minimal moves.
    /// </summary>
    private static void ReconcileKeyedMiddle(
        Element[] oldChildren, Element[] newChildren,
        int oldStart, int oldMidLen, int newStart, int newMidLen,
        int prefixLen, int suffixLen,
        IChildCollection children,
        Reconciler reconciler,
        Action requestRerender,
        AnimationKind? ambientKind)
    {
        // Pool the per-call working buffers (#33). ChildReconciler recurses
        // via UpdateChild below, so each (re-entrant) call must own distinct
        // buffers — ArrayPool and the dict pool guarantee that, whereas a
        // single ThreadStatic scratch would corrupt the outer diff.
        var intPool = ArrayPool<int>.Shared;
        var boolPool = ArrayPool<bool>.Shared;
        var oldKeyMap = RentKeyIndexDict(oldMidLen);
        int[] newToOld = intPool.Rent(newMidLen);
        bool[] matched = boolPool.Rent(oldMidLen);
        bool[] inLis = boolPool.Rent(newMidLen);
        // Live panel index of each surviving old item, keyed by old-relative
        // index (-1 when absent). Replaces the second pooled key→index dict
        // (#33): positions are tracked here and mutated in lock-step with every
        // Insert/Move so a survivor's current control is always resolvable
        // without a COM re-scan — and never patched by a not-yet-reached final
        // index (bug C1).
        int[] oldRelToPanel = intPool.Rent(oldMidLen);
        try
        {
            // A rented bool array can carry stale `true` markers; `matched`
            // must default to false for the unmatched-removal pass below.
            global::System.Array.Clear(matched, 0, oldMidLen);

            // Build old key → index map
            for (int i = 0; i < oldMidLen; i++)
            {
                var key = GetKey(oldChildren[oldStart + i], oldStart + i);
                oldKeyMap[key] = i;
            }

            // Map new keys to old indices
            for (int i = 0; i < newMidLen; i++)
            {
                var key = GetKey(newChildren[newStart + i], newStart + i);
                if (oldKeyMap.TryGetValue(key, out int oldIdx) &&
                    reconciler.CanUpdate(oldChildren[oldStart + oldIdx], newChildren[newStart + i]))
                {
                    newToOld[i] = oldIdx;
                    matched[oldIdx] = true;
                }
                else
                {
                    newToOld[i] = -1;
                }
            }

            // Compute LIS on newToOld into a pooled membership mask — avoids
            // the HashSet allocation and the redundant identical-set copy that
            // the old `new HashSet<int>(ComputeLIS(...))` pair did (#31/#32).
            ComputeLISInto(newToOld, newMidLen, inLis);

            // Step 1: Remove unmatched old items (reverse order for stable indices)
            for (int i = oldMidLen - 1; i >= 0; i--)
            {
                if (!matched[i])
                {
                    int panelIdx = prefixLen + i;
                    if (panelIdx < children.Count)
                    {
                        reconciler.RemoveChildWithExitTransition(children, panelIdx);
                    }
                }
            }

            // Build the survivor → live-panel-index model by scanning the
            // realized collection AFTER removals. A live scan (rather than a
            // computed compaction) is required because
            // RemoveChildWithExitTransition can DEFER removal under an
            // exit/ambient transition, leaving the exiting control occupying a
            // slot for this pass — reading actual positions keeps the model
            // consistent with the real collection.
            for (int r = 0; r < oldMidLen; r++) oldRelToPanel[r] = -1;
            int searchEnd = children.Count - suffixLen;
            for (int i = prefixLen; i < searchEnd && i < children.Count; i++)
            {
                var child = children.Get(i);
                if (child is FrameworkElement fe && Reconciler.GetElementTag(fe) is Element tagElement)
                {
                    var key = GetKey(tagElement, i);
                    if (oldKeyMap.TryGetValue(key, out int oldRel) && matched[oldRel])
                        oldRelToPanel[oldRel] = i;
                }
            }

            // Step 2: position + patch via the canonical right-to-left LIS walk.
            // `initialAnchor` is the start of the suffix (end of the middle);
            // each item is placed immediately before its already-positioned
            // right neighbour, and every survivor is patched at its CURRENT live
            // position resolved from the model — never at a final index the panel
            // has not yet been rearranged into (bug C1) — with the model kept
            // exact across every Insert/Move (bug H1).
            int initialAnchor = children.Count - suffixLen;
            var sink = new RealKeyedMiddleSink(
                oldChildren, newChildren, oldStart, newStart,
                children, reconciler, requestRerender, ambientKind);
            RunKeyedMiddleCore(ref sink, newToOld, inLis, newMidLen, oldMidLen, initialAnchor, oldRelToPanel);
        }
        finally
        {
            // Return every pooled buffer on all exits (including exceptions).
            // The dict pool clears entries on return (ReturnKeyIndexDict), so
            // element-key references never leak across frames. The int/bool
            // buffers hold no references and have their used range fully
            // (re)initialized before any read — `matched` via the Array.Clear
            // above, `inLis` via ComputeLISInto's leading clear, `newToOld`
            // written in full for [0,newMidLen), and `oldRelToPanel` reset to -1
            // for [0,oldMidLen) — so they return dirty (clearArray:false) and
            // skip an avoidable O(rented-capacity) wipe on this hot path.
            // (Reference-typed pooled buffers elsewhere — e.g. the
            // string[]/ReactorRow[] in KeyedListDiff — DO clear, to avoid pinning
            // objects across frames.)
            ReturnKeyIndexDict(oldKeyMap);
            intPool.Return(newToOld);
            intPool.Return(oldRelToPanel);
            boolPool.Return(matched);
            boolPool.Return(inLis);
        }
    }

    /// <summary>
    /// Side-effect surface for <see cref="RunKeyedMiddleCore{TSink}"/>. The
    /// keyed-middle positioning algorithm is expressed once against this
    /// interface so the production reconciler (real WinUI Mount/Update/Move) and
    /// the headless correctness oracle in the test suite drive the identical
    /// index logic. Implemented as a <c>struct</c> behind a generic constraint
    /// so the JIT devirtualizes the calls with no boxing on the hot path (and it
    /// stays AOT/trim-clean — no reflection).
    /// </summary>
    internal interface IKeyedMiddleSink
    {
        /// <summary>Mount new child <paramref name="newIdx"/> and insert it at
        /// <paramref name="panelIdx"/>. Returns <c>false</c> when nothing was
        /// inserted (mount produced no control) so the caller skips the model
        /// shift.</summary>
        bool MountInsert(int newIdx, int panelIdx);

        /// <summary>Move the existing control at <paramref name="fromIdx"/> to
        /// <paramref name="toIdx"/> using final-position semantics (matching
        /// <see cref="IChildCollection.Move"/>).</summary>
        void MoveExisting(int fromIdx, int toIdx);

        /// <summary>Patch the surviving control for old-relative index
        /// <paramref name="oldRelIdx"/> — currently located at
        /// <paramref name="panelIdx"/> — against new child
        /// <paramref name="newIdx"/>.</summary>
        void Patch(int oldRelIdx, int newIdx, int panelIdx);
    }

    /// <summary>
    /// Canonical LIS-based keyed reconciliation of the middle section, processed
    /// right-to-left so each item is positioned immediately before its
    /// already-placed right neighbour (the live "anchor"). LIS members are left
    /// in place; only out-of-LIS movers are relocated, giving the minimal move
    /// count. Every survivor is patched at its CURRENT live position (resolved
    /// from <paramref name="oldRelToPanel"/>, which is kept exact across every
    /// structural mutation) rather than a final index the panel has not yet been
    /// rearranged into — the invariant that fixes the keyed-identity corruption
    /// (C1) and the stale-index moves (H1).
    /// </summary>
    /// <typeparam name="TSink">Concrete sink struct; a generic constraint keeps
    /// the calls devirtualized and allocation-free.</typeparam>
    /// <param name="sink">Side-effect surface that applies the mount/move/patch
    /// operations the walk decides on.</param>
    /// <param name="newToOld">For each new-middle slot, the old-relative index it
    /// reuses, or -1 for a freshly-mounted item.</param>
    /// <param name="inLis">LIS membership mask over <paramref name="newToOld"/>.</param>
    /// <param name="newMidLen">Number of items in the new middle section.</param>
    /// <param name="oldMidLen">Number of items in the old middle section.</param>
    /// <param name="initialAnchor">Live panel index of the start of the suffix
    /// (end of the middle) — where right-most items are placed.</param>
    /// <param name="oldRelToPanel">Live panel index of each surviving old item by
    /// old-relative index (-1 if absent). Mutated in place to stay exact.</param>
    internal static void RunKeyedMiddleCore<TSink>(
        ref TSink sink,
        int[] newToOld, bool[] inLis,
        int newMidLen, int oldMidLen,
        int initialAnchor, int[] oldRelToPanel)
        where TSink : struct, IKeyedMiddleSink
    {
        int anchor = initialAnchor;
        for (int i = newMidLen - 1; i >= 0; i--)
        {
            int oldRel = newToOld[i];
            if (oldRel < 0)
            {
                // New item: mount + insert immediately before the anchor.
                if (sink.MountInsert(i, anchor))
                {
                    // Insertion at `anchor` shifts every tracked survivor at an
                    // index >= anchor one slot to the right.
                    for (int r = 0; r < oldMidLen; r++)
                        if (oldRelToPanel[r] >= anchor) oldRelToPanel[r]++;
                    // The new control now occupies `anchor` and is the anchor for
                    // the next (left) item, so `anchor` itself is unchanged.
                }
                continue;
            }

            int cur = oldRelToPanel[oldRel];
            if (cur < 0) continue; // Defensive: survivor not realized (skip).

            if (!inLis[i])
            {
                // Out-of-LIS mover: relocate immediately before the anchor. With
                // Move's final-position semantics, the destination is anchor-1
                // when the control currently sits left of the anchor, else
                // anchor itself.
                int to = cur < anchor ? anchor - 1 : anchor;
                if (cur != to)
                {
                    sink.MoveExisting(cur, to);
                    // Keep the model exact: a Move shifts the half-open range
                    // between the two endpoints by one slot.
                    if (cur < to)
                    {
                        for (int r = 0; r < oldMidLen; r++)
                        {
                            int p = oldRelToPanel[r];
                            if (p > cur && p <= to) oldRelToPanel[r] = p - 1;
                        }
                    }
                    else
                    {
                        for (int r = 0; r < oldMidLen; r++)
                        {
                            int p = oldRelToPanel[r];
                            if (p >= to && p < cur) oldRelToPanel[r] = p + 1;
                        }
                    }
                    oldRelToPanel[oldRel] = to;
                    cur = to;
                }
            }

            sink.Patch(oldRel, i, cur);
            anchor = cur;
        }
    }

    /// <summary>
    /// Production <see cref="IKeyedMiddleSink"/> — drives real WinUI controls
    /// through the reconciler. A <c>readonly struct</c> so
    /// <see cref="RunKeyedMiddleCore{TSink}"/> stays allocation- and
    /// virtual-dispatch-free.
    /// </summary>
    private readonly struct RealKeyedMiddleSink : IKeyedMiddleSink
    {
        private readonly Element[] _oldChildren;
        private readonly Element[] _newChildren;
        private readonly int _oldStart;
        private readonly int _newStart;
        private readonly IChildCollection _children;
        private readonly Reconciler _reconciler;
        private readonly Action _requestRerender;
        private readonly AnimationKind? _ambientKind;

        public RealKeyedMiddleSink(
            Element[] oldChildren, Element[] newChildren, int oldStart, int newStart,
            IChildCollection children, Reconciler reconciler, Action requestRerender,
            AnimationKind? ambientKind)
        {
            _oldChildren = oldChildren;
            _newChildren = newChildren;
            _oldStart = oldStart;
            _newStart = newStart;
            _children = children;
            _reconciler = reconciler;
            _requestRerender = requestRerender;
            _ambientKind = ambientKind;
        }

        public bool MountInsert(int newIdx, int panelIdx)
        {
            var ctrl = _reconciler.Mount(_newChildren[_newStart + newIdx], _requestRerender);
            if (ctrl is null) return false;
            _children.Insert(panelIdx, ctrl);
            ApplyAmbientEnterIfActive(ctrl, _newChildren[_newStart + newIdx], _ambientKind);
            return true;
        }

        public void MoveExisting(int fromIdx, int toIdx)
        {
            var moved = _children.Get(fromIdx);
            _children.Move(fromIdx, toIdx);
            // Spec 042 §6 — implicit Offset animation on the moved child so the
            // reorder reads visually under an ambient transaction.
            if (_ambientKind is { } k)
                ApplyAmbientMove(moved, k);
        }

        public void Patch(int oldRelIdx, int newIdx, int panelIdx)
        {
            if (panelIdx >= _children.Count) return;
            var replacement = _reconciler.UpdateChild(
                _oldChildren[_oldStart + oldRelIdx],
                _newChildren[_newStart + newIdx],
                _children.Get(panelIdx),
                _requestRerender);
            if (replacement is not null)
            {
                _reconciler.UnmountChild(_children.Get(panelIdx));
                _children.Replace(panelIdx, replacement);
            }
        }
    }

    /// <summary>
    /// Compute the Longest Increasing Subsequence over the first
    /// <paramref name="length"/> entries of <paramref name="arr"/>, writing the
    /// membership mask into <paramref name="inLis"/> (<c>inLis[i] == true</c> iff
    /// index <c>i</c> participates in the LIS). Entries with value -1 are skipped
    /// (unmapped items). O(n log n) patience sorting. All working buffers are
    /// rented from <see cref="ArrayPool{T}"/>, so the hot reconcile path pays no
    /// per-call heap allocation (#32).
    /// </summary>
    internal static void ComputeLISInto(int[] arr, int length, bool[] inLis)
    {
        if (length > 0) Array.Clear(inLis, 0, length);
        if (length == 0) return;

        var pool = ArrayPool<int>.Shared;
        int[] tails = pool.Rent(length);        // Smallest tail values
        int[] tailIndices = pool.Rent(length);  // arr indices matching tails
        int[] predecessors = pool.Rent(length); // back-pointers for reconstruction
        try
        {
            int tailsCount = 0;
            for (int i = 0; i < length; i++)
            {
                if (arr[i] == -1) continue; // Skip unmapped

                int val = arr[i];

                // Binary search for insertion position in tails[0..tailsCount).
                int lo = 0, hi = tailsCount;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (tails[mid] < val) lo = mid + 1;
                    else hi = mid;
                }

                // Predecessor is the previous tail's arr-index (read before the
                // tails[lo] write — index lo-1 is unaffected by it).
                predecessors[i] = lo > 0 ? tailIndices[lo - 1] : -1;

                if (lo == tailsCount)
                {
                    tails[tailsCount] = val;
                    tailIndices[tailsCount] = i;
                    tailsCount++;
                }
                else
                {
                    tails[lo] = val;
                    tailIndices[lo] = i;
                }
            }

            // Backtrack from the last tail to mark LIS membership. Only
            // non-skipped indices ever enter the chain, so unwritten
            // predecessor slots for skipped entries are never read.
            if (tailsCount == 0) return;
            int idx = tailIndices[tailsCount - 1];
            while (idx != -1)
            {
                inLis[idx] = true;
                idx = predecessors[idx];
            }
        }
        finally
        {
            pool.Return(tails);
            pool.Return(tailIndices);
            pool.Return(predecessors);
        }
    }

    /// <summary>
    /// Compute Longest Increasing Subsequence, returning the set of
    /// participating indices. Convenience wrapper over
    /// <see cref="ComputeLISInto"/> for tests and set-shaped callers; the hot
    /// reconcile path uses the allocation-free <see cref="ComputeLISInto"/>.
    /// </summary>
    internal static HashSet<int> ComputeLIS(int[] arr)
    {
        var result = new HashSet<int>();
        if (arr.Length == 0) return result;

        var pool = ArrayPool<bool>.Shared;
        bool[] mask = pool.Rent(arr.Length);
        try
        {
            ComputeLISInto(arr, arr.Length, mask);
            for (int i = 0; i < arr.Length; i++)
                if (mask[i]) result.Add(i);
        }
        finally
        {
            pool.Return(mask, clearArray: true);
        }
        return result;
    }

    private static Element[] Filter(Element[] elements)
    {
        // Single count pass; the result array is then sized exactly and filled
        // in one more pass — no intermediate List + ToArray double-allocation
        // (#36). The common no-null case still returns the input untouched.
        int keep = 0;
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i] is not null and not EmptyElement) keep++;
        }
        if (keep == elements.Length) return elements;

        var result = new Element[keep];
        int j = 0;
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i] is not null and not EmptyElement)
                result[j++] = elements[i];
        }
        return result;
    }

    private static bool HasAnyKeys(Element[] elements)
    {
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i].Key is not null) return true;
        }
        return false;
    }

    private static bool KeyMatch(Element a, Element b)
    {
        // Both must have the same key (or both null) AND same type
        if (a.GetType() != b.GetType()) return false;
        return a.Key == b.Key;
    }

    // Cache the per-Type display name used by the unkeyed GetKey fallback so we
    // don't reflect over the runtime type on every diff (#38). Explicit keys
    // are strongly preferred and bypass this path entirely.
    private static readonly ConcurrentDictionary<Type, string> _typeNameCache = new();

    private static string GetKey(Element element, int positionalIndex)
    {
        // Use explicit key if available, otherwise fall back to type+position
        if (element.Key is not null) return element.Key;
        var typeName = _typeNameCache.GetOrAdd(element.GetType(), static t => t.Name);
        return $"__pos_{positionalIndex}_{typeName}";
    }

    // ── Re-entrancy-safe Dictionary&lt;string,int&gt; pool ────────────────────
    // ReconcileKeyedMiddle needs a transient key→index map per call, and it
    // recurses through UpdateChild. A per-thread stack hands each (nested) call
    // its own instance: rented dicts are not in the pool, so an inner call can
    // never grab a buffer the outer call is still using. Pool size is capped so
    // deep trees don't retain an unbounded number of dictionaries.
    [ThreadStatic] private static Stack<Dictionary<string, int>>? _keyIndexDictPool;
    private const int KeyIndexDictPoolCap = 16;

    private static Dictionary<string, int> RentKeyIndexDict(int capacity)
    {
        var pool = _keyIndexDictPool;
        if (pool is { Count: > 0 })
        {
            var d = pool.Pop();
            if (capacity > 0) d.EnsureCapacity(capacity);
            return d;
        }
        return new Dictionary<string, int>(capacity > 0 ? capacity : 0, StringComparer.Ordinal);
    }

    private static void ReturnKeyIndexDict(Dictionary<string, int> dict)
    {
        dict.Clear();
        var pool = _keyIndexDictPool ??= new Stack<Dictionary<string, int>>();
        if (pool.Count < KeyIndexDictPoolCap) pool.Push(dict);
    }

    /// <summary>
    /// Spec 042 §6 — when an <see cref="Animations.Animate"/> transaction
    /// is active and the newly-mounted child has no explicit
    /// <c>.Transition(...)</c> modifier, apply the default fade-up enter
    /// so the structural change reads visually. Per-element transitions
    /// continue to win when set; this is purely a default for the
    /// transactional case.
    /// </summary>
    private static void ApplyAmbientEnterIfActive(UIElement ctrl, Element element, AnimationKind? ambientKind)
    {
        if (ambientKind is not { } kind) return;
        if (element.ElementTransition is not null) return;
        Reconciler.ApplyAmbientEnterAnimation(ctrl, kind);
    }

    /// <summary>
    /// Spec 042 §6 — implicit Offset animation on a moved child so the
    /// reorder reads visually. Mirrors the per-container offset animation
    /// in <c>Reconciler.Update.cs:StartMoveOffsetAnimation</c> for the
    /// templated-list path; same curve resolution by kind.
    /// </summary>
    private static void ApplyAmbientMove(UIElement ctrl, AnimationKind kind)
    {
        var curve = AnimationKindMap.ToCurve(kind);
        if (curve is null) return;
        try
        {
            var visual = global::Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ctrl);
            var compositor = visual.Compositor;
            var anim = Animation.AnimationHelper.CreateVector3ImplicitAnimation(compositor, "Offset", curve);
            var coll = compositor.CreateImplicitAnimationCollection();
            coll["Offset"] = anim;
            visual.ImplicitAnimations = coll;
        }
        catch
        {
            // Composition can throw in headless / disposing contexts.
            // Animation is non-critical — correctness is preserved.
        }
    }
}
