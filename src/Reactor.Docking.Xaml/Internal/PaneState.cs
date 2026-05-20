using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Docking.Internal;

/// <summary>
/// Per-pane tracking state retained on the host between reconciles. Keyed by
/// the user-supplied <see cref="DockableContent.Key"/> so a tear-out/drop-back
/// or tab reorder preserves the Reactor element subtree mounted as content.
///
/// Spec 045 §4.4: "Update path. Structural diff between previous and new
/// DockNode tree, keyed on DockableContent.Key."
/// </summary>
internal sealed class PaneState
{
    /// <summary>The upstream WinUI.Dock document instance.</summary>
    public required WinUI.Dock.Document Document { get; init; }

    /// <summary>
    /// The Reactor element currently mounted as the pane's content (the
    /// previous render's <see cref="DockableContent.Content"/>). Null when the
    /// pane has no body — only its title is rendered.
    /// </summary>
    public Element? ContentElement { get; set; }

    /// <summary>The host control whose Content slot carries the realized Reactor subtree.</summary>
    public required ContentControl ContentHost { get; init; }

    /// <summary>Most recent realized WinUI control for ContentElement.</summary>
    public UIElement? ContentControl_Realized { get; set; }
}
