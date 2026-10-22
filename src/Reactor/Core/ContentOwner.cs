namespace Microsoft.UI.Reactor.Core;

/// <summary>Result of resolving the window that owns a host's content (see <see cref="IContentOwner"/>).</summary>
internal enum ContentOwnerStatus
{
    /// <summary>The owning window id was resolved.</summary>
    Available,
    /// <summary>The owning window closed, or the host was disposed.</summary>
    Closed,
    /// <summary>The host is alive but not currently in a window (an unloaded <c>ReactorHostControl</c>).</summary>
    Detached,
    /// <summary>The caller is not on the owner's UI thread.</summary>
    WrongThread,
}

/// <summary>
/// The host that renders a reconciler's content, as seen by that content. Carried by
/// <see cref="Reconciler.Owner"/> and recorded on each <see cref="RenderContext"/> at render,
/// so call-time APIs (the picker methods) resolve the window that renders the component
/// rather than the process-wide active host.
/// </summary>
internal interface IContentOwner
{
    /// <summary>
    /// The <see cref="Microsoft.UI.Reactor.ReactorWindow"/> that owns the host, or null for a
    /// <c>ReactorHostControl</c> or a <c>ReactorHost</c> constructed over a raw window.
    /// </summary>
    ReactorWindow? ReactorWindow { get; }

    /// <summary>
    /// Resolves the id of the top-level window that currently hosts the content. Safe to call
    /// from any thread: the closed and thread checks run before any XAML access.
    /// </summary>
    ContentOwnerStatus TryGetWindowId(out WindowId id);
}
