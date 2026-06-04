using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;

namespace Microsoft.UI.Reactor.Advanced.Win2D;

/// <summary>
/// V1 handler that mounts, updates, and unmounts <see cref="Win2DCanvasElement"/> instances.
/// </summary>
public sealed class Win2DCanvasHandler : IElementHandler<Win2DCanvasElement, CanvasControl>
{
    /// <summary>
    /// Mounts a <see cref="CanvasControl"/> and wires Win2D events.
    /// </summary>
    public CanvasControl Mount(MountContext ctx, Win2DCanvasElement el)
    {
        var ctrl = ctx.RentControl<CanvasControl>();
        Reconciler.SetElementTag(ctrl, el);

        if (ctrl.ClearColor != el.ClearColor)
            ctrl.ClearColor = el.ClearColor;

        var bind = ctx.BindFor(ctrl, el);
        // Win2D documents CanvasControl.Draw as a XAML visibility/invalidation callback; unlike
        // CanvasAnimatedControl.Update/Draw, it is not a game-loop-thread event, so bind.OnCustomEvent is safe.
        bind.OnCustomEvent<CanvasDrawEventArgs>(
            subscribe: static (c, h) => ((CanvasControl)c).Draw += (sender, args) => h(sender, args),
            unsubscribe: static (_, _) => { },
            handler: static (cur, args) => cur.OnDraw?.Invoke(args.DrawingSession, args));
        bind.OnCustomEvent<CanvasCreateResourcesEventArgs>(
            subscribe: static (c, h) => ((CanvasControl)c).CreateResources += (sender, args) => h(sender, args),
            unsubscribe: static (_, _) => { },
            handler: (cur, args) => TrackCreateResources(args, cur.OnCreateResources, ctrl));

        ctx.ApplySetters(el.Setters, ctrl);
        return ctrl;
    }

    /// <summary>
    /// Updates manual canvas properties and invalidates when the redraw key changes.
    /// </summary>
    public void Update(UpdateContext ctx, Win2DCanvasElement oldEl, Win2DCanvasElement newEl, CanvasControl ctrl)
    {
        Reconciler.SetElementTag(ctrl, newEl);
        var bind = ctx.BindFor(ctrl, newEl);

        if (ctrl.ClearColor != newEl.ClearColor)
            bind.WriteSuppressed(() => ctrl.ClearColor = newEl.ClearColor);

        // Value-equality on RedrawKey — boxed primitives (int, enum, etc.) compare by reference
        // when boxed across renders, so ReferenceEquals would invalidate on every parent re-render
        // even when the key value is unchanged. The intent of RedrawKey is "redraw when this value
        // changes"; Equals captures that. For identity-only keys, authors pass a fixed sentinel
        // instance and rely on reference equality producing Equals == true.
        if (!Equals(oldEl.RedrawKey, newEl.RedrawKey))
            ctrl.Invalidate();

        ctx.ApplySetters(newEl.Setters, ctrl);
    }

    /// <summary>
    /// Removes the Win2D control from the visual tree and returns it through the Reactor control path.
    /// </summary>
    public void Unmount(UnmountContext ctx, CanvasControl ctrl)
    {
        ctrl.RemoveFromVisualTree();
        ctx.ReturnControl(ctrl);
    }

    private static void TrackCreateResources(
        CanvasCreateResourcesEventArgs args,
        Func<CanvasControl, Task>? create,
        CanvasControl ctrl)
    {
        if (create is null) return;
        args.TrackAsyncAction(global::System.WindowsRuntimeSystemExtensions.AsAsyncAction(create(ctrl)));
    }
}
