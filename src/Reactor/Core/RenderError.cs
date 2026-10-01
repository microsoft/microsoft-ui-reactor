using System;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Replaces Reactor's built-in render-error fallback. Called on the UI thread with
/// the exception Reactor caught.
/// </summary>
/// <param name="error">What failed, where, and a way to request propagation.</param>
/// <returns>
/// An element to show instead of the built-in fallback, or <c>null</c> to keep the
/// built-in fallback (which shows the full exception text). Ignored when
/// <see cref="RenderError.Source"/> is <see cref="RenderErrorSource.Cleanup"/> or when
/// <see cref="RenderError.Propagate"/> was called.
/// </returns>
/// <remarks>
/// <para>Set it app-wide with <see cref="ReactorApp.DefaultRenderErrorHandler"/>, per window
/// with <see cref="WindowSpec.RenderErrorHandler"/>, or per host with
/// <see cref="Hosting.ReactorHost.RenderErrorHandler"/> /
/// <see cref="Hosting.ReactorHostControl.RenderErrorHandler"/>. A host's own handler
/// wins; otherwise the app-wide default is used. Exceptions caught by an
/// <c>ErrorBoundary</c> are handled by that boundary and never reach this handler.</para>
/// <para>Failure is closed, not open: if the handler throws, or the element it returns
/// throws while rendering, Reactor shows a neutral "Something went wrong." message
/// without exception details, and does not call the handler again for that failure.</para>
/// </remarks>
public delegate Element? RenderErrorHandler(RenderError error);

/// <summary>Where a render error handled by a <see cref="RenderErrorHandler"/> came from.</summary>
public enum RenderErrorSource
{
    /// <summary>The host's root component <c>Render()</c> or root render function threw.</summary>
    RootRender,

    /// <summary>A child component's <c>Render()</c> threw (in-tree placeholder).</summary>
    ComponentRender,

    /// <summary>
    /// The commit phase threw: reconciling the element tree into native controls
    /// (mount/update/unmount), installing the result in the host, or a host callback run
    /// after the render pass such as <c>OnRenderComplete</c>.
    /// </summary>
    Reconcile,

    /// <summary>An effect body, or an effect cleanup run while flushing effects, threw.</summary>
    Effects,

    /// <summary>
    /// An effect cleanup threw while the host was being disposed (window closed, host
    /// control disposed). Nothing can be displayed at that point, so the handler's return
    /// value is ignored; it is a report-only notification. Remaining cleanups still run.
    /// </summary>
    Cleanup,
}

/// <summary>
/// A render-time exception Reactor caught, handed to a <see cref="RenderErrorHandler"/>.
/// </summary>
public sealed class RenderError
{
    internal RenderError(Exception exception, RenderErrorSource source, string? componentName, bool isHostLevel)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        Source = source;
        ComponentName = componentName;
        IsHostLevel = isHostLevel;
    }

    /// <summary>The exception Reactor caught.</summary>
    public Exception Exception { get; }

    /// <summary>Which part of the render pass threw.</summary>
    public RenderErrorSource Source { get; }

    /// <summary>The failing component's type name, when known.</summary>
    public string? ComponentName { get; }

    /// <summary>
    /// Where the failure sits. For a displayable error: <c>true</c> when the returned
    /// element replaces the host's whole content (root render, reconcile, root effects);
    /// <c>false</c> when it replaces only the failing component's slot in the tree. For
    /// <see cref="RenderErrorSource.Cleanup"/>, where no element is shown: <c>true</c> for
    /// a cleanup registered by the host's root component, <c>false</c> for one registered
    /// by a child component.
    /// </summary>
    public bool IsHostLevel { get; }

    /// <summary>Whether <see cref="Propagate"/> has been called.</summary>
    public bool IsPropagationRequested { get; private set; }

    // A handler that throws after calling Propagate() did not complete its decision;
    // Reactor then uses its safe fallback instead of propagating.
    internal void CancelPropagation() => IsPropagationRequested = false;

    /// <summary>
    /// Show no fallback and route the exception to the app's unhandled-exception path
    /// instead: <see cref="ReactorApp.AppLogger"/> and
    /// <see cref="ReactorApplication.OnUnhandledException"/>. When that callback marks it
    /// handled, nothing is shown where the failure happened and the app keeps running;
    /// otherwise the exception is rethrown out of the render pass (stack preserved), where
    /// it follows the dispatcher's normal unhandled-exception behavior. The handler's
    /// return value is ignored. Has no effect if the handler throws afterwards.
    /// </summary>
    public void Propagate() => IsPropagationRequested = true;
}
