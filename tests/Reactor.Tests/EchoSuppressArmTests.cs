using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #1090 — <c>BeginSuppressCancelable</c> / <c>EchoSuppressArm</c>, the
/// retractable form of the echo-suppress counter token.
///
/// <para><b>Why the primitive exists.</b> <c>BeginSuppress</c> is a promise that
/// an event is coming. <c>ListViewHandler.Update</c> could only <em>predict</em>
/// one: it armed a token and then reassigned <c>ItemsSource</c>, assuming WinUI
/// would drop the selection and fire. That assumption is version-dependent —
/// the runtime in Windows App SDK 2.1.x resets the selection synchronously and
/// fires, while newer runtimes can preserve a still-valid selection and raise
/// nothing. On the second behavior the token strands and
/// <c>ShouldSuppress</c> consumes it on the user's next genuine selection,
/// silently dropping it.</para>
///
/// <para><b>Why these tests are headless.</b> Which branch a live control takes
/// is decided by the WinUI version the host binds, and Reactor's selftest host
/// is self-contained and pinned. A live fixture can therefore only ever exercise
/// <em>one</em> of the two branches on a given machine. Driving
/// <see cref="Reconciler.ReactorState"/> directly reaches both deterministically,
/// which is the only place the retraction contract can actually be pinned.</para>
///
/// <para>Each test names the platform branch it models.</para>
/// </summary>
public class EchoSuppressArmTests
{
    /// <summary>Models the trampoline: consume a token if one is outstanding.</summary>
    private static bool RaiseEvent(Reconciler.ReactorState state)
        => ChangeEchoSuppressor.ShouldSuppress(state);

    [Fact]
    public void BeginSuppressCancelable_ArmsExactlyOneToken()
    {
        var state = new Reconciler.ReactorState();

        var arm = ChangeEchoSuppressor.BeginSuppressCancelable(state);

        Assert.True(arm.IsArmed);
        Assert.Equal(1, state.EchoSuppressCount);
    }

    /// <summary>
    /// Platform branch A — the selection SURVIVES the swap, so the predicted
    /// echo never happens. This is issue #1090: without retraction the token
    /// outlives the write and eats the user's next genuine selection.
    /// </summary>
    [Fact]
    public void SelectionPreserved_NoEcho_RetractionLetsNextRealEventThrough()
    {
        var state = new Reconciler.ReactorState();

        var arm = ChangeEchoSuppressor.BeginSuppressCancelable(state);
        // ...ItemsSource swap happens here and raises NOTHING...
        arm.CancelIfUnconsumed();

        Assert.Equal(0, state.EchoSuppressCount);
        // The user's next genuine selection must reach the callback.
        Assert.False(RaiseEvent(state));
    }

    /// <summary>
    /// The same branch WITHOUT retraction, pinning the defect the primitive
    /// exists to prevent: a plain <c>BeginSuppress</c> strands, and the next
    /// genuine event is swallowed. If this ever stops being true the strand
    /// hazard is gone and the retraction is dead weight.
    /// </summary>
    [Fact]
    public void SelectionPreserved_NoEcho_WithoutRetraction_SwallowsNextRealEvent()
    {
        var state = new Reconciler.ReactorState();

        ChangeEchoSuppressor.BeginSuppressCancelable(state);   // armed, never retracted
        // ...ItemsSource swap happens here and raises NOTHING...

        Assert.True(RaiseEvent(state));      // the user's real selection is eaten
        Assert.Equal(0, state.EchoSuppressCount);
    }

    /// <summary>
    /// Platform branch B — the selection IS dropped and the echo fires
    /// synchronously inside the assignment (measured behavior on WASDK 2.1.x).
    /// The echo consumes the token; the retraction must then be a no-op, and
    /// must not drive the counter negative.
    /// </summary>
    [Fact]
    public void SelectionDropped_SynchronousEcho_RetractionIsNoOp()
    {
        var state = new Reconciler.ReactorState();

        var arm = ChangeEchoSuppressor.BeginSuppressCancelable(state);
        Assert.True(RaiseEvent(state));       // the drop echo is suppressed
        arm.CancelIfUnconsumed();             // caller cannot tell — calls anyway

        Assert.Equal(0, state.EchoSuppressCount);
        Assert.False(RaiseEvent(state));      // next real event still gets through
    }

    /// <summary>
    /// Platform branch C — the selection is dropped but the echo is DEFERRED to
    /// the dispatcher. The caller sees a changed index and keeps the token, which
    /// must still be there when the queued event finally arrives.
    /// </summary>
    [Fact]
    public void SelectionDropped_DeferredEcho_TokenSurvivesUntilEventArrives()
    {
        var state = new Reconciler.ReactorState();

        var arm = ChangeEchoSuppressor.BeginSuppressCancelable(state);
        // Index changed, so the handler does NOT retract.
        Assert.True(arm.IsArmed);
        Assert.Equal(1, state.EchoSuppressCount);

        // ...dispatcher drains later...
        Assert.True(RaiseEvent(state));       // the deferred drop echo is suppressed
        Assert.False(RaiseEvent(state));      // and nothing is left over
    }

