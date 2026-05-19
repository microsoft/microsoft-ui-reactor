using Microsoft.UI.Reactor.Animation;

namespace Microsoft.UI.Reactor.Core.Internal;

/// <summary>
/// Translates a public <see cref="AnimationKind"/> token to a concrete
/// <see cref="Animation.Curve"/> the compositor can consume. The mapping is
/// intentionally narrow so the public surface stays declarative — adding a new
/// kind is a one-line edit here, not a churn of per-call-site curve plumbing.
/// (spec 042 §6.)
/// </summary>
/// <remarks>
/// Durations and easings match Reactor's existing motion guidelines (see
/// <see cref="Easing"/>): 250 ms for ease-in/out, 200 ms for ease-out reposition
/// (matches WinUI <c>RepositionThemeTransition</c>), and a critically-damped
/// spring with a short period for the expressive Spring kind. The values can
/// evolve without affecting callers as long as the perceptual hierarchy
/// (Spring &gt; EaseInOut &gt; EaseOut &gt; EaseIn) is preserved.
/// </remarks>
internal static class AnimationKindMap
{
    /// <summary>
    /// Resolve a curve for the given kind, or <see langword="null"/> when the
    /// kind is <see cref="AnimationKind.None"/>. Caller treats null as "no
    /// transition this render," which is the explicit-suppression contract for
    /// nested <see cref="Animations.Animate"/> calls.
    /// </summary>
    public static Curve? ToCurve(AnimationKind kind) => kind switch
    {
        AnimationKind.None       => null,
        AnimationKind.Spring     => Curve.Spring(dampingRatio: 0.8f, period: 0.05f),
        AnimationKind.EaseIn     => Curve.Ease(250, Easing.EaseIn),
        AnimationKind.EaseOut    => Curve.Ease(200, Easing.EaseOut),
        AnimationKind.EaseInOut  => Curve.Ease(250, Easing.EaseInOut),
        AnimationKind.Default    => Curve.Ease(200, Easing.EaseOut),
        _                        => Curve.Ease(200, Easing.EaseOut),
    };

    /// <summary>
    /// True when the kind produces a visible animation (i.e., a non-null
    /// curve). Callers use this to skip per-container Composition setup
    /// entirely for the <see cref="AnimationKind.None"/> case.
    /// </summary>
    public static bool IsActive(AnimationKind kind) => kind != AnimationKind.None;
}
