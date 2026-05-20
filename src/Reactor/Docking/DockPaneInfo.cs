using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// Snapshot of a pane's identity surfaced to pane-content components via
/// the <c>UsePane()</c> hook. Apps read this from the pane's own subtree
/// to address themselves (e.g. for context-menu actions).
/// </summary>
/// <remarks>Spec 045 §5.3.11.</remarks>
public readonly record struct DockPaneInfo(object? Key, string Title, DockableContent Content);
