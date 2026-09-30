using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Shared plumbing behind <see cref="RenderErrorHandler"/> (issue #1291): resolves the
/// effective handler, invokes it defensively, and routes <see cref="RenderError.Propagate"/>
/// to the unhandled-exception path. Every site that used to go straight to
/// <see cref="ErrorFallback"/> goes through here instead.
/// </summary>
internal static class RenderErrorDispatch
{
    // Exceptions that are on their way out to the unhandled-exception path. Reactor's own
    // catch sites filter on this so a propagated in-tree exception is not re-caught by the
    // host's outer catch and fed to the handler a second time.
    private static readonly ConditionalWeakTable<Exception, object> s_propagating = new();

    // Exceptions already delivered to ReactorApplication.OnUnhandledException, so the
    // Application.UnhandledException handler does not deliver them twice when the rethrow
    // happens to reach it.
    private static readonly ConditionalWeakTable<Exception, object> s_reported = new();

    private static readonly object s_marker = new();

    /// <summary>A host's own handler wins; otherwise the app-wide default.</summary>
    internal static RenderErrorHandler? Resolve(RenderErrorHandler? hostHandler) =>
        hostHandler ?? ReactorApp.DefaultRenderErrorHandler;

    internal static bool IsPropagating(Exception ex) => s_propagating.TryGetValue(ex, out _);

    internal static bool WasReportedAsUnhandled(Exception ex) => s_reported.TryGetValue(ex, out _);

    /// <summary>
    /// Invokes <paramref name="handler"/>. Returns the app's element, or <c>null</c> for the
    /// built-in fallback. A handler that throws is logged and treated as returning <c>null</c>
    /// — a broken error handler must not hide the original failure.
    /// </summary>
    internal static Element? InvokeHandler(RenderErrorHandler? handler, RenderError error, ILogger? logger)
    {
        if (handler is null) return null;
        try
        {
            return handler(error);
        }
        catch (Exception hx) when (hx is not OutOfMemoryException and not StackOverflowException)
        {
            logger?.LogError(hx, "RenderErrorHandler threw while handling {ExceptionType}; using the built-in fallback",
                error.Exception.GetType().Name);
            global::System.Diagnostics.Debug.WriteLine(
                $"[Reactor] RenderErrorHandler threw ({hx.GetType().Name}: {hx.Message}); using the built-in fallback.");
            return null;
        }
    }

    /// <summary>
    /// Builds the in-tree placeholder for a component whose render (or effect flush) threw.
    /// With no handler this is exactly the built-in <see cref="ErrorFallback.BuildElement"/>.
    /// </summary>
    internal static Element BuildInTreeFallback(RenderErrorHandler? handler, RenderError error, ILogger? logger)
    {
        var appElement = InvokeHandler(handler, error, logger);
        if (error.IsPropagationRequested)
        {
            RaiseUnhandled(error.Exception);
            return EmptyElement.Instance;
        }
        return appElement is null ? ErrorFallback.BuildElement(error.Exception) : Guard(appElement);
    }

    /// <summary>
    /// Wraps an app-supplied fallback in an internal error boundary whose own fallback is
    /// the built-in placeholder, so a fallback that itself throws degrades to the built-in
    /// UI instead of re-entering the handler (and looping).
    /// </summary>
    internal static Element Guard(Element appElement) =>
        new ErrorBoundaryElement(appElement, ErrorFallback.BuildElement);

    /// <summary>
    /// Reports a cleanup exception thrown during host/reconciler disposal. Returns
    /// <c>false</c> when no handler is configured — the caller must then rethrow, which
    /// keeps the pre-#1291 behavior. Otherwise the handler is notified (return ignored);
    /// if it asked to propagate and the unhandled-exception path did not handle it,
    /// <paramref name="pending"/> captures the exception for the caller to rethrow once
    /// disposal has finished.
    /// </summary>
    internal static bool ReportCleanup(
        RenderErrorHandler? handler, Exception ex, string? componentName, bool isHostLevel,
        ILogger? logger, ref ExceptionDispatchInfo? pending)
    {
        if (handler is null) return false;
        logger?.LogError(ex, "Effect cleanup threw during dispose: {ComponentName}", componentName ?? "(root)");
        var error = new RenderError(ex, RenderErrorSource.Cleanup, componentName, isHostLevel);
        InvokeHandler(handler, error, logger);
        if (error.IsPropagationRequested && !TryReportUnhandled(ex))
        {
            s_propagating.AddOrUpdate(ex, s_marker);
            pending ??= ExceptionDispatchInfo.Capture(ex);
        }
        return true;
    }

    /// <summary>
    /// Delivers <paramref name="ex"/> to <see cref="ReactorApplication.OnUnhandledException"/>
    /// (logging through <see cref="ReactorApp.AppLogger"/>). Returns whether the app marked
    /// it handled.
    /// </summary>
    internal static bool TryReportUnhandled(Exception ex)
    {
        s_reported.AddOrUpdate(ex, s_marker);
        return ReactorApplication.ReportUnhandled(ex);
    }

    /// <summary>
    /// <see cref="RenderError.Propagate"/>: returns when the app handled the exception;
    /// otherwise rethrows it (stack preserved), marked so Reactor's catch sites let it pass.
    /// </summary>
    /// <remarks>
    /// Reactor renders from <c>DispatcherQueue</c> callbacks, and WinUI does not raise
    /// <c>Application.UnhandledException</c> for exceptions escaping those
    /// (microsoft/microsoft-ui-xaml#8940), so the app's handler is invoked explicitly here
    /// rather than relying on the rethrow to reach it.
    /// </remarks>
    internal static void RaiseUnhandled(Exception ex)
    {
        if (TryReportUnhandled(ex)) return;
        s_propagating.AddOrUpdate(ex, s_marker);
        ExceptionDispatchInfo.Capture(ex).Throw();
    }
}
