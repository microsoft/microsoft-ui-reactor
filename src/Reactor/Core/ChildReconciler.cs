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
        Action requestRerender)
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
            ReconcilePositional(oldFiltered, newFiltered, children, reconciler, requestRerender, ambientKind);
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
        AnimationKind? ambientKind)
    {
        int childCount = children.Count; // cache to avoid repeated COM calls
        int common = Math.Min(oldChildren.Length, newChildren.Length);

        // Update common children in place
        for (int i = 0; i < common; i++)
        {
            if (i >= childCount) break;

            // Early skip: if the element is structurally identical (and has no
            // theme bindings that need re-evaluation), we can avoid the expensive
            // children.Get(i) COM call entirely. This saves ~2 COM roundtrips per
            // unchanged child (IVector.GetAt + all the property diffing inside Update).
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
                if (newEl.HasCallbacks && children.Get(i) is FrameworkElement fe)
                    Reconciler.SetElementTag(fe, newEl);
                continue;
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
            if (Element.CanSkipUpdate(oldEl, newEl)
                && !reconciler.ForceRenderThroughWrapper(newEl))
            {
                reconciler.DebugElementsSkipped++;
                // Refresh the Tag when the element carries callbacks so the
                // event trampoline dispatches through the latest closure.
                if (newEl.HasCallbacks && prefixLen < childCount && children.Get(prefixLen) is FrameworkElement fe)
                    Reconciler.SetElementTag(fe, newEl);
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
                if (newEl.HasCallbacks && panelIdx >= 0 && panelIdx < childCount && children.Get(panelIdx) is FrameworkElement fe)
                    Reconciler.SetElementTag(fe, newEl);
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
        var keyToIndex = RentKeyIndexDict(newMidLen);
        int[] newToOld = intPool.Rent(newMidLen);
        bool[] matched = boolPool.Rent(oldMidLen);
        bool[] inLis = boolPool.Rent(newMidLen);
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

            // Build key→panel-index lookup for O(1) FindItemByOldIndex (CR-011).
            int searchEnd = children.Count - suffixLen;
            for (int i = prefixLen; i < searchEnd && i < children.Count; i++)
            {
                var child = children.Get(i);
                if (child is FrameworkElement fe && Reconciler.GetElementTag(fe) is Element tagElement)
                {
                    var key = GetKey(tagElement, i);
                    keyToIndex.TryAdd(key, i);
                }
            }

            // Step 2: Process new items - insert new, move existing not in LIS
            for (int i = 0; i < newMidLen; i++)
            {
                int targetPanelIdx = prefixLen + i;

                if (newToOld[i] == -1)
                {
                    var ctrl = reconciler.Mount(newChildren[newStart + i], requestRerender);
                    if (ctrl is not null)
                    {
                        children.Insert(targetPanelIdx, ctrl);
                        ApplyAmbientEnterIfActive(ctrl, newChildren[newStart + i], ambientKind);
                    }
                }
                else if (inLis[i])
                {
                    if (targetPanelIdx < children.Count)
                    {
                        var replacement = reconciler.UpdateChild(
                            oldChildren[oldStart + newToOld[i]],
                            newChildren[newStart + i],
                            children.Get(targetPanelIdx),
                            requestRerender);
                        if (replacement is not null)
                        {
                            reconciler.UnmountChild(children.Get(targetPanelIdx));
                            children.Replace(targetPanelIdx, replacement);
                        }
                    }
                }
                else
                {
                    int oldRelIdx = newToOld[i];
                    int oldAbsIdx = oldStart + oldRelIdx;
                    var lookupKey = oldAbsIdx < oldChildren.Length ? GetKey(oldChildren[oldAbsIdx], oldAbsIdx) : null;
                    int currentPos = lookupKey != null && keyToIndex.TryGetValue(lookupKey, out var pos) ? pos : -1;
                    if (currentPos >= 0 && currentPos != targetPanelIdx)
                    {
                        var movedChild = children.Get(currentPos);
                        children.Move(currentPos, targetPanelIdx);
                        // Update lookup: moved element is now at targetPanelIdx.
                        if (lookupKey != null) keyToIndex[lookupKey] = targetPanelIdx;
                        // Spec 042 §6 — implicit Offset animation on the moved
                        // child so the reorder reads visually under an ambient.
                        // Attach via the existing Composition helper rather
                        // than the per-element LayoutAnimation modifier (which
                        // remains the user's per-element opt-in).
                        if (ambientKind is { } k && movedChild is UIElement movedUi)
                            ApplyAmbientMove(movedUi, k);
                    }
                    if (targetPanelIdx < children.Count)
                    {
                        var replacement = reconciler.UpdateChild(
                            oldChildren[oldStart + oldRelIdx],
                            newChildren[newStart + i],
                            children.Get(targetPanelIdx),
                            requestRerender);
                        if (replacement is not null)
                        {
                            reconciler.UnmountChild(children.Get(targetPanelIdx));
                            children.Replace(targetPanelIdx, replacement);
                        }
                    }
                }
            }
        }
        finally
        {
            // Return every pooled buffer on all exits (including exceptions).
            // The dict pool clears entries on return (ReturnKeyIndexDict), so
            // element-key references never leak across frames. The bool buffers
            // hold no references and have their used range fully (re)initialized
            // before any read — `matched` via the Array.Clear-on-rent above and
            // `inLis` via ComputeLISInto's own leading clear — so they return
            // dirty (clearArray:false) and skip an avoidable O(rented-capacity)
            // wipe on this hot path. `newToOld` (int) is likewise overwritten in
            // full for [0,newMidLen) before each read, so it too returns without
            // clearing. (Reference-typed pooled buffers elsewhere — e.g. the
            // string[]/ReactorRow[] in KeyedListDiff — DO clear, to avoid pinning
            // objects across frames.)
            ReturnKeyIndexDict(oldKeyMap);
            ReturnKeyIndexDict(keyToIndex);
            intPool.Return(newToOld);
            boolPool.Return(matched);
            boolPool.Return(inLis);
        }
    }

    /// <summary>
    /// Find the current position of an item that was originally at a given old index.
    /// Uses the reconciler's element map to match elements.
    /// </summary>
    private static int FindItemByOldIndex(
        IChildCollection children,
        Element[] oldElements,
        int oldIndex,
        int searchStart,
        int searchEnd,
        Reconciler reconciler)
    {
        for (int i = searchStart; i < searchEnd && i < children.Count; i++)
        {
            var child = children.Get(i);
            if (child is FrameworkElement fe && Reconciler.GetElementTag(fe) is Element tagElement)
            {
                if (oldIndex < oldElements.Length &&
                    GetKey(tagElement, -1) == GetKey(oldElements[oldIndex], oldIndex))
                    return i;
            }
        }
        return -1;
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
    // ReconcileKeyedMiddle needs two transient key→index maps per call, and it
    // recurses through UpdateChild. A per-thread stack hands each (nested) call
    // its own instances: rented dicts are not in the pool, so an inner call can
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
