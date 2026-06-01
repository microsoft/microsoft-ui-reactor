using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Per-control "suppress next change event" counter used by the reconciler.
///
/// Background — why this exists:
///   A Reactor Update handler that writes a value-bearing DP (<c>cp.Color = ...</c>,
///   <c>nb.Value = ...</c>, <c>ts.IsOn = ...</c>) synthesizes a ValueChanged /
///   ColorChanged / Toggled / etc. event. If the user wired an OnChanged callback
///   (via <c>ColorPicker(..., onChanged: ...)</c> and friends), that callback
///   gets invoked with the value WE just wrote — which is indistinguishable from
///   a real user interaction. If the owning component's state derives from
///   another source (e.g. a PropertyGrid bound to the selected row), the echo
///   writes the new value back into the PREVIOUS state — a silent cross-row
///   value swap. See spec-030 investigation notes.
///
/// Contract:
///   - Before a programmatic write that will raise the control's change event,
///     call <see cref="BeginSuppress(UIElement)"/>. Pair it 1:1 with the write.
///   - The registered event handler must call <see cref="ShouldSuppress(UIElement)"/>
///     as its first line and return early if it returns true. That consumes
///     one suppression token.
///   - Callers should guard with an equality check (only suppress when the new
///     value actually differs) so the token is always consumed by a real event.
///
/// Storage — why <see cref="Reconciler.ReactorAttached.StateProperty"/> instead
/// of a <c>ConditionalWeakTable&lt;UIElement, …&gt;</c>: WinRT projection can
/// produce two managed RCWs over the same underlying <c>DependencyObject</c>.
/// A CWT keyed by RCW would store the BeginSuppress count on one wrapper while
/// the queued change-event handler runs against a different wrapper, so
/// ShouldSuppress would miss the token and the echo would fire (issues #86,
/// #114). The attached DP lives on the native object, so every wrapper sees
/// the same counter — the same fix shape used for <c>ModifierEventHandlerState</c>.
/// </summary>
internal static class ChangeEchoSuppressor
{
    /// <summary>
    /// Increment the suppress counter before a programmatic property write that
    /// will raise a change event. Pair exactly one BeginSuppress with exactly
    /// one expected event.
    /// </summary>
    // <snippet:change-echo>
    internal static void BeginSuppress(UIElement control)
    {
        if (control is not FrameworkElement fe) return;
        Reconciler.GetOrCreateReactorState(fe).EchoSuppressCount++;
    }

    /// <summary>
    /// Returns <c>true</c> if the current event fire should be suppressed (and
    /// decrements the counter). Returns <c>false</c> otherwise. Call at the top
    /// of a change-event handler before invoking the user's OnChanged.
    /// </summary>
    internal static bool ShouldSuppress(UIElement control)
    {
        if (control is not FrameworkElement fe) return false;
        if (fe.GetValue(Reconciler.ReactorAttached.StateProperty) is not Reconciler.ReactorState state)
            return false;
        // §8.2 — setter-suppression scope: drop the echo without consuming a
        // counter token. The scope wraps ApplySetters, where the engine can't
        // predict which value-bearing DPs the user's `.Set(...)` will write.
        if (state.EchoSuppressScopeDepth > 0) return true;
        if (state.EchoSuppressCount > 0)
        {
            state.EchoSuppressCount--;
            return true;
        }
        return false;
    }
    // </snippet:change-echo>

    /// <summary>
    /// Spec 047 §8 value-diff echo suppression. Arms a one-shot predicate that
    /// recognizes the engine-synthesized change event for a programmatic
    /// controlled write by its readback value. Pair with a bare write (no
    /// counter bump). The matching change-event trampoline calls
    /// <see cref="ShouldSuppressEcho"/> which consumes the arm. Used only on
    /// migrated single-value, exact-comparable, synchronous controlled
    /// round-trips; the counter remains the mechanism everywhere else.
    /// </summary>
    internal static void ArmExpectedEcho(UIElement control, Func<object?, bool> matches)
    {
        if (control is not FrameworkElement fe) return;
        Reconciler.GetOrCreateReactorState(fe).PendingEchoMatch = matches;
    }

    /// <summary>Clears any pending value-diff arm (e.g. at Mount, defending
    /// against a stale arm left on a pooled control).</summary>
    internal static void ClearExpectedEcho(UIElement control)
    {
        if (control is not FrameworkElement fe) return;
        if (fe.GetValue(Reconciler.ReactorAttached.StateProperty) is Reconciler.ReactorState state)
            state.PendingEchoMatch = null;
    }

    /// <summary>
    /// Value-diff counterpart of <see cref="ShouldSuppress"/> for change-event
    /// trampolines. Returns <c>true</c> if this fire is an engine-synthesized
    /// echo that should be dropped.
    ///
    /// The causal counter / setter scope still wins first (external
    /// <c>WriteSuppressed</c> or an <c>ApplySetters</c> scope, which carry no
    /// expected value). When the counter/scope path fires, any value-diff arm
    /// still set is cleared unconditionally — it belongs to a synchronous-echo
    /// control whose own echo already fired (arm consumed) or was a no-op write
    /// that will never echo, so there is nothing to leak; clearing it prevents
    /// it from stranding and swallowing the user's next real interaction.
    /// Otherwise the one-shot value-diff predicate is consumed and its match
    /// result returned — a mismatch means a real user change superseded the
    /// pending write, so the arm is cleared and the event falls through to the
    /// user callback.
    /// </summary>
    internal static bool ShouldSuppressEcho(UIElement control, object? currentReadback)
    {
        if (control is not FrameworkElement fe) return false;
        if (fe.GetValue(Reconciler.ReactorAttached.StateProperty) is not Reconciler.ReactorState state)
            return false;

        if (state.EchoSuppressScopeDepth > 0 || state.EchoSuppressCount > 0)
        {
            // The counter/scope is suppressing THIS event. Any value-diff arm
            // still set here belongs to a synchronous-echo control whose own
            // echo already fired-and-consumed (arm would be null) or was a
            // guarded/no-op write that will never echo — so there is no future
            // echo to leak. Clear it unconditionally so a coincident later real
            // event whose readback happens to match cannot be swallowed.
            // (Deferred-echo controls do NOT use this arm — ControlledPropEntry
            // keeps its own per-payload arm with drain-on-match semantics.)
            state.PendingEchoMatch = null;
            // Mirror ShouldSuppress: scope is non-consuming; a counter token is.
            if (state.EchoSuppressScopeDepth == 0)
                state.EchoSuppressCount--;
            return true;
        }

        if (state.PendingEchoMatch is { } match)
        {
            state.PendingEchoMatch = null;
            return match(currentReadback);
        }
        return false;
    }
}
