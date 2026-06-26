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
    /// return TextBox(value, setValue).Ref(inputRef);
    /// </example>
    public static (ElementRef Ref, Action RequestFocus) UseElementFocus(this RenderContext ctx,
        FocusState state = FocusState.Programmatic)
    {
        // The ElementRef must survive re-renders but never changes after the first.
        // UseMemo with empty deps allocates it exactly once via a non-capturing static
        // factory; UseState(new ElementRef()) would eagerly allocate (and discard) a
        // fresh ElementRef on EVERY render even though only the first is ever kept.
        var elRef = ctx.UseMemo(static () => new ElementRef(), Array.Empty<object>());

        // #55: previously this hook captured the UI dispatcher via a
        // GetForCurrentThread() COM call AND allocated a fresh `requestFocus`
        // closure on EVERY render (multiplied by every focusable cell). Both are
        // now built exactly once and stashed in a UseRef cell:
        //   • the dispatcher is resolved a single time (it never changes for the
        //     component's UI thread), and
        //   • the closure is allocated once and reused; the latest `state` is
        //     written into the cache each render (a cheap volatile field store,
        //     no allocation) and read at invoke time, so focus still uses the
        //     current render's focus state. The cache fields are `volatile` so a
        //     background-thread invoke sees the latest values (see FocusHookCache).
        var cacheRef = ctx.UseRef<FocusHookCache?>(null);
        var cache = cacheRef.Current;
        if (cache is null)
        {
            cache = new FocusHookCache { State = state };
            // Capture the UI dispatcher once — RequestFocus may be called from a
            // background thread (UseEffect cleanup, task continuations) where
            // GetForCurrentThread() would return the wrong queue or null. Guard the
            // call: in unit-test / headless contexts the WinUI activation factory
            // isn't registered and GetForCurrentThread throws a COMException.
            try { cache.UiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(); }
            catch (global::System.Runtime.InteropServices.COMException) { cache.UiQueue = null; }
            cache.RequestFocus = () =>
            {
                if (cache.UiQueue is null)
                {
                    // No dispatcher available (headless/tests) — invoke synchronously.
                    Microsoft.UI.Reactor.Input.FocusManager.Focus(elRef, cache.State);
                    return;
                }
                cache.UiQueue.TryEnqueue(() => Microsoft.UI.Reactor.Input.FocusManager.Focus(elRef, cache.State));
            };
            cacheRef.Current = cache;
        }
        else
        {
            cache.State = state;
        }

        return (elRef, cache.RequestFocus);
    }

    private sealed class FocusHookCache
    {
        // RequestFocus may be invoked from a background thread (task continuations),
        // while UiQueue (write-once at construction) and State (refreshed each render)
        // are written on the UI render thread. `volatile` gives those cross-thread
        // reads/writes release/acquire semantics so a background invoke never observes
        // a stale UiQueue (e.g. a spurious null -> a wrong synchronous off-thread
        // focus) or an outdated FocusState on a weak memory model (e.g. ARM64).
        // RequestFocus itself is write-once and only read on the render thread.
        public volatile Microsoft.UI.Dispatching.DispatcherQueue? UiQueue;
        public volatile FocusState State;
        public Action RequestFocus = default!;
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
