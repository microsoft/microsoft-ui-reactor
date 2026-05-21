using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.1 — Reactor element for the docking splitter handle.
//
//  This is an internal element type: the public API surface for §2.1 is
//  the renderer's behavior, not a user-facing element. The element is
//  exposed only so the renderer's composition stays inspectable in unit
//  tests (the reconciler reaches the element via a registered handler).
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Reactor element rendering a <see cref="DockSplitterControl"/>. Internal
/// to the docking subsystem.
/// </summary>
internal sealed record DockSplitterElement(
    DockSplitterDirection Direction,
    Action<double, double, bool> OnDelta,
    double KeyboardStep = DockSplitterControl.DefaultKeyboardStepDip)
    : Element
{
    /// <summary>
    /// Optional diagnostic sink fired with a single string per
    /// splitter pointer event (pressed / moved / released). The host
    /// hooks this to <see cref="Diagnostics.DockOperationLog"/> so the
    /// drag's intermediate math lands in the visible log + Debug.WriteLine.
    /// </summary>
    public Action<string>? DiagnosticSink { get; init; }

    internal override bool HasCallbacks => true;
}

/// <summary>
/// Reconciler registration for <see cref="DockSplitterElement"/>. Called by
/// the dock-host mount path; safe to call multiple times — the underlying
/// registry takes the most recent registration.
/// </summary>
internal static class DockSplitterReconcilerRegistration
{
    public static void Register(Reconciler reconciler)
    {
        ArgumentNullException.ThrowIfNull(reconciler);

        reconciler.RegisterType<DockSplitterElement, DockSplitterControl>(
            mount: static (_, element, _) =>
            {
                var control = new DockSplitterControl
                {
                    Direction = element.Direction,
                    KeyboardStep = element.KeyboardStep,
                    DiagnosticSink = element.DiagnosticSink,
                };
                Wire(control, element);
                return control;
            },
            update: static (_, oldEl, newEl, control, _) =>
            {
                if (oldEl.Direction != newEl.Direction)
                    control.Direction = newEl.Direction;
                if (Math.Abs(oldEl.KeyboardStep - newEl.KeyboardStep) > double.Epsilon)
                    control.KeyboardStep = newEl.KeyboardStep;
                // Always re-bind the sink so the closure captures the
                // latest host render state.
                control.DiagnosticSink = newEl.DiagnosticSink;
                if (!ReferenceEquals(oldEl.OnDelta, newEl.OnDelta))
                {
                    Unwire(control);
                    Wire(control, newEl);
                }
                return null;
            },
            unmount: static (_, control) => Unwire(control));
    }

    private static void Wire(DockSplitterControl control, DockSplitterElement element)
    {
        EventHandler<DockSplitterDeltaEventArgs> handler = (_, args) =>
            element.OnDelta(args.Delta, args.HostExtentDip, args.IsFinal);
        _handlers.AddOrUpdate(control, handler);
        control.ResizeDelta += handler;
    }

    private static void Unwire(DockSplitterControl control)
    {
        if (_handlers.TryGetValue(control, out var existing))
        {
            control.ResizeDelta -= existing;
            _handlers.Remove(control);
        }
    }

    // Per-control delegate storage. A DP value can't carry a managed generic
    // delegate without a CCW, and WinRT marshalling rejects
    // EventHandler<DockSplitterDeltaEventArgs> at SetValue time (the runtime
    // can't synthesize an IID for the closed generic delegate). Storing the
    // handler in a CWT keyed by the control sidesteps the COM trip entirely.
    private static readonly ConditionalWeakTable<DockSplitterControl,
        EventHandler<DockSplitterDeltaEventArgs>> _handlers = new();
}
