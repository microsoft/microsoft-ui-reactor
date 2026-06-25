namespace Microsoft.UI.Reactor.Core.Internal;

/// <summary>
/// Immutable snapshot of an active <see cref="Animations.Animate(AnimationKind, global::System.Action)"/>
/// transaction. Carried on an <see cref="global::System.Threading.AsyncLocal{T}"/> stack while the
/// transaction is open, and snapshotted into pending-render state by
/// <c>UseState</c> / <c>UseReducer</c> setters so the eventual render observes
/// the same intent even if the rerender hops a dispatcher and the transaction
/// has already unwound. (spec 042 §6, Q3)
/// </summary>
/// <remarks>
/// <para>
/// Record-class shape keeps the type immutable and reference-equality-cheap
/// for the per-render comparisons the reconciler performs. The struct
/// <see cref="AnimationAmbient.Scope"/> nested type is the only mutating
/// surface — it push/pops the <see cref="AnimationAmbient.Current"/>
/// AsyncLocal and is the recommended way to enter and leave a transaction.
/// </para>
/// <para>
/// <b>Thread / context propagation.</b> <see cref="global::System.Threading.AsyncLocal{T}"/>
/// flows with <see cref="global::System.Threading.ExecutionContext"/>, which is captured by
/// <c>DispatcherQueue.TryEnqueue</c>, <c>Task.Run</c>, and most other deferred
/// dispatch primitives. So a setter that fires inside an <c>Animate</c>
/// block and only triggers a rerender via a dispatcher hop still sees the
/// ambient when the deferred work runs. The "snapshot in setters" pattern
/// (per spec 042 §9 Q3) is the explicit insurance against primitives that
/// don't capture <see cref="global::System.Threading.ExecutionContext"/> (e.g.
/// <c>ThreadPool.UnsafeQueueUserWorkItem</c>) — setters capture
/// <see cref="AnimationAmbient.Current"/> synchronously and stash it for
/// the reconciler to re-push around the render.
/// </para>
/// </remarks>
internal sealed record AmbientAnimation(AnimationKind Kind)
{
    /// <summary>True when the ambient intent is something other than <see cref="AnimationKind.None"/>.</summary>
    public bool HasEffect => Kind != AnimationKind.None;
}

/// <summary>
/// Static accessor for the ambient <see cref="AmbientAnimation"/>. Reads
/// are O(1) via <see cref="global::System.Threading.AsyncLocal{T}"/>; writes go through
/// <see cref="Scope"/>. (spec 042 §6)
/// </summary>
internal static class AnimationAmbient
{
    private static readonly global::System.Threading.AsyncLocal<AmbientAnimation?> _current = new();

    // #183: one-way latch flipped the first time a non-null ambient is pushed.
    // Host setState paths read AnimationAmbient.Current (an AsyncLocal walk over
    // the ExecutionContext) on every state update; gating that read behind this
    // cheap volatile bool lets animation-free apps skip it entirely. Behaviour-
    // preserving: while this stays false no Scope has ever published a non-null
    // ambient, so Current is provably null and the gated read would have
    // returned null anyway. Never reset — once any Animate scope has run,
    // callers always read Current.
    private static volatile bool _hasAny;

    /// <summary>
    /// True once any non-null ambient has been pushed via <see cref="Scope"/>
    /// during the process lifetime. A cheap sentinel that lets hot setState
    /// paths skip the <see cref="Current"/> AsyncLocal read until the first
    /// <see cref="Animations.Animate(AnimationKind, global::System.Action)"/>
    /// transaction is entered. (#183)
    /// </summary>
    public static bool HasAny => _hasAny;

    /// <summary>
    /// The currently-active ambient, or <see langword="null"/> when no
    /// <see cref="Animations.Animate(AnimationKind, global::System.Action)"/>
    /// transaction is open on this logical async flow.
    /// </summary>
    public static AmbientAnimation? Current => _current.Value;

    /// <summary>
    /// RAII push of the next ambient. The previous value is restored on
    /// <c>Dispose</c>; nested <c>Animate</c> blocks stack naturally because
    /// each scope captures the value it displaced.
    /// </summary>
    public readonly struct Scope : global::System.IDisposable
    {
        private readonly AmbientAnimation? _previous;
        private readonly bool _entered;

        /// <summary>Push <paramref name="next"/> onto the ambient stack.</summary>
        public Scope(AmbientAnimation? next)
        {
            _previous = _current.Value;
            _current.Value = next;
            _entered = true;
            // Latch the sentinel so subsequent setState paths know an ambient
            // may be observable (#183). Only a non-null push matters — a null
            // push leaves Current null, which the gate already handles.
            if (next is not null) _hasAny = true;
        }

        /// <summary>Restore the ambient that was displaced when this scope was constructed.</summary>
        public void Dispose()
        {
            if (!_entered) return;
            _current.Value = _previous;
        }
    }
}
