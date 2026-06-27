using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Pins the modifier-resolution optimization in <see cref="Reconciler.Update"/>
/// (the redundant self-merge fix). When an element carries modifiers directly
/// (no <c>ModifiedElement</c> wrapper) — the common case for every cell in a
/// large grid — the resolved <c>oldModifiers</c>/<c>modifiers</c> are already the
/// element's own <see cref="ElementModifiers"/> reference, so the old code ran
/// <c>x.Merge(x)</c>, allocating six value-identical records per changed cell for
/// nothing. <see cref="Reconciler.Update"/> now guards that with
/// <c>!ReferenceEquals(...)</c>.
///
/// These tests are headless (no XAML Application). They avoid brush-typed
/// modifiers (which require a UI thread) and use value-type fields only.
/// </summary>
public class ReconcilerModifierMergeTests
{
    private static readonly Action NoOp = () => { };

    // A leaf element that carries modifiers directly — mirrors the shape of a
    // grid cell (TextBlockElement with Layout/Visual modifiers, no wrapper).
    private sealed record DirectModifierLeaf(int Id) : Element;

    private static ElementModifiers CellModifiers() => new()
    {
        Layout = new LayoutModifiers { Padding = new Thickness(2, 1, 2, 1), Width = 8 },
        Visual = new VisualModifiers { Opacity = 0.5 },
    };

    // ── Invariant the guard relies on: Merge(x, x) is value-equal to x ──────

    [Fact]
    public void Merge_With_Self_Is_Value_Equal_To_Original()
    {
        var m = CellModifiers();

        var merged = m.Merge(m);

        // Value-equal (record structural equality, including the Layout/Visual
        // sub-records) — this is what makes skipping the self-merge safe.
        Assert.Equal(m, merged);
        Assert.True(Element.ModifiersEqual(m, merged));

        // …but a *distinct* instance: Merge always allocates a fresh record
        // (plus fresh Layout/Visual sub-records). That allocation is exactly
        // what the reconciler now avoids for the no-wrapper case.
        Assert.NotSame(m, merged);
        Assert.NotSame(m.Layout, merged.Layout);
        Assert.NotSame(m.Visual, merged.Visual);
    }

    // ── The merge the guard must still perform stays correct (inner wins) ───

    [Fact]
    public void Merge_Prefers_Other_Fields_And_Fills_Gaps_From_Base()
    {
        // Simulates accumulated wrapper modifiers (base) merged with an inner
        // element's own modifiers (other). `other` wins where set; `base` fills
        // the gaps. If anyone "optimizes" Merge into a no-op, this fails — the
        // ReferenceEquals guard only skips the *self* case, never a real merge.
        var baseMods = new ElementModifiers
        {
            Layout = new LayoutModifiers { Padding = new Thickness(2), Width = 8 },
            Visual = new VisualModifiers { Opacity = 0.25 },
        };
        var other = new ElementModifiers
        {
            Layout = new LayoutModifiers { Width = 99 },          // overrides Width
            Visual = new VisualModifiers { Rotation = 45f },      // adds Rotation
        };

        var merged = baseMods.Merge(other);

        Assert.Equal(99, merged.Width);                            // other wins
        Assert.Equal(new Thickness(2), merged.Padding);           // base fills gap
        Assert.Equal(0.25, merged.Opacity);                       // base fills gap
        Assert.Equal(45f, merged.Visual?.Rotation);               // other adds
    }

    // ── Revert→fail teeth: the no-wrapper resolution must not self-merge ────

    [Fact]
    public void Update_DirectModifiers_AvoidsRedundantSelfMergeAllocation()
    {
        var reconciler = new Reconciler();
        // Same instance for old and new: structurally identical → Update takes
        // the shallow-equality skip path and returns without ever dereferencing
        // the (null) control. The modifier resolution at the top of Update still
        // runs, so this isolates exactly the self-merge allocation. Before the
        // fix this allocated six records (ElementModifiers + Layout + Visual, per
        // side); after the fix it allocates none.
        var leaf = new DirectModifierLeaf(1) { Modifiers = CellModifiers() };

        // Warm up the JIT + the resolution path.
        for (int i = 0; i < 2_000; i++)
            _ = reconciler.Update(leaf, leaf, control: null!, NoOp);

        const int iterations = 50_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            _ = reconciler.Update(leaf, leaf, control: null!, NoOp);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        // New code allocates ~0 B/call on this path. The reverted self-merge
        // allocates well over 1 KB/call (six records). A 64 B/iteration cap
        // cleanly separates the two while tolerating incidental bookkeeping.
        Assert.True(delta <= 64L * iterations,
            $"Update self-merged direct modifiers: {delta}B over {iterations} calls " +
            $"({delta / (double)iterations:F1} B/call, cap 64). The redundant " +
            $"x.Merge(x) appears to be back.");
    }

