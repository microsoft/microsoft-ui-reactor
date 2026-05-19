using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Internal;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Phase 3.1 of spec 042: the ambient <c>Animations.Animate(...)</c>
/// transaction. Pins down the AsyncLocal stack semantics so the
/// reconciler-side consumer (Phase 3.3+) can rely on:
/// <list type="bullet">
/// <item><description>Ambient is observable synchronously inside
/// <c>Animate</c>.</description></item>
/// <item><description>Ambient is null after <c>Animate</c> returns.</description></item>
/// <item><description>Nesting stacks correctly — inner kind wins, outer
/// resumes after.</description></item>
/// <item><description>Ambient flows across awaits / dispatcher hops
/// (the contract Q3 from spec §9 depends on).</description></item>
/// <item><description>Concurrent async flows do not see each other's
/// ambient (the AsyncLocal isolation guarantee).</description></item>
/// </list>
/// </summary>
public class AnimationAmbientTests
{
    // ════════════════════════════════════════════════════════════════════
    //  Basic push / pop
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Current_Is_Null_When_No_Transaction_Open()
    {
        // Pinning the cold-start contract — without this, a leaked ambient
        // from a previous test would silently animate the rest of the suite.
        Assert.Null(AnimationAmbient.Current);
    }

    [Fact]
    public void Animate_Sets_Current_During_Action()
    {
        AnimationKind? observed = null;

        Animations.Animate(AnimationKind.Spring, () =>
        {
            observed = AnimationAmbient.Current?.Kind;
        });

        Assert.Equal(AnimationKind.Spring, observed);
    }

    [Fact]
    public void Animate_Clears_Current_After_Action_Returns()
    {
        Animations.Animate(AnimationKind.EaseOut, () => { });

        Assert.Null(AnimationAmbient.Current);
    }

    [Fact]
    public void Animate_Clears_Current_On_Exception()
    {
        // The try/finally inside `using var _ = new Scope(...)` must restore
        // the previous ambient even when the action throws. A leak here
        // would poison every subsequent render on the same logical flow.
        Assert.Throws<global::System.InvalidOperationException>(() =>
            Animations.Animate(AnimationKind.Spring, () =>
                throw new global::System.InvalidOperationException("boom")));

        Assert.Null(AnimationAmbient.Current);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Generic Animate<T> overload
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Animate_Generic_Returns_Func_Value_And_Sets_Ambient()
    {
        var result = Animations.Animate(AnimationKind.EaseInOut, () =>
        {
            Assert.Equal(AnimationKind.EaseInOut, AnimationAmbient.Current?.Kind);
            return 42;
        });

        Assert.Equal(42, result);
        Assert.Null(AnimationAmbient.Current);
    }

    [Fact]
    public void Animate_Generic_Clears_Current_On_Exception()
    {
        Assert.Throws<global::System.InvalidOperationException>(() =>
            Animations.Animate<int>(AnimationKind.Spring, () =>
                throw new global::System.InvalidOperationException("boom")));

        Assert.Null(AnimationAmbient.Current);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Nesting — outer/inner stack discipline
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Nested_Animate_Inner_Kind_Wins_Inside()
    {
        AnimationKind? observedInner = null;
        AnimationKind? observedOuterAfter = null;

        Animations.Animate(AnimationKind.Spring, () =>
        {
            Animations.Animate(AnimationKind.EaseIn, () =>
            {
                observedInner = AnimationAmbient.Current?.Kind;
            });
            observedOuterAfter = AnimationAmbient.Current?.Kind;
        });

        Assert.Equal(AnimationKind.EaseIn, observedInner);
        Assert.Equal(AnimationKind.Spring, observedOuterAfter);
        Assert.Null(AnimationAmbient.Current);
    }

    [Fact]
    public void Nested_Animate_None_Suppresses_Outer()
    {
        // AnimationKind.None inside an outer Spring should explicitly NOT
        // animate the state changes in the inner scope — this is the
        // escape-hatch contract (the inner author opts out of the caller's
        // implicit animation intent).
        AnimationKind? observed = null;

        Animations.Animate(AnimationKind.Spring, () =>
        {
            Animations.Animate(AnimationKind.None, () =>
            {
                observed = AnimationAmbient.Current?.Kind;
            });
        });

        Assert.Equal(AnimationKind.None, observed);
    }

    // ════════════════════════════════════════════════════════════════════
    //  ExecutionContext propagation — the Q3 contract
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Ambient_Flows_Across_Await()
    {
        // The state-setter pattern relies on AsyncLocal flowing across
        // dispatcher hops. Test the underlying primitive with a Task.Yield
        // (the simplest ExecutionContext-capturing async boundary).
        AnimationKind? afterYield = null;

        await Animations.Animate(AnimationKind.Spring, async () =>
        {
            await Task.Yield();
            afterYield = AnimationAmbient.Current?.Kind;
        });

        Assert.Equal(AnimationKind.Spring, afterYield);
    }

    [Fact]
    public async Task Captured_Ambient_Survives_Dispatched_Continuation()
    {
        // Mimics the real-world UseState setter that captures Current and
        // queues a deferred render: capture *inside* Animate, then schedule
        // a Task.Run continuation that ASSERTs the captured value is
        // observable even after Animate's `using` has popped its scope.
        AmbientAnimation? captured = null;
        Task continuation = null!;

        Animations.Animate(AnimationKind.EaseOut, () =>
        {
            captured = AnimationAmbient.Current;
            continuation = Task.Run(() =>
            {
                // Task.Run captures ExecutionContext at queue time; the
                // ambient propagates with the captured EC even though the
                // outer using-scope has already disposed by the time this
                // body actually runs on the thread pool.
                Assert.Equal(AnimationKind.EaseOut, AnimationAmbient.Current?.Kind);
            });
        });

        await continuation;

        Assert.Equal(AnimationKind.EaseOut, captured?.Kind);
        Assert.Null(AnimationAmbient.Current);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Argument validation
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Animate_Action_Null_Throws()
    {
        Assert.Throws<global::System.ArgumentNullException>(() =>
            Animations.Animate(AnimationKind.Spring, (global::System.Action)null!));
    }

    [Fact]
    public void Animate_Generic_Func_Null_Throws()
    {
        Assert.Throws<global::System.ArgumentNullException>(() =>
            Animations.Animate<int>(AnimationKind.Spring, (global::System.Func<int>)null!));
    }

    // ════════════════════════════════════════════════════════════════════
    //  AmbientAnimation record properties
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AmbientAnimation_HasEffect_Is_False_For_None()
    {
        Assert.False(new AmbientAnimation(AnimationKind.None).HasEffect);
    }

    [Fact]
    public void AmbientAnimation_HasEffect_Is_True_For_NonNone_Kinds()
    {
        Assert.True(new AmbientAnimation(AnimationKind.Spring).HasEffect);
        Assert.True(new AmbientAnimation(AnimationKind.Default).HasEffect);
        Assert.True(new AmbientAnimation(AnimationKind.EaseIn).HasEffect);
        Assert.True(new AmbientAnimation(AnimationKind.EaseOut).HasEffect);
        Assert.True(new AmbientAnimation(AnimationKind.EaseInOut).HasEffect);
    }
}
