namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Spec 047 §9.2 / §14 Phase 1 (1.7) — discriminator + payload pair for the
/// per-control event state field on <see cref="Reconciler.ReactorState"/>.
///
/// The handler reads the payload only after asserting
/// <c>HandlerType == typeof(MyPayload)</c>. Hot-reload safety: if a handler
/// is replaced while a control is mounted (Phase 4+ scenario), the type
/// discriminator detects a mismatched payload and the new handler can
/// initialize a fresh box without trusting stale state.
///
/// The pool reset contract in <see cref="Reconciler.ReturnControl{T}"/>
/// PRESERVES <c>ReactorState.ControlEventState</c> across pool rent/return
/// (issue #114): the trampolines stay subscribed to the native WinUI events
/// for the control's lifetime and read live state via GetElementTag, so
/// clearing the box on return would force re-allocation on every rent and
/// double-subscribe. The box is only dropped on full detach
/// (DetachReactorState). A stale type is never observable post-rent because
/// GetOrCreateControlEventPayload re-creates the box on a HandlerType mismatch.
/// </summary>
internal sealed class ControlEventStateBox
{
    public Type HandlerType = null!;
    public object Payload = null!;
}
