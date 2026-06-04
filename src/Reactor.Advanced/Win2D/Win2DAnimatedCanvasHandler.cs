using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Windows.Foundation;

namespace Microsoft.UI.Reactor.Advanced.Win2D;

/// <summary>
/// V1 handler that mounts, updates, and unmounts <see cref="Win2DAnimatedCanvasElement"/> instances.
/// </summary>
/// <remarks>
/// Win2D raises <see cref="CanvasAnimatedControl.Update"/> and <see cref="CanvasAnimatedControl.Draw"/> on
/// its game-loop thread. Per spec §8.1, the game-thread callbacks are isolated from Reactor's UI-thread
/// element-tag/state store: they read only a volatile holder updated by <see cref="Update"/> and must not call
/// <c>Reconciler.GetElementTag</c>, <c>ReactorAttached.StateProperty</c>, <c>bind.OnCustomEvent</c>, or any
/// DependencyProperty API. <see cref="CanvasAnimatedControl.CreateResources"/> remains UI-thread per Win2D docs.
/// </remarks>
public sealed class Win2DAnimatedCanvasHandler : IElementHandler<Win2DAnimatedCanvasElement, CanvasAnimatedControl>
{
    private static readonly ConditionalWeakTable<CanvasAnimatedControl, AnimatedCanvasSubscriptions> Subscriptions = new();

    /// <summary>
    /// Mounts a <see cref="CanvasAnimatedControl"/> and wires Win2D events.
    /// </summary>
    public CanvasAnimatedControl Mount(MountContext ctx, Win2DAnimatedCanvasElement el)
    {
        var ctrl = ctx.RentControl<CanvasAnimatedControl>();
        Reconciler.SetElementTag(ctrl, el);

        if (ctrl.ClearColor != el.ClearColor)
            ctrl.ClearColor = el.ClearColor;
        if (ctrl.TargetElapsedTime != el.TargetElapsedTime)
            ctrl.TargetElapsedTime = el.TargetElapsedTime;
        // Intentionally NEVER write ctrl.Paused — under WinUI 3, toggling
        // CanvasAnimatedControl.Paused wakes the game thread for one tick
        // and then permanently parks it (see Win2DAnimatedCanvasElement.IsPaused
        // remarks). IsPaused is enforced by skipping the user's OnUpdate
        // delegate in InvokeUpdate; the native game loop stays unpaused so
        // the canvas can resume after any number of pause/resume toggles.

        var subscriptions = new AnimatedCanvasSubscriptions(el);
        TypedEventHandler<ICanvasAnimatedControl, CanvasAnimatedUpdateEventArgs> updateHandler = (_, args) =>
            InvokeUpdate(subscriptions.Element, args);
        TypedEventHandler<ICanvasAnimatedControl, CanvasAnimatedDrawEventArgs> drawHandler = (_, args) =>
            InvokeDraw(subscriptions.Element, args);
        subscriptions.UpdateHandler = updateHandler;
        subscriptions.DrawHandler = drawHandler;
        Subscriptions.Remove(ctrl);
        Subscriptions.Add(ctrl, subscriptions);

        ctrl.Update += updateHandler;
        ctrl.Draw += drawHandler;

        var bind = ctx.BindFor(ctrl, el);
        // Win2D documents CanvasAnimatedControl.CreateResources as raised on XAML's UI thread, so the
        // UI-thread Reactor custom-event trampoline is safe here; only Update/Draw are game-thread callbacks.
        bind.OnCustomEvent<CanvasCreateResourcesEventArgs>(
            subscribe: static (c, h) => ((CanvasAnimatedControl)c).CreateResources += (sender, args) => h(sender, args),
            unsubscribe: static (_, _) => { },
            handler: (cur, args) => TrackCreateResources(args, cur.OnCreateResources, ctrl));

        ctx.ApplySetters(el.Setters, ctrl);
        return ctrl;
    }