    /// <summary>
    /// The handler's usage pattern, end to end, across every platform branch:
    /// arm when there is a selection to lose, swap, then retract iff the index
    /// did not move. The invariant that matters is the one the trampoline sees —
    /// after the swap settles, no token may be left over to eat real input, and
    /// no genuine drop echo may escape.
    /// </summary>
    /// <param name="dropsSelection">Whether the platform clears the selection.</param>
    /// <param name="echoIsDeferred">Whether that drop echo is queued rather than
    /// raised inside the assignment.</param>
    [Theory]
    [InlineData(false, false)]  // selection preserved, no echo   → issue #1090's branch
    [InlineData(true, false)]   // dropped, synchronous echo      → WASDK 2.1.x branch
    [InlineData(true, true)]    // dropped, deferred echo         → dispatcher branch
    public void HandlerPattern_LeavesNoStrandedTokenAndSwallowsOnlyTheDropEcho(
        bool dropsSelection, bool echoIsDeferred)
    {
        var state = new Reconciler.ReactorState();
        int selection = 0;                 // a selection exists before the swap

        // --- the handler's rebuild block -------------------------------------
        int before = selection;
        var arm = before >= 0
            ? ChangeEchoSuppressor.BeginSuppressCancelable(state)
            : default;

        bool dropEchoEscaped = false;
        if (dropsSelection)
        {
            selection = -1;
            if (!echoIsDeferred)
                dropEchoEscaped = !RaiseEvent(state);   // fires inside the assignment
        }

        if (selection == before)
            arm.CancelIfUnconsumed();
        // ---------------------------------------------------------------------

        if (dropsSelection && echoIsDeferred)
            dropEchoEscaped = !RaiseEvent(state);       // dispatcher drains later

        // The engine-synthesized drop must never reach the user callback: that is
        // the #495 render storm.
        Assert.False(dropEchoEscaped);

        // And nothing may be left over: the user's next genuine selection must
        // reach the callback. That is #1090.
        Assert.Equal(0, state.EchoSuppressCount);
        Assert.False(RaiseEvent(state));
    }

    /// <summary>Retraction is idempotent — calling it twice returns one token.</summary>
    [Fact]
    public void CancelIfUnconsumed_CalledTwice_ReturnsOnlyOneToken()
    {
        var state = new Reconciler.ReactorState { EchoSuppressCount = 1 };  // someone else's token

        var arm = ChangeEchoSuppressor.BeginSuppressCancelable(state);      // now 2
        arm.CancelIfUnconsumed();
        arm.CancelIfUnconsumed();

        // The pre-existing token must survive both calls.
        Assert.Equal(1, state.EchoSuppressCount);
    }

    /// <summary>
    /// A retraction must not cancel a token that belonged to an unrelated write
    /// already pending when we armed.
    /// </summary>
    [Fact]
    public void CancelIfUnconsumed_LeavesAPreExistingTokenIntact()
    {
        var state = new Reconciler.ReactorState { EchoSuppressCount = 1 };  // unrelated pending write

        var arm = ChangeEchoSuppressor.BeginSuppressCancelable(state);
        arm.CancelIfUnconsumed();

        Assert.Equal(1, state.EchoSuppressCount);
        Assert.True(RaiseEvent(state));     // the unrelated echo is still suppressed
        Assert.False(RaiseEvent(state));    // and only that one
    }

    /// <summary>
    /// <c>default</c> is the "never armed" value — the shape a handler uses when
    /// there was no selection to lose. Every operation on it is inert.
    /// </summary>
    [Fact]
    public void DefaultArm_IsNotArmedAndCancelIsInert()
    {
        var state = new Reconciler.ReactorState();
        var arm = default(ChangeEchoSuppressor.EchoSuppressArm);

        Assert.False(arm.IsArmed);
        arm.CancelIfUnconsumed();

        Assert.Equal(0, state.EchoSuppressCount);
        Assert.False(RaiseEvent(state));
    }

    /// <summary>
    /// The retraction returns a counter token, so it must not disturb the
    /// non-consuming setter scope or a pending value-diff arm.
    /// </summary>
    [Fact]
    public void CancelIfUnconsumed_DoesNotDisturbScopeOrValueDiffArm()
    {
        var state = new Reconciler.ReactorState
        {
            EchoSuppressScopeDepth = 1,
            PendingEchoMatch = _ => true,
        };

        var arm = ChangeEchoSuppressor.BeginSuppressCancelable(state);
        arm.CancelIfUnconsumed();

        Assert.Equal(0, state.EchoSuppressCount);
        Assert.Equal(1, state.EchoSuppressScopeDepth);
        Assert.NotNull(state.PendingEchoMatch);
    }
}