    // ── Lifecycle callbacks survive a merge (regression guard) ─────────────
    // ModifiersEqual deliberately ignores OnMount/OnUpdate (side-effect hooks,
    // not render state), but Merge must PRESERVE them. A self-merge that
    // silently dropped OnUpdateAction is exactly the class of bug that bit a
    // sibling change — pin it so the self-merge skip stays provably safe for
    // the callback-bearing fields, not just the Layout/Visual buckets.
    [Fact]
    public void Merge_Preserves_Lifecycle_Callbacks()
    {
        Action<FrameworkElement> mount = _ => { };
        Action<FrameworkElement> unmount = _ => { };
        Action<FrameworkElement> update = _ => { };

        var baseMods = new ElementModifiers
        {
            OnMountAction = mount,
            OnUnmountAction = unmount,
            OnUpdateAction = update,
        };

        // Self-merge (the no-wrapper case the reconciler now skips) is value-
        // identical: every callback is still the very same delegate afterwards.
        // This is what makes skipping the self-merge safe for callback fields.
        var self = baseMods.Merge(baseMods);
        Assert.Same(mount, self.OnMountAction);
        Assert.Same(unmount, self.OnUnmountAction);
        Assert.Same(update, self.OnUpdateAction);

        // A real merge with an `other` that leaves callbacks unset keeps the
        // base callbacks (base fills the gap)…
        var other = new ElementModifiers { Visual = new VisualModifiers { Opacity = 0.3 } };
        var filled = baseMods.Merge(other);
        Assert.Same(mount, filled.OnMountAction);
        Assert.Same(unmount, filled.OnUnmountAction);
        Assert.Same(update, filled.OnUpdateAction);

        // …and `other` wins where it sets its own callback.
        Action<FrameworkElement> update2 = _ => { };
        var overriding = baseMods.Merge(new ElementModifiers { OnUpdateAction = update2 });
        Assert.Same(update2, overriding.OnUpdateAction);
        Assert.Same(mount, overriding.OnMountAction); // base still fills the gap
    }

    // ── The guard skips ONLY the self-merge: a real wrapper layer still merges ──
    // Update_DirectModifiers_AvoidsRedundantSelfMergeAllocation pins that the
    // no-wrapper case is skipped. This pins the *other* half through
    // Reconciler.Update: when a ModifiedElement wrapper contributes a DISTINCT
    // modifier instance, the !ReferenceEquals guard's fall-through MUST still run
    // the real merge (wrapper ⊕ inner). The merged value-correctness is covered
    // by Merge_Prefers_Other_Fields_And_Fills_Gaps_From_Base; here we prove the
    // reconciler does not wrongly skip that merge. (End-to-end application of the
    // resolved modifiers to a live control is covered by the selftest fixtures —
    // ModifierEventFixtures / ControlUpdateFixtures — which need a UI thread.)
    [Fact]
    public void Update_WrapperLayer_StillMergesInnerModifiers()
    {
        var reconciler = new Reconciler();

        // No-wrapper leaf: the resolved modifiers ARE the element's own instance,
        // so the !ReferenceEquals guard skips the self-merge → ~0 B/call.
        var leaf = new DirectModifierLeaf(1) { Modifiers = CellModifiers() };

        // Wrapped leaf: the outer ModifiedElement contributes a distinct modifier
        // instance (WrappedModifiers), so the accumulator is no longer reference-
        // equal to the inner element's own Modifiers — the guard falls through and
        // performs the real merge, which allocates. Same instance for old and new
        // so Update still takes the shallow-equality skip and never dereferences
        // the (null) control.
        var inner = new DirectModifierLeaf(2) { Modifiers = CellModifiers() };
        var wrapped = new ModifiedElement(
            inner,
            new ElementModifiers { Visual = new VisualModifiers { Rotation = 30f } });

        // Warm up the JIT + both resolution paths.
        for (int i = 0; i < 2_000; i++)
        {
            _ = reconciler.Update(leaf, leaf, control: null!, NoOp);
            _ = reconciler.Update(wrapped, wrapped, control: null!, NoOp);
        }

        const int iterations = 50_000;

        long b0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            _ = reconciler.Update(leaf, leaf, control: null!, NoOp);
        long noWrapper = GC.GetAllocatedBytesForCurrentThread() - b0;

        long b1 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            _ = reconciler.Update(wrapped, wrapped, control: null!, NoOp);
        long wrapper = GC.GetAllocatedBytesForCurrentThread() - b1;

        // The wrapper layer's merge allocates a fresh ElementModifiers (+ a Visual
        // bucket) per side, per call — comfortably more than 200 B/call above the
        // guarded no-wrapper path. If anyone broadens the guard so it also skips
        // the wrapper merge, `wrapper` collapses toward `noWrapper` and the wrapper
        // layer's modifiers would silently stop applying. This catches that.
        Assert.True(wrapper > noWrapper + 200L * iterations,
            $"Wrapper-layer merge appears to have been skipped: wrapper={wrapper} B, " +
            $"noWrapper={noWrapper} B over {iterations} calls " +
            $"(delta {(wrapper - noWrapper) / (double)iterations:F1} B/call, expected > 200).");
    }
}
