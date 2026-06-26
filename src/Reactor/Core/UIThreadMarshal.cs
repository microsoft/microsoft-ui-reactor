namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Shared UI-thread marshaling gate for hook mutators that run off the render
/// thread. Mirrors the auto-marshal contract introduced for <c>UseState</c> /
/// <c>UseReducer</c> in issue #212 (see <see cref="RenderContext"/>'s private
/// <c>MarshalIfOffUIThread</c>) so the rest of the hook surface can stay
/// thread-safe-by-default without each call site reimplementing the same
/// thread-id check + dispatcher hop.
/// </summary>
/// <remarks>
/// The design goal (issue #234) is that authors should not have to predict upfront
/// whether a mutation will originate off-thread. The intended call shape keeps the
/// UI-thread fast path allocation-free:
/// <code>
/// if (UIThreadMarshal.IsOffUIThread(_uiThreadId)
///     &amp;&amp; UIThreadMarshal.MarshalOffUIThread(_uiThreadId, "Op", () => Op(...)))
///     return; // scheduled on the UI dispatcher
/// // ... inline body runs on the UI thread ...
/// </code>
/// Because the closure is the argument to <see cref="MarshalOffUIThread"/> on the
/// right of <c>&amp;&amp;</c>, it is only allocated when <see cref="IsOffUIThread"/>
/// is <see langword="true"/>. On the UI thread the gate is a single
/// <see cref="Environment.CurrentManagedThreadId"/> read plus an
/// <see langword="int"/> compare (~1&#8211;2&#160;ns, zero allocation), so the
/// common case stays lock-light. Only the genuinely off-thread path pays for a
/// marshal closure + dispatcher enqueue.
/// </remarks>
internal static class UIThreadMarshal
{
    /// <summary>
    /// Fast UI-thread check. Returns <see langword="true"/> when the caller is NOT
    /// on the thread identified by <paramref name="uiThreadId"/> (the render/UI
    /// thread captured when the owning hook handle was created).
    /// </summary>
    public static bool IsOffUIThread(int uiThreadId)
        => global::System.Environment.CurrentManagedThreadId != uiThreadId;

    /// <summary>
    /// Shared off-thread dispatch primitive: enqueues <paramref name="work"/> via
    /// <paramref name="tryEnqueue"/>, or throws a caller-supplied diagnostic when no
    /// dispatcher is available (<paramref name="tryEnqueue"/> is <see langword="null"/>)
    /// or the dispatcher refuses the enqueue (<paramref name="tryEnqueue"/> returns
    /// <see langword="false"/>). Both <see cref="RenderContext"/>'s hook-setter
    /// marshal and <c>NavigationHandle&lt;TRoute&gt;</c>'s mutator gate funnel
    /// through this single implementation so the marshal-or-throw contract from
    /// issue #212 has exactly one copy. The caller is responsible for the UI-thread
    /// fast-path check BEFORE calling this (this method assumes it is already
    /// off-thread). The message factories are only invoked on the throwing paths, so
    /// the successful-enqueue path allocates no message strings.
    /// </summary>
    /// <param name="tryEnqueue">
    /// Posts the work onto the captured UI dispatcher and returns whether it was
    /// accepted; <see langword="null"/> when no dispatcher has been captured.
    /// </param>
    /// <param name="work">The mutation to run on the UI thread.</param>
    /// <param name="onNoDispatcher">
    /// Builds the exception message thrown when no dispatcher is available.
    /// </param>
    /// <param name="onRefused">
    /// Builds the exception message thrown when the dispatcher refuses the enqueue.
    /// </param>
    public static bool EnqueueOrThrow(
        global::System.Func<global::System.Action, bool>? tryEnqueue,
        global::System.Action work,
        global::System.Func<string> onNoDispatcher,
        global::System.Func<string> onRefused)
    {
        if (tryEnqueue is null)
        {
            // Test/headless context with no captured UI dispatcher AND off-thread.
            // Surface loudly instead of silently racing the reconciler.
            throw new global::System.InvalidOperationException(onNoDispatcher());
        }

        // TryEnqueue returns false once the dispatcher has begun shutting down
        // (queue closed, owning thread exiting). Silently swallowing that would
        // lose the mutation with no diagnostic; throw so the caller sees the same
        // loud failure mode as the no-dispatcher path.
        if (!tryEnqueue(work))
        {
            throw new global::System.InvalidOperationException(onRefused());
        }

        return true;
    }

    /// <summary>
    /// Marshals <paramref name="work"/> onto the captured UI dispatcher. Call this
    /// only once <see cref="IsOffUIThread"/> has confirmed the caller is off-thread
    /// (the short-circuit pattern in the type remarks keeps the closure off the hot
    /// path). Always returns <see langword="true"/> when the work is scheduled, so
    /// callers can write
    /// <c>if (IsOffUIThread(id) &amp;&amp; MarshalOffUIThread(...)) return;</c>.
    /// <para>
    /// When no <see cref="Microsoft.UI.Reactor.ReactorApp.UIDispatcher"/> has been
    /// captured (unit-test / headless contexts) or when the dispatcher has already
    /// begun shutting down, this throws <see cref="InvalidOperationException"/>
    /// instead of silently racing the reconciler or dropping the mutation &#8212;
    /// closing the same silent-failure gap #212 closed for state setters.
    /// </para>
    /// </summary>
    /// <param name="uiThreadId">
    /// The managed thread id captured on the render/UI thread (used only in the
    /// thrown diagnostic message).
    /// </param>
    /// <param name="operation">
    /// Human-readable name of the mutating operation, used in the thrown message.
    /// </param>
    /// <param name="work">
    /// The mutation to re-invoke on the UI dispatcher. The caller makes this
    /// idempotent by simply re-calling the same public method (which re-enters the
    /// gate and falls through inline on the UI thread).
    /// </param>
    public static bool MarshalOffUIThread(int uiThreadId, string operation, global::System.Action work)
    {
        var dq = Microsoft.UI.Reactor.ReactorApp.UIDispatcher;
        return EnqueueOrThrow(
            dq is null ? null : w => dq.TryEnqueue(w.Invoke),
            work,
            () =>
            {
                int callerThreadId = global::System.Environment.CurrentManagedThreadId;
                return $"{operation} was called from thread {callerThreadId}, " +
                    $"but the captured UI thread is {uiThreadId}, and no UI dispatcher is " +
                    $"available to marshal the call. Invoke it on the UI thread.";
            },
            () =>
            {
                int callerThreadId = global::System.Environment.CurrentManagedThreadId;
                return $"{operation} was called from thread {callerThreadId}, " +
                    $"but the UI dispatcher refused the marshaled call (TryEnqueue returned " +
                    $"false — typically because the dispatcher is shutting down). The mutation " +
                    $"was dropped. Cancel background work in effect cleanup before window/app " +
                    $"shutdown.";
            });
    }
}
