namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// Insertion-policy hook applied when a pane is added to the layout
/// (programmatic insert, drag-drop landing, JSON restore). Apps register
/// a strategy on <see cref="DockManager.LayoutStrategy"/>.
/// </summary>
/// <remarks>
/// Spec 045 §5.3.6 (Phase 2 addition).
///
/// <para>
/// <see cref="BeforeInsertDocument"/> / <see cref="BeforeInsertToolWindow"/>
/// returning <c>true</c> short-circuits the default insertion (the strategy
/// performed the placement itself via the model handle); returning
/// <c>false</c> lets the manager proceed with its default routing.
/// <see cref="AfterInsertDocument"/> / <see cref="AfterInsertToolWindow"/>
/// run after the placement and are the chance to set dimensions, pin to a
/// side, or activate the pane.
/// </para>
///
/// <para>
/// Strategies receive a <see cref="DockHostModel"/> mutable handle — not
/// the immutable <see cref="DockNode"/> tree. This is the same model
/// instance the reconciler reads from (spec 045 §5.3.10).
/// </para>
///
/// <para>
/// Example: route any tool window whose title starts with "Error" to the
/// bottom side, height 180:
/// <code>
/// class ErrorPaneStrategy : IDockLayoutStrategy {
///     public bool BeforeInsertToolWindow(DockHostModel model, ToolWindow tw) {
///         if (tw.Title.StartsWith("Error", StringComparison.Ordinal)) {
///             model.PinToSide(tw, DockSide.Bottom);
///             return true; // we placed it; skip default insertion
///         }
///         return false;
///     }
///     // … other members no-op …
/// }
/// </code>
/// </para>
/// </remarks>
public interface IDockLayoutStrategy
{
    /// <summary>
    /// Pre-insert hook for documents. Return <c>true</c> if the strategy
    /// performed placement; <c>false</c> to let the manager proceed with
    /// default insertion.
    /// </summary>
    bool BeforeInsertDocument(DockHostModel model, Document document) => false;

    /// <summary>Post-insert hook for documents — runs after default routing.</summary>
    void AfterInsertDocument(DockHostModel model, Document document) { }

    /// <summary>
    /// Pre-insert hook for tool windows. Return <c>true</c> if the strategy
    /// performed placement; <c>false</c> to let the manager proceed with
    /// default insertion.
    /// </summary>
    bool BeforeInsertToolWindow(DockHostModel model, ToolWindow toolWindow) => false;

    /// <summary>Post-insert hook for tool windows — runs after default routing.</summary>
    void AfterInsertToolWindow(DockHostModel model, ToolWindow toolWindow) { }
}
