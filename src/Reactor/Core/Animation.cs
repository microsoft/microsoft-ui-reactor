namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Animation kinds that an ambient <c>Animations.Animate(...)</c>
/// transaction can carry through a state-setter into the resulting diff.
/// See spec 042 §6. Phase 3 will consume these in the reconciler; Phase 1
/// only ships the API shells so callers can stage adoption.
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
/// Phase 3 will plumb an <c>AsyncLocal&lt;AmbientAnimation?&gt;</c> stack
/// through the state-setter dispatch and into <c>KeyedListDiff.Apply</c> /
/// <c>ChildReconciler.Reconcile</c>, so a single state mutation can tag
/// all of its resulting container ops with one animation kind. This file
/// is the Phase 0 placeholder: the methods exist with their final
/// signatures but currently just invoke the action — <b>callers can adopt
/// the syntax now without behavior changing.</b>
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
    /// invoked from <paramref name="action"/>.</param>
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
        // Phase 3 will push/pop an AsyncLocal here. Today: no-op pass-through.
        action();
    }

    /// <summary>
    /// Wrap a value-producing state mutation in an ambient animation
    /// transaction. Returns the value produced by <paramref name="func"/>.
    /// </summary>
    public static T Animate<T>(AnimationKind kind, Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        // Phase 3 will push/pop an AsyncLocal here. Today: no-op pass-through.
        return func();
    }
}
