namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// App-supplied observation hook for dock and float lifecycle events.
/// Phase 1 surface; Phase 2 collapses into Action props on
/// <see cref="DockManager"/> (spec 045 §5.3.5).
/// </summary>
/// <remarks>
/// Spec 045 §4.3. Upstream WinUI.Dock's <c>ActivateMainWindow</c> is
/// absorbed by Reactor's window topology and is not exposed here.
///
/// <para>
/// Phase 2 keeps this interface as a one-release <c>[Obsolete]</c>
/// forwarder so apps that bound their P1 behavior class to a
/// <see cref="DockManager.Behavior"/> assignment continue to compile
/// (spec 045 §5.3.5). Migrate to the per-event Action props on
/// <see cref="DockManager"/>: <see cref="DockManager.OnContentDocked"/>
/// replaces <see cref="OnDocked"/>; <see cref="DockManager.OnContentFloating"/>
/// (cancellable) and <see cref="DockManager.OnContentFloated"/> replace
/// <see cref="OnFloating"/>.
/// </para>
/// </remarks>
[global::System.Obsolete(
    "IDockBehavior is collapsing into the per-event Action props on DockManager. " +
    "OnDocked → OnContentDocked, OnFloating → OnContentFloating/OnContentFloated. " +
    "Slated for removal one release after Phase 2 ships. See spec 045 §5.3.5.",
    error: false)]
public interface IDockBehavior
{
    /// <summary>Called after a pane is docked (programmatic or drag-in).</summary>
    /// <param name="src">The pane being docked.</param>
    /// <param name="target">The relative position of the dock landing.</param>
    void OnDocked(DockableContent src, DockTarget target);

    /// <summary>Called after a pane is torn out into a floating window.</summary>
    /// <param name="content">The pane being floated.</param>
    void OnFloating(DockableContent content);
}
