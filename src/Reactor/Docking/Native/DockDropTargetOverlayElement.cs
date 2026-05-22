using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.3 — Reactor element for the drop-target overlay.
//
//  Internal element. The public surface is DockManager.ShowDropTargets
//  (§2.3) and the §2.4 drag pipeline (which flips the prop during a tab
//  drag). Direct construction of this element is reserved for the docking
//  subsystem so the layout can be composed without an extra public type.
// ════════════════════════════════════════════════════════════════════════

internal sealed record DockDropTargetOverlayElement(
    Action<DockTarget?>? OnHover,
    Action<DockTarget>? OnConfirm,
    Action? OnDismiss,
    DockDropOverlayMode Mode = DockDropOverlayMode.Host)
    : Element
{
    internal override bool HasCallbacks => true;
}

internal static class DockDropTargetReconcilerRegistration
{
    public static void Register(Reconciler reconciler)
    {
        ArgumentNullException.ThrowIfNull(reconciler);

        reconciler.RegisterType<DockDropTargetOverlayElement, DockDropTargetOverlayControl>(
            mount: static (_, element, _) =>
            {
                var control = new DockDropTargetOverlayControl { Mode = element.Mode };
                Wire(control, element);
                return control;
            },
            update: static (_, oldEl, newEl, control, _) =>
            {
                if (oldEl.Mode != newEl.Mode) control.Mode = newEl.Mode;
                if (!ReferenceEquals(oldEl.OnHover, newEl.OnHover)
                    || !ReferenceEquals(oldEl.OnConfirm, newEl.OnConfirm)
                    || !ReferenceEquals(oldEl.OnDismiss, newEl.OnDismiss))
                {
                    Unwire(control);
                    Wire(control, newEl);
                }
                return null;
            },
            unmount: static (_, control) =>
            {
                Unwire(control);
                // Reactor unmount is the reliable lifecycle boundary —
                // the WinUI Unloaded path can miss the root-level Esc
                // handler when the visual tree is replaced mid-drag.
                control.DetachGlobalHandlers();
            });
    }

    private static void Wire(DockDropTargetOverlayControl control, DockDropTargetOverlayElement element)
    {
        EventHandler<DockDropTargetEventArgs> hover = (_, args) =>
            element.OnHover?.Invoke(args.Target);
        EventHandler<DockDropTargetEventArgs> confirm = (_, args) =>
        {
            if (args.Target is DockTarget t) element.OnConfirm?.Invoke(t);
        };
        EventHandler dismiss = (_, _) => element.OnDismiss?.Invoke();

        var bag = new HandlerBag(hover, confirm, dismiss);
        _handlers.AddOrUpdate(control, bag);
        control.TargetHovered += hover;
        control.TargetConfirmed += confirm;
        control.OverlayDismissed += dismiss;
    }

    private static void Unwire(DockDropTargetOverlayControl control)
    {
        if (_handlers.TryGetValue(control, out var bag))
        {
            control.TargetHovered -= bag.Hover;
            control.TargetConfirmed -= bag.Confirm;
            control.OverlayDismissed -= bag.Dismiss;
            _handlers.Remove(control);
        }
    }

    private sealed record HandlerBag(
        EventHandler<DockDropTargetEventArgs> Hover,
        EventHandler<DockDropTargetEventArgs> Confirm,
        EventHandler Dismiss);

    // Per-control delegate storage — same pattern as DockSplitterElement,
    // dodges the WinRT generic-delegate CCW round-trip.
    private static readonly ConditionalWeakTable<DockDropTargetOverlayControl, HandlerBag> _handlers = new();
}
