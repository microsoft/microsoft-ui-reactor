namespace Microsoft.UI.Reactor.Docking;

// Phase 2 cancellable lifecycle events (spec 045 §5.3.5, tracking §2.12).
//
// Each *ing variant carries a Cancel flag; setting it to true aborts the
// transition and leaves state unchanged. *ed variants are observation only.

/// <summary>Base type for cancellable docking lifecycle event payloads.</summary>
/// <remarks>Spec 045 §5.3.5.</remarks>
public abstract class DockCancelEventArgs
{
    /// <summary>Setting to true aborts the in-flight transition.</summary>
    public bool Cancel { get; set; }
}

/// <summary>Args for <see cref="DockManager.OnLayoutChanging"/>.</summary>
public sealed class DockLayoutChangingEventArgs : DockCancelEventArgs { }

/// <summary>Args for <see cref="DockManager.OnLayoutChanged"/>.</summary>
public sealed class DockLayoutChangedEventArgs { }

/// <summary>Args for <see cref="DockManager.OnDocumentClosing"/>.</summary>
public sealed class DockDocumentClosingEventArgs : DockCancelEventArgs
{
    /// <summary>The document about to be closed.</summary>
    public required DockableContent Document { get; init; }
}

/// <summary>Args for <see cref="DockManager.OnDocumentClosed"/>.</summary>
public sealed class DockDocumentClosedEventArgs
{
    /// <summary>The document that closed.</summary>
    public required DockableContent Document { get; init; }
}

/// <summary>Args for <see cref="DockManager.OnToolWindowHiding"/>.</summary>
public sealed class DockToolWindowHidingEventArgs : DockCancelEventArgs
{
    /// <summary>The tool window about to auto-hide.</summary>
    public required DockableContent ToolWindow { get; init; }
}

/// <summary>Args for <see cref="DockManager.OnToolWindowHidden"/>.</summary>
public sealed class DockToolWindowHiddenEventArgs
{
    /// <summary>The tool window that auto-hid.</summary>
    public required DockableContent ToolWindow { get; init; }
}

/// <summary>Args for <see cref="DockManager.OnToolWindowClosing"/>.</summary>
public sealed class DockToolWindowClosingEventArgs : DockCancelEventArgs
{
    /// <summary>The tool window about to be closed.</summary>
    public required DockableContent ToolWindow { get; init; }
}

/// <summary>Args for <see cref="DockManager.OnToolWindowClosed"/>.</summary>
public sealed class DockToolWindowClosedEventArgs
{
    /// <summary>The tool window that closed.</summary>
    public required DockableContent ToolWindow { get; init; }
}

/// <summary>Args for <see cref="DockManager.OnContentFloating"/>.</summary>
public sealed class DockContentFloatingEventArgs : DockCancelEventArgs
{
    /// <summary>The pane being torn out.</summary>
    public required DockableContent Content { get; init; }
}

/// <summary>Args for <see cref="DockManager.OnContentFloated"/>.</summary>
public sealed class DockContentFloatedEventArgs
{
    /// <summary>The pane that floated.</summary>
    public required DockableContent Content { get; init; }
}

/// <summary>Args for <see cref="DockManager.OnContentDocking"/>.</summary>
public sealed class DockContentDockingEventArgs : DockCancelEventArgs
{
    /// <summary>The pane being docked.</summary>
    public required DockableContent Content { get; init; }

    /// <summary>The dock target receiving the pane.</summary>
    public required DockTarget Target { get; init; }
}

/// <summary>Args for <see cref="DockManager.OnContentDocked"/>.</summary>
public sealed class DockContentDockedEventArgs
{
    /// <summary>The pane that docked.</summary>
    public required DockableContent Content { get; init; }

    /// <summary>The dock target it landed at.</summary>
    public required DockTarget Target { get; init; }
}

/// <summary>Args for <see cref="DockManager.OnActiveContentChanged"/>.</summary>
public sealed class DockActiveContentChangedEventArgs
{
    /// <summary>The newly-active pane (null if no pane is active).</summary>
    public DockableContent? ActiveContent { get; init; }

    /// <summary>The previously-active pane (null if none was active).</summary>
    public DockableContent? PreviousContent { get; init; }
}

/// <summary>Args for <see cref="DockManager.OnFloatingWindowCreated"/>.</summary>
public sealed class DockFloatingWindowCreatedEventArgs
{
    /// <summary>The pane that spawned the floating window (null when restored from JSON).</summary>
    public DockableContent? DraggedSource { get; init; }
}

/// <summary>
/// Why a docking floating window closed. Lets <see cref="DockManager.OnFloatingWindowClosed"/>
/// handlers tell a genuine user close (release per-document resources)
/// apart from a synthetic close that follows a tab migrating to another
/// surface (the content is still alive — do NOT release anything).
/// </summary>
/// <remarks>Spec 045 §5.3.5.</remarks>
public enum DockFloatingCloseReason
{
    /// <summary>
    /// The window closed and its content is gone — the OS close button,
    /// Alt+F4, an app-driven <c>Close()</c>, the last tab being closed, or
    /// the owning host unmounting. It is safe to release per-document state
    /// tied to the content. This is the zero value, so a handler that ignores
    /// <see cref="DockFloatingWindowClosedEventArgs.Reason"/> sees the
    /// pre-#417 behaviour. Named for the outcome (content gone), not the
    /// initiator — it deliberately covers app-driven and host-unmount closes
    /// too, and so is distinct from <c>WindowCloseReason.UserClosed</c>.
    /// </summary>
    ContentClosed = 0,

    /// <summary>
    /// The window closed synthetically because its last pane was
    /// dock-backed into another host (cross-window dock-back). The content
    /// is alive and visible in its new dock position — handlers must NOT
    /// release resources tied to it.
    /// </summary>
    MigratedToHost,

    /// <summary>
    /// The window closed synthetically because its last pane was re-torn-out
    /// or dropped into a different floating window. The content is alive in
    /// the destination float — handlers must NOT release resources tied to it.
    /// </summary>
    MigratedToFloat,
}

/// <summary>Args for <see cref="DockManager.OnFloatingWindowClosed"/>.</summary>
public sealed class DockFloatingWindowClosedEventArgs
{
    /// <summary>The pane that was inside the floating window when it closed (best-effort).</summary>
    public DockableContent? Content { get; init; }

    /// <summary>
    /// Why the window closed. <see cref="DockFloatingCloseReason.ContentClosed"/>
    /// uniquely identifies a true close; the <c>Migrated*</c> values mark the
    /// synthetic close that pairs a cross-window dock-back / re-tear-out, where
    /// <see cref="Content"/> is still alive elsewhere and must not be released.
    /// </summary>
    /// <remarks>Spec 045 §5.3.5. See issue #417.</remarks>
    public required DockFloatingCloseReason Reason { get; init; }
}
