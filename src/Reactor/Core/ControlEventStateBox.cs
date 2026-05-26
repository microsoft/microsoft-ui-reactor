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
/// clears <c>ReactorState.ControlEventState</c> to null on return, so a
/// stale type is never observable post-rent.
/// </summary>
internal sealed class ControlEventStateBox
{
    public Type HandlerType = null!;
    public object Payload = null!;
}
