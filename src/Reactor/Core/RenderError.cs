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
/// <para>The handler can run many times for the same fault. Reactor does not latch a
/// failing component: every later render retries it (that retry is how recovery works),
/// and if it throws again the handler is called again — once per re-render of a failing
/// child, and once per host render for a host-level failure. De-duplicate side effects
/// such as telemetry or logging, for example by remembering the exceptions already
/// recorded.</para>
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
    /// <see cref="ReactorApplication.OnUnhandledException"/>. The handler's return value is
    /// ignored. Has no effect if the handler throws afterwards.
    /// </summary>
    /// <remarks>
    /// <para>When <see cref="ReactorApplication.OnUnhandledException"/> returns <c>true</c>,
    /// nothing is shown where the failure happened and the app keeps running. Otherwise the
    /// exception is rethrown (stack preserved) out of whatever ran the render:</para>
    /// <list type="bullet">
    /// <item>Re-renders, and every render of a <see cref="Hosting.ReactorHostControl"/>, run
    /// from a <c>DispatcherQueue</c> callback. WinUI ends the process for an exception that
    /// escapes one, without raising <c>Application.UnhandledException</c> or
    /// <c>AppDomain.UnhandledException</c> (microsoft/microsoft-ui-xaml#8940). The
    /// <see cref="ReactorApplication.OnUnhandledException"/> call Reactor makes first is
    /// therefore the only managed hook the app gets, and returning <c>false</c> (or not
    /// setting it) ends the process.</item>
    /// <item>The first render of a <see cref="Hosting.ReactorHost"/> runs synchronously
    /// inside <c>Mount</c> / <c>ReactorApp.OpenWindow</c>, so there the exception is thrown
    /// to that caller.</item>
    /// </list>
    /// <para><see cref="ReactorApplication.OnUnhandledException"/> is static and is called
    /// even when the app's <c>Application</c> is not a <see cref="ReactorApplication"/> — for
    /// example a XAML app that hosts Reactor content in <see cref="Hosting.ReactorHostControl"/>.
    /// For such an app it is the only way to observe a propagated render error.</para>
    /// </remarks>
    public void Propagate() => IsPropagationRequested = true;
}
