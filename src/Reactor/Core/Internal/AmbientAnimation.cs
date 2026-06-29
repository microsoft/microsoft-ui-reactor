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

    // Issue #660 (#183): one-way sentinel flipped true the first time any
    // non-null ambient is pushed. RequestRender reads AnimationAmbient.Current
    // (an AsyncLocal walk of the ExecutionContext) on every setState — ~1000/sec
    // from background threads on the data-grid workload — even though the vast
    // majority of apps never open an Animations.Animate(...) transaction. Gating
    // that read behind this volatile bool turns the common case into a single
    // relaxed bool read. Mirrors AnimationScope.HasScope.
    private static volatile bool _hasAny;

    /// <summary>
    /// True once any <see cref="Animations.Animate(AnimationKind, global::System.Action)"/>
    /// transaction has ever opened in this process. When false, <see cref="Current"/>
    /// is guaranteed null, so callers can skip the AsyncLocal read entirely.
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
            // Issue #660 (#183): flip the sentinel so future Current reads take
            // the real AsyncLocal path. One-way: once an ambient has existed,
            // every read must consult the AsyncLocal to stay correct.
            if (next is not null)
                _hasAny = true;
        }

        /// <summary>Restore the ambient that was displaced when this scope was constructed.</summary>
        public void Dispose()
        {
            if (!_entered) return;
            _current.Value = _previous;
        }
    }
}