    /// <summary>
    /// Updates animated canvas state while preserving the active Win2D game loop.
    /// </summary>
    public void Update(UpdateContext ctx, Win2DAnimatedCanvasElement oldEl, Win2DAnimatedCanvasElement newEl, CanvasAnimatedControl ctrl)
    {
        if (Subscriptions.TryGetValue(ctrl, out var subscriptions))
            subscriptions.Element = newEl;

        Reconciler.SetElementTag(ctrl, newEl);

        // IsPaused is enforced inside InvokeUpdate/InvokeDraw by skipping the user's
        // delegate when the latest element says paused. We do NOT write
        // CanvasAnimatedControl.Paused: under WinUI 3, toggling that property
        // wakes the game thread for exactly one tick and then permanently parks it,
        // so a re-renderable "pause" via Paused leaves the canvas frozen. Keeping
        // the game loop ticking and gating the callbacks costs ~one empty 16 ms
        // tick of CPU per pause-second; if an author needs true game-thread
        // suspension they can opt in via `.Set(ctrl => ctrl.Paused = true)` and
        // accept that the canvas cannot resume on the same control instance.
        if (ctrl.TargetElapsedTime != newEl.TargetElapsedTime)
            ctrl.TargetElapsedTime = newEl.TargetElapsedTime;
        if (ctrl.ClearColor != newEl.ClearColor)
            ctrl.ClearColor = newEl.ClearColor;

        ctx.ApplySetters(newEl.Setters, ctrl);
    }

    /// <summary>
    /// Removes the Win2D control from the visual tree and returns it through the Reactor control path.
    /// </summary>
    public void Unmount(UnmountContext ctx, CanvasAnimatedControl ctrl)
    {
        if (Subscriptions.TryGetValue(ctrl, out var subscriptions))
        {
            if (subscriptions.UpdateHandler is { } updateHandler)
                ctrl.Update -= updateHandler;
            if (subscriptions.DrawHandler is { } drawHandler)
                ctrl.Draw -= drawHandler;
            Subscriptions.Remove(ctrl);
        }

        ctrl.RemoveFromVisualTree();
        ctx.ReturnControl(ctrl);
    }

    private sealed class AnimatedCanvasSubscriptions
    {
        private Win2DAnimatedCanvasElement _element;

        public AnimatedCanvasSubscriptions(Win2DAnimatedCanvasElement element) => _element = element;

        public Win2DAnimatedCanvasElement Element
        {
            get => Volatile.Read(ref _element);
            set => Volatile.Write(ref _element, value);
        }

        public TypedEventHandler<ICanvasAnimatedControl, CanvasAnimatedUpdateEventArgs>? UpdateHandler { get; set; }

        public TypedEventHandler<ICanvasAnimatedControl, CanvasAnimatedDrawEventArgs>? DrawHandler { get; set; }
    }

    private static void TrackCreateResources(
        CanvasCreateResourcesEventArgs args,
        Func<CanvasAnimatedControl, Task>? create,
        CanvasAnimatedControl ctrl)
    {
        if (create is null) return;
        args.TrackAsyncAction(global::System.WindowsRuntimeSystemExtensions.AsAsyncAction(create(ctrl)));
    }

    private static void InvokeUpdate(Win2DAnimatedCanvasElement el, CanvasAnimatedUpdateEventArgs args)
    {
        if (el.IsPaused) return;
#if DEBUG
        try
        {
            el.OnUpdate?.Invoke(args, el.DrawState);
        }
        catch (InvalidOperationException ex) when (IsLikelyWinUIThreadAffinity(ex))
        {
            throw new InvalidOperationException(
                $"{ex.Message} Win2DAnimatedCanvas callbacks run on the Win2D game thread; see docs/guide/win2d-canvas.md#threading.",
                ex);
        }
#else
        el.OnUpdate?.Invoke(args, el.DrawState);
#endif
    }

    private static void InvokeDraw(Win2DAnimatedCanvasElement el, CanvasAnimatedDrawEventArgs args)
    {
        // No IsPaused gate here: pausing the simulation should not blank the display.
        // OnDraw is a passive read of whatever DrawState already holds — invoking it
        // every tick while paused keeps the last frame visible (and animations driven
        // purely by Draw-time clocks can continue) without advancing physics.
#if DEBUG
        try
        {
            el.OnDraw?.Invoke(args.DrawingSession, args, el.DrawState);
        }
        catch (InvalidOperationException ex) when (IsLikelyWinUIThreadAffinity(ex))
        {
            throw new InvalidOperationException(
                $"{ex.Message} Win2DAnimatedCanvas callbacks run on the Win2D game thread; see docs/guide/win2d-canvas.md#threading.",
                ex);
        }
#else
        el.OnDraw?.Invoke(args.DrawingSession, args, el.DrawState);
#endif
    }

#if DEBUG
    private static bool IsLikelyWinUIThreadAffinity(InvalidOperationException ex)
    {
        var message = ex.Message;
        return message.Contains("marshalled for a different thread", StringComparison.OrdinalIgnoreCase)
            || message.Contains("RPC_E_WRONG_THREAD", StringComparison.OrdinalIgnoreCase)
            || (message.Contains("thread", StringComparison.OrdinalIgnoreCase)
                && message.Contains("affinity", StringComparison.OrdinalIgnoreCase));
    }
#endif
}
