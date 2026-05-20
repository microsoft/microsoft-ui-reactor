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

/// <summary>Args for <see cref="DockManager.OnFloatingWindowClosed"/>.</summary>
public sealed class DockFloatingWindowClosedEventArgs
{
    /// <summary>The pane that was inside the floating window when it closed (best-effort).</summary>
    public DockableContent? Content { get; init; }
}
