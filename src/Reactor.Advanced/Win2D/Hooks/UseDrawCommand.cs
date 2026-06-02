using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Advanced.Win2D;

/// <summary>
/// Hooks for memoizing Win2D draw delegates.
/// </summary>
public static class UseDrawCommandHook
{
    /// <summary>
    /// Memoizes a Win2D draw callback and rebuilds it only when <paramref name="deps"/> change.
    /// </summary>
    /// <remarks>
    /// The returned delegate is typically passed to <c>Win2DCanvas(...)</c> from
    /// <see cref="Microsoft.UI.Reactor.Advanced.Factories"/>.
    /// If the delegate is consumed by animated callbacks, follow
    /// <see href="docs/guide/win2d-canvas.md#threading">the Win2D canvas threading guide</see>.
    /// </remarks>
    public static Action<CanvasDrawingSession, CanvasDrawEventArgs> UseDrawCommand<TState>(
        this RenderContext ctx,
        TState state,
        Action<CanvasDrawingSession, CanvasDrawEventArgs, TState> draw,
        object[] deps)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(deps);

        return ctx.UseMemo(
            () => new Action<CanvasDrawingSession, CanvasDrawEventArgs>((session, args) => draw(session, args, state)),
            deps);
    }
}
