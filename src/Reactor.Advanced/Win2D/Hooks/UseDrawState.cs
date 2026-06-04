using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Advanced.Win2D;

/// <summary>
/// Hooks for Win2D draw-loop state.
/// </summary>
public static class UseDrawStateHook
{
    /// <summary>
    /// Creates a stable mutable draw-state reference for Win2D callbacks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned <see cref="Ref{T}"/> is created once per component instance and survives
    /// re-renders, so animated Win2D callbacks see the same object across UI state changes.
    /// </para>
    /// <para>
    /// <b>Thread-safety contract.</b> <see cref="Ref{T}"/> is a plain auto-property: it does
    /// <i>not</i> insert memory barriers and does <i>not</i> guarantee that reads from
    /// Win2D's game thread observe UI-thread writes promptly, nor that compound mutations of
    /// the referenced object are atomic. Authors who share <see cref="Ref{T}.Current"/> across
    /// the UI and Win2D game threads must enforce their own synchronization:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>For replaced <see cref="Ref{T}.Current"/> values typed as
    ///   <see langword="class"/> references on a 64-bit runtime, assignment is single-word
    ///   atomic. Use <c>System.Threading.Volatile.Read</c> / <c>Volatile.Write</c> on a
    ///   private field if ordered visibility matters.</description></item>
    ///   <item><description>For mutable arrays or collections shared across threads, use
    ///   <c>lock</c>, <c>System.Collections.Concurrent</c> primitives, or a producer/
    ///   consumer hand-off queue drained by the game thread at the start of each tick.</description></item>
    ///   <item><description>For primitive counters mutated from the game thread and read from
    ///   the UI thread (e.g., FPS samples), use <c>System.Threading.Interlocked</c> or
    ///   <c>System.Threading.Volatile</c>.</description></item>
    /// </list>
    /// <para>
    /// See <see href="docs/guide/win2d-canvas.md#threading">the Win2D canvas threading guide</see>
    /// for the full discussion.
    /// </para>
    /// </remarks>
    public static Ref<T> UseDrawState<T>(this RenderContext ctx, Func<T> init)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(init);

        var holder = ctx.UseRef<Ref<T>?>(null);
        if (holder.Current is null)
            holder.Current = new Ref<T>(init());

        var drawState = holder.Current;
        ctx.UseEffect(() => () =>
        {
            if (drawState.Current is IDisposable disposable)
                disposable.Dispose();
        }, Array.Empty<object>());

        return drawState;
    }
}
