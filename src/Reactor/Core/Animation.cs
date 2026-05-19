using Microsoft.UI.Reactor.Core.Internal;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Animation kinds that an ambient <c>Animations.Animate(...)</c>
/// transaction can carry through a state-setter into the resulting diff.
/// See spec 042 §6.
/// </summary>
public enum AnimationKind
{
    /// <summary>No animation; container changes apply instantly.</summary>
    None,

    /// <summary>Framework default — currently matches <see cref="EaseOut"/>.</summary>
    Default,

    /// <summary>Critically-damped spring; expressive on insert/remove.</summary>
    Spring,

    /// <summary>Standard quadratic ease-in.</summary>
    EaseIn,

    /// <summary>Standard quadratic ease-out (matches WinUI <c>RepositionThemeTransition</c>).</summary>
    EaseOut,

    /// <summary>Standard ease-in-out.</summary>
    EaseInOut,
}

/// <summary>
/// Transactional animation entry points — the SwiftUI <c>withAnimation { … }</c>
/// analog. See spec 042 §6.
/// </summary>
/// <remarks>
/// <para>
/// <c>Animate(...)</c> pushes an <c>AmbientAnimation</c> onto an
/// <see cref="global::System.Threading.AsyncLocal{T}"/> stack for the duration of the
/// supplied action. State setters from <c>UseState</c> / <c>UseReducer</c>
/// invoked inside the block snapshot the ambient synchronously so the
/// resulting render observes the same intent even when the rerender hops
/// a dispatcher. The reconciler consumes the snapshot when applying
/// <c>KeyedListDiff</c> ops and <c>ChildReconciler</c> mount / move /
/// unmount, configuring per-container animations to match.
/// </para>
/// <para>
/// <c>Animate</c> does <b>not</b> animate arbitrary property changes
/// (color, size) on surviving leaves — that remains the job of
/// per-element modifiers such as <c>WithImplicitTransition</c>. Scoping
/// the transaction to keyed structural changes keeps the SwiftUI
/// "withAnimation only animates layout-shape ops" contract intact.
/// (spec 042 §6, scope discipline)
/// </para>
/// <para>
/// Named <c>Animations</c> (plural) instead of <c>Animation</c> to avoid
/// collision with the existing <c>Microsoft.UI.Reactor.Animation</c>
/// sub-namespace, which houses per-element animation modifiers.
/// </para>
/// </remarks>
public static class Animations
{
    /// <summary>
    /// Wrap a state mutation in an ambient animation transaction.
    /// </summary>
    /// <param name="kind">The animation kind that should apply to any
    /// container insert / move / remove ops produced by state setters
    /// invoked from <paramref name="action"/>. Passing
    /// <see cref="AnimationKind.None"/> is meaningful: it explicitly
    /// suppresses any *outer* <c>Animate</c> for the scope of the inner
    /// block (nested calls stack like <c>using</c>).</param>
    /// <param name="action">State mutation. Typically calls
    /// <c>setItems(...)</c> from a hook.</param>
    /// <example>
    /// <code>
    /// Animate(AnimationKind.Spring, () =&gt; setItems([..items, x]));
    /// </code>
    /// </example>
    public static void Animate(AnimationKind kind, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var _ = new AnimationAmbient.Scope(new AmbientAnimation(kind));
        action();
    }

    /// <summary>
    /// Wrap a value-producing state mutation in an ambient animation
    /// transaction. Returns the value produced by <paramref name="func"/>.
    /// </summary>
    public static T Animate<T>(AnimationKind kind, Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        using var _ = new AnimationAmbient.Scope(new AmbientAnimation(kind));
        return func();
    }
}
