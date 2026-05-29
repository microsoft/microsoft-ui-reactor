using System.Diagnostics.CodeAnalysis;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Spec 047 §13 Q18 / §14 Phase 1 (1.5) — author-facing pool policy.
///
/// External and built-in V1 handlers attach a <c>PoolPolicy&lt;TControl&gt;</c>
/// to their <c>RentControl</c> / <c>ReturnControl</c> calls to opt out of
/// pooling (e.g. for controls with persistent native resources like
/// MediaPlayerElement, MapControl, SwapChainPanel) or to layer an
/// author-defined reset action on top of the engine's default contract.
///
/// Pool key is <c>typeof(TControl)</c> only (Q18). Finer keys (e.g. by
/// style hash) are a Phase 3+ addition.
///
/// Provisional surface — see <see cref="ReactorBinding"/> for the
/// <c>REACTOR_V1_PREVIEW</c> diagnostic id.
/// </summary>
public sealed class PoolPolicy<TControl> where TControl : class
{
    /// <summary>
    /// When false, <c>RentControl</c> always allocates a fresh instance
    /// and <c>ReturnControl</c> is a no-op (the control is dropped on the
    /// floor for the GC to collect). Default true.
    /// </summary>
    public bool IsPoolable { get; init; } = true;

    /// <summary>
    /// Optional extra reset action invoked AFTER the engine's default
    /// contract clears (ControlEventState, ModifierEventHandlerState,
    /// ReactorAttached.StateProperty Tag, Reactor-set DataContext).
    /// Use for control-specific transient state the engine can't know
    /// about (e.g. ScrollViewer.ChangeView animation queue).
    /// </summary>
    public Action<TControl>? Reset { get; init; }
}
