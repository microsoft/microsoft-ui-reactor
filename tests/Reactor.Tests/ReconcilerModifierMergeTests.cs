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
}
