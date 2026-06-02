using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;

namespace Microsoft.UI.Reactor.Advanced.Win2D;

/// <summary>
/// V1 handler that mounts, updates, and unmounts <see cref="Win2DVirtualCanvasElement"/> instances.
/// </summary>
public sealed class Win2DVirtualCanvasHandler : IElementHandler<Win2DVirtualCanvasElement, CanvasVirtualControl>
{
    /// <summary>
    /// Mounts a <see cref="CanvasVirtualControl"/> and wires region invalidation events.
    /// </summary>
    public CanvasVirtualControl Mount(MountContext ctx, Win2DVirtualCanvasElement el)
    {
        var ctrl = ctx.RentControl<CanvasVirtualControl>();
        Reconciler.SetElementTag(ctrl, el);
        ApplyContentSize(ctrl, el);

        var bind = ctx.BindFor(ctrl, el);
        // Win2D documents CanvasVirtualControl as XAML-driven virtualized drawing; RegionsInvalidated
        // and CreateResources are not CanvasAnimatedControl game-loop-thread callbacks, so bind.OnCustomEvent is safe.
        bind.OnCustomEvent<CanvasRegionsInvalidatedEventArgs>(
            subscribe: static (c, h) => ((CanvasVirtualControl)c).RegionsInvalidated += (sender, args) => h(sender, args),
            unsubscribe: static (_, _) => { },
            handler: (cur, args) => DrawInvalidatedRegions(ctrl, cur, args));
        bind.OnCustomEvent<CanvasCreateResourcesEventArgs>(
            subscribe: static (c, h) => ((CanvasVirtualControl)c).CreateResources += (sender, args) => h(sender, args),
            unsubscribe: static (_, _) => { },
            handler: (cur, args) => TrackCreateResources(args, cur.OnCreateResources, ctrl));

        ctx.ApplySetters(el.Setters, ctrl);
        return ctrl;
    }

    /// <summary>
    /// Updates virtual content size and invalidates newly requested regions.
    /// </summary>
    public void Update(UpdateContext ctx, Win2DVirtualCanvasElement oldEl, Win2DVirtualCanvasElement newEl, CanvasVirtualControl ctrl)
    {
        Reconciler.SetElementTag(ctrl, newEl);

        if (!SizesEqual(ctrl.Width, ctrl.Height, newEl.ContentSize.Width, newEl.ContentSize.Height))
            ApplyContentSize(ctrl, newEl);

        if (!ReferenceEquals(oldEl.InvalidateRegions, newEl.InvalidateRegions) && newEl.InvalidateRegions is { } regions)
        {
            foreach (var rect in regions)
                ctrl.Invalidate(rect);
        }

        ctx.ApplySetters(newEl.Setters, ctrl);
    }

    /// <summary>
    /// Removes the Win2D control from the visual tree and returns it through the Reactor control path.
    /// </summary>
    public void Unmount(UnmountContext ctx, CanvasVirtualControl ctrl)
    {
        ctrl.RemoveFromVisualTree();
        ctx.ReturnControl(ctrl);
    }

    private static void ApplyContentSize(CanvasVirtualControl ctrl, Win2DVirtualCanvasElement el)
    {
        ctrl.Width = el.ContentSize.Width;
        ctrl.Height = el.ContentSize.Height;
    }

    // Width/Height are doubles; avoid `!=` to satisfy CA1018-style float-equality analysis
    // and skip pointless DP writes for sub-pixel float-precision noise from layout/devicescale math.
    private const double SizeEpsilon = 0.5; // half a DIP is well below display rounding
    private static bool SizesEqual(double aw, double ah, double bw, double bh) =>
        global::System.Math.Abs(aw - bw) <= SizeEpsilon
        && global::System.Math.Abs(ah - bh) <= SizeEpsilon;

    private static void DrawInvalidatedRegions(
        CanvasVirtualControl ctrl,
        Win2DVirtualCanvasElement el,
        CanvasRegionsInvalidatedEventArgs args)
    {
        if (el.OnRegionDraw is not { } draw) return;

        foreach (var region in args.InvalidatedRegions)
        {
            using var session = ctrl.CreateDrawingSession(region);
            draw(session, region);
        }
    }

    private static void TrackCreateResources(
        CanvasCreateResourcesEventArgs args,
        Func<CanvasVirtualControl, Task>? create,
        CanvasVirtualControl ctrl)
    {
        if (create is null) return;
        args.TrackAsyncAction(global::System.WindowsRuntimeSystemExtensions.AsAsyncAction(create(ctrl)));
    }
}
