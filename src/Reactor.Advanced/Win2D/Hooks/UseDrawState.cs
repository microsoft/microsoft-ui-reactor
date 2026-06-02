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
    /// Treat the returned <see cref="Ref{T}.Current"/> like a <c>volatile</c>
    /// field — Win2D's animated callbacks run on the game thread while UI
    /// re-renders may mutate <c>Current</c> on the UI thread. See
    /// <see href="docs/guide/win2d-canvas.md#threading">the Win2D canvas threading guide</see>.
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
