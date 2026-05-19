using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hooks;

/// <summary>
/// Hook for imperative element focus (spec 027 Tier 5). Returns a stable
/// <see cref="ElementRef"/> (survives re-renders) plus a <c>RequestFocus</c> action
/// that schedules <see cref="Microsoft.UI.Reactor.Input.FocusManager.Focus"/> on the UI dispatcher. Scheduling
/// defers focus past the current reconcile pass so callers can safely request focus
/// from effects or event handlers without racing against layout.
/// </summary>
public static class UseElementFocusExtensions
{
    /// <summary>
    /// Creates (or retrieves) a component-scoped <see cref="ElementRef"/> and pairs it
    /// with a <c>RequestFocus</c> action. Bind the ref to an element via <c>.Ref(ref)</c>.
    /// Calling <c>RequestFocus</c> schedules <see cref="Microsoft.UI.Reactor.Input.FocusManager.Focus"/> on the UI
    /// dispatcher; if the ref's target has not mounted yet the call is a no-op.
    /// </summary>
    /// <example>
    /// var (inputRef, requestFocus) = ctx.UseElementFocus();
    /// ctx.UseEffect(() => requestFocus(), Array.Empty&lt;object&gt;()); // focus on first render
    /// return TextField(value, setValue).Ref(inputRef);
    /// </example>
    public static (ElementRef Ref, Action RequestFocus) UseElementFocus(this RenderContext ctx,
        FocusState state = FocusState.Programmatic)
    {
        var (elRef, _) = ctx.UseState(new ElementRef());
        // Capture the UI dispatcher at render time — RequestFocus may be called from a
        // background thread (e.g. from UseEffect cleanup, task continuations), where
        // GetForCurrentThread() would return the wrong queue or null.
        // Guard the call itself: in unit-test / headless contexts the WinUI activation
        // factory isn't registered and GetForCurrentThread throws a COMException.
        Microsoft.UI.Dispatching.DispatcherQueue? uiQueue;
        try { uiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(); }
        catch { uiQueue = null; }
        Action requestFocus = () =>
        {
            if (uiQueue is null)
            {
                // No dispatcher available (headless/tests) — invoke synchronously.
                Microsoft.UI.Reactor.Input.FocusManager.Focus(elRef, state);
                return;
            }
            uiQueue.TryEnqueue(() => Microsoft.UI.Reactor.Input.FocusManager.Focus(elRef, state));
        };
        return (elRef, requestFocus);
    }

    /// <summary>
    /// Component-extension overload of <see cref="UseElementFocus(RenderContext, FocusState)"/>.
    /// Equivalent to calling the <see cref="RenderContext"/>-extension form against
    /// <c>component.Context</c>; see that overload for the full contract.
    /// </summary>
    /// <param name="component">The component whose render context owns the hook slot.</param>
    /// <param name="state">The focus state used by the imperative request.</param>
    public static (ElementRef Ref, Action RequestFocus) UseElementFocus(this Component component,
        FocusState state = FocusState.Programmatic)
        => component.Context.UseElementFocus(state);
}
