using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// Context for an <see cref="IDockAdapter.OnGroupCreated"/> callback.
/// Carries the dragged-out source pane (if the group was created by a
/// tear-out, otherwise null).
/// </summary>
/// <remarks>
/// Spec 045 §4.3. Phase 1 is intentionally minimal — Phase 2's
/// <c>DockHostModel</c> introduces a richer mutation handle (§5.3.10).
/// </remarks>
public sealed record DockTabGroupContext(DockableContent? DraggedSource);

/// <summary>
/// App-supplied adapter for two paths the wrapper can't infer from the
/// declarative <see cref="DockManager.Layout"/> alone: content rehydration
/// after layout-JSON restore, and custom floating-window title bar chrome.
/// </summary>
/// <remarks>
/// Spec 045 §4.3. In Phase 2 the adapter surface collapses into per-event
/// Action props on <c>DockManager</c> (§5.3.5); the interface stays as a
/// <c>[Obsolete]</c> forwarder for one release.
/// </remarks>
public interface IDockAdapter
{
    /// <summary>
    /// Called when the wrapper instantiates a pane from persisted layout
    /// JSON. Apps return the Reactor <see cref="Element"/> subtree to mount
    /// as the pane's content. Return null to leave content empty (the pane
    /// will render its <see cref="DockableContent.Title"/> but no body).
    /// </summary>
    /// <param name="content">The reconstituted pane, including its
    /// <see cref="DockableContent.Key"/> and <see cref="DockableContent.PersistenceState"/>.
    /// Match by <c>Key</c>.</param>
    Element? OnContentCreated(DockableContent content);

    /// <summary>
    /// Called when the manager creates a new <c>DocumentGroup</c> at the
    /// tail end of a tear-out drag. Apps may use this to wire group-level
    /// chrome (e.g., a custom tab-strip toolbar). Read the dragged source
    /// (when applicable) from <see cref="DockTabGroupContext.DraggedSource"/>.
    /// </summary>
    /// <param name="group">The freshly-created group's context.</param>
    void OnGroupCreated(DockTabGroupContext group);

    /// <summary>
    /// Returns an optional Reactor element to render as the title bar of a
    /// freshly-created floating window. Return null to use the default.
    /// </summary>
    /// <param name="draggedSource">The pane whose tear-out spawned the
    /// floating window, or null if the window was restored from layout JSON.</param>
    Element? GetFloatingWindowTitleBar(DockableContent? draggedSource);
}
