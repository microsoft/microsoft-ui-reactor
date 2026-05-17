using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Internal;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Animation;

/// <summary>
/// Spec 042 §6, scope discipline (Phase 3.5): the
/// <c>Animations.Animate(...)</c> ambient applies only to structural
/// changes routed through <see cref="KeyedListDiff"/> and
/// <see cref="ChildReconciler"/>. It must <b>not</b> animate arbitrary
/// property changes (color, size, brush) on surviving leaf elements —
/// that remains the job of per-element modifiers such as
/// <c>WithImplicitTransition</c> / <c>.LayoutAnimation()</c>.
/// <para>
/// The architectural guard is structural: <see cref="AnimationAmbient"/>
/// and <see cref="AnimationScope"/> are two independent channels — the
/// transactional ambient flows on <c>AsyncLocal</c>, the per-element
/// curve scope flows on <c>ThreadStatic</c>. Reactor's property-setter
/// hot path (<c>AnimationHelper.SetOrAnimate</c>) only consults
/// <see cref="AnimationScope.Current"/>. The tests below pin this
/// separation so a future contributor can't accidentally collapse the
/// two ambients and silently change the SwiftUI-style scoping
/// contract.
/// </para>
/// </summary>
public class AnimateScopeDisciplineTests
{
    [Fact]
    public void Animate_Does_Not_Push_AnimationScope()
    {
        // The contract: setting AnimationKind.Spring on the transaction
        // does NOT set the ThreadStatic curve scope. If it did,
        // AnimationHelper.SetOrAnimate would route every property change
        // inside the block through the compositor — animating colors,
        // sizes, etc. on surviving leaves, which is explicitly out of
        // scope (and would surprise users coming from SwiftUI).
        bool scopeWasSetInside = false;

        Animations.Animate(AnimationKind.Spring, () =>
        {
            scopeWasSetInside = AnimationScope.HasScope;
        });

        Assert.False(scopeWasSetInside,
            "Animations.Animate must not push onto AnimationScope — " +
            "doing so would animate non-structural property changes on leaves.");
        Assert.False(AnimationScope.HasScope);
    }

    [Fact]
    public void Animate_And_WithAnimation_Are_Independent_Channels()
    {
        // Sanity: nesting WithAnimation inside Animate observes BOTH
        // ambients independently (the AsyncLocal carries the kind, the
        // ThreadStatic carries the curve). Neither one infects the other's
        // scope — which is what lets the user opt into per-property curves
        // inside a transactional block without surprising fan-out.
        AnimationKind? observedKind = null;
        Curve? observedCurve = null;

        Animations.Animate(AnimationKind.EaseOut, () =>
        {
            AnimationScope.WithAnimation(Curve.Spring(), () =>
            {
                observedKind = AnimationAmbient.Current?.Kind;
                observedCurve = AnimationScope.Current;
            });
        });

        Assert.Equal(AnimationKind.EaseOut, observedKind);
        Assert.IsType<SpringCurve>(observedCurve);

        // Both channels back to default after both scopes unwind.
        Assert.Null(AnimationAmbient.Current);
        Assert.False(AnimationScope.HasScope);
    }

    [Fact]
    public void Animate_Does_Not_Affect_AnimationScope_HasScope_Even_For_None()
    {
        // The None-kind explicit-suppression path is also a structural-only
        // concern; it must not flip the AnimationScope state.
        Animations.Animate(AnimationKind.None, () =>
        {
            Assert.False(AnimationScope.HasScope);
        });

        Assert.False(AnimationScope.HasScope);
    }
}
