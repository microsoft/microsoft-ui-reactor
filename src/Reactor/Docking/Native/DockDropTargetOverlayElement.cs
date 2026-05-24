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
    /// <summary>
    /// Spec 046 §6.6 — drop targets the overlay should render in the
    /// disabled (dimmed) style and ignore at hit-test time. Null or empty
    /// means every target is enabled (pre-046 behavior). The host computes
    /// the set per render from the active drag's payload + this overlay's
    /// group context via <see cref="DockDropFilter"/>.
    /// </summary>
    public DockTarget[]? DisabledTargets { get; init; }

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
                control.SetDisabledTargets(element.DisabledTargets);
                Wire(control, element);
                return control;
            },
            update: static (_, oldEl, newEl, control, _) =>
            {
                if (oldEl.Mode != newEl.Mode) control.Mode = newEl.Mode;
                if (!DisabledTargetsEqual(oldEl.DisabledTargets, newEl.DisabledTargets))
                    control.SetDisabledTargets(newEl.DisabledTargets);
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

    private static bool DisabledTargetsEqual(DockTarget[]? a, DockTarget[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        var lenA = a?.Length ?? 0;
        var lenB = b?.Length ?? 0;
        if (lenA != lenB) return false;
        if (lenA == 0) return true;
        // Small arrays (≤ 9 elements) — O(n²) is cheaper than a HashSet alloc.
        for (int i = 0; i < lenA; i++)
        {
            bool found = false;
            for (int j = 0; j < lenB; j++)
                if (a![i] == b![j]) { found = true; break; }
            if (!found) return false;
        }
        return true;
    }
}
