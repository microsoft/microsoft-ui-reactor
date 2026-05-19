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
/// the same counter — the same fix shape used for <c>EventHandlerState</c>.
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
        if (fe.GetValue(Reconciler.ReactorAttached.StateProperty) is Reconciler.ReactorState state
            && state.EchoSuppressCount > 0)
        {
            state.EchoSuppressCount--;
            return true;
        }
        return false;
    }
    // </snippet:change-echo>
}
