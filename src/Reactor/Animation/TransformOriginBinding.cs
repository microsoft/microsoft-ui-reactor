using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Animation;

/// <summary>
/// Spec 066 §8.2: <c>.TransformOrigin(x, y)</c> as an expression on the handoff visual,
/// <c>CenterPoint = Size × ReactorOrigin + ReactorOriginOffset</c>. The expression starts once per
/// element. An origin change or a <c>.CenterPoint()</c> change only writes (or animates) one of the
/// two property-set entries, so it never restarts.
/// </summary>
/// <remarks>
/// <para>While an origin is active the expression owns <c>Visual.CenterPoint</c>: a <c>.CenterPoint()</c>
/// write goes to <see cref="OffsetProperty"/> (<see cref="AnimationHelper"/>), and nothing else may write
/// or animate the visual's <c>CenterPoint</c>. Under a curve the offset itself animates
/// (<c>visual.Properties.StartAnimation</c>), so the expression keeps running.</para>
/// <para><c>CompositionPropertySet</c> can't delete an entry, so unset, pool cleanup and navigation put
/// the origin into an inactive state instead: both entries zero, no animation on either, and the static
/// <c>.CenterPoint()</c> value (or zero) on the visual.</para>
/// <para>An origin of <c>(0, 0)</c> is the leading top corner, which the static <c>CenterPoint</c> already
/// gives, so it stays inactive and doesn't take the visual (§3 invariant 2).</para>
/// </remarks>
internal static class TransformOriginBinding
{
    internal const string OriginProperty = "ReactorOrigin";
    internal const string OffsetProperty = "ReactorOriginOffset";

    private const string PivotExpression =
        "Vector3(t.Size.X * t." + OriginProperty + ".X + t." + OffsetProperty + ".X, "
        + "t.Size.Y * t." + OriginProperty + ".Y + t." + OffsetProperty + ".Y, t." + OffsetProperty + ".Z)";

    private static readonly Vector2 Centre = new(0.5f, 0.5f);

    /// <summary>Whether the element's pivot is the origin expression (its <c>CenterPoint</c> writes go to the offset).</summary>
    internal static bool IsActive(Reconciler.ReactorState? state) => state?.TransformRest?.Origin is not null;

    /// <summary>Declares the element's size-relative origin.</summary>
    internal static void Apply(UIElement element, Vector2 origin)
    {
        if (origin == Vector2.Zero)
        {
            Deactivate(element);
            return;
        }

        var visual = AnimationHelper.GetElementVisual(element, out var state);
        var rest = state.TransformRest ??= new();
        var properties = visual.Properties;
        properties.InsertVector2(OriginProperty, origin);
        if (rest.Origin is null)
        {
            // The static .CenterPoint() value becomes the offset; an animation on it ended with the
            // static channel's own animation, which Start stops.
            properties.InsertVector3(OffsetProperty, rest.CenterPoint);
            if (!state.OriginExpressionRunning) Start(visual, state);
        }

        rest.Origin = origin;
    }

    /// <summary>The unset arm: puts the origin into the inactive state, restoring the static pivot.</summary>
    internal static void Deactivate(UIElement element)
    {
        if (element.GetValue(Reconciler.ReactorAttached.StateProperty) is not Reconciler.ReactorState
            { TransformRest: { Origin: not null } rest, HandedOutVisual: { } visual } state)
            return;
        rest.Origin = null;
        Release(visual, state, rest.CenterPoint);
    }

    /// <summary>
    /// After a navigation transition: puts the page's own origin back into the pivot expression, or
    /// leaves the inactive state when the page declares none. A visual that was never bound (no
    /// origin and no DrillIn) is left alone.
    /// </summary>
    /// <remarks>
    /// <c>NormalizeVisual</c> leaves a bound visual's <c>CenterPoint</c> expression running, and this
    /// writes into it. Stopping and restarting it in the same commit as the property writes rendered
    /// the previous pivot until the next input change (measured on a hosted runner).
    /// </remarks>
    internal static void Restore(UIElement element, Visual visual)
    {
        if (!IsBound(visual)) return;
        var state = Reconciler.GetOrCreateReactorState(element);
        if (state.TransformRest is not { Origin: { } origin } rest)
        {
            Release(visual, state, state.TransformRest?.CenterPoint ?? Vector3.Zero);
            return;
        }

        var properties = visual.Properties;
        properties.InsertVector2(OriginProperty, origin);
        properties.InsertVector3(OffsetProperty, rest.CenterPoint);
        if (!state.OriginExpressionRunning) Start(visual, state);
    }

    /// <summary>
    /// Navigation's DrillIn pivots the page on its centre (it used to bind its own
    /// <c>Size / 2</c> expression). <see cref="Restore"/> puts the page's own origin back afterwards.
    /// </summary>
    internal static void BindCentre(UIElement page, Visual visual)
    {
        var state = Reconciler.GetOrCreateReactorState(page);
        var properties = visual.Properties;
        properties.InsertVector2(OriginProperty, Centre);
        properties.InsertVector3(OffsetProperty, Vector3.Zero);
        if (!state.OriginExpressionRunning) Start(visual, state);
    }

    /// <summary>Whether <c>NormalizeVisual</c> must leave the visual's <c>CenterPoint</c> to <see cref="Restore"/>.</summary>
    internal static bool OwnsCenterPoint(Visual visual) => IsBound(visual);
    /// <summary>
    /// Pool cleanup (§7.3 steps 1 and 4): the caller already stopped the visual's <c>CenterPoint</c>
    /// and wrote zero to it, and forgets the bookkeeping afterwards.
    /// </summary>
    internal static void ResetForPool(Visual visual)
    {
        if (!IsBound(visual)) return;
        var properties = visual.Properties;
        properties.StopAnimation(OffsetProperty);
        properties.InsertVector2(OriginProperty, Vector2.Zero);
        properties.InsertVector3(OffsetProperty, Vector3.Zero);
    }

    // Both entries are inserted together, and StopAnimation on an entry that was never inserted throws.
    private static bool IsBound(Visual visual)
        => visual.Properties.TryGetVector2(OriginProperty, out _) == CompositionGetValueStatus.Succeeded;

    private static void Start(Visual visual, Reconciler.ReactorState state)
    {
        visual.StopAnimation("CenterPoint");
        var pivot = visual.Compositor.CreateExpressionAnimation(PivotExpression);
        pivot.SetReferenceParameter("t", visual);
        visual.StartAnimation("CenterPoint", pivot);
        state.OriginExpressionRunning = true;
    }

    private static void Release(Visual visual, Reconciler.ReactorState state, Vector3 center)
    {
        state.OriginExpressionRunning = false;
        var properties = visual.Properties;
        // The insert below replaces a running offset animation too (measured); the stop keeps that explicit.
        properties.StopAnimation(OffsetProperty);
        visual.StopAnimation("CenterPoint");
        properties.InsertVector2(OriginProperty, Vector2.Zero);
        properties.InsertVector3(OffsetProperty, Vector3.Zero);
        // StopAnimation leaves the expression's last value. An .Animate(CenterPoint) key must not
        // animate this back to the static pivot.
        Reconciler.WithoutImplicitAnimations(visual, () => visual.CenterPoint = center);
    }
}
