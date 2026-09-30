using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Shared plumbing behind <see cref="RenderErrorHandler"/> (issue #1291): resolves the
/// effective handler, invokes it defensively, and routes <see cref="RenderError.Propagate"/>
/// to the unhandled-exception path. Every site that used to go straight to
/// <see cref="ErrorFallback"/> goes through here instead.
/// </summary>
/// <remarks>
/// Outcome table — the detailed built-in fallback appears only when no handler is set or
/// the handler explicitly returned <c>null</c>. Once an app configured a handler it asked
/// for the exception text to stay off screen, so every failure of the handler path (the
/// handler throws, or its element throws while rendering) fails closed to the neutral
/// <see cref="ErrorFallback.SafeMessage"/>.
/// </remarks>
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

    /// <summary>What the handler decided.</summary>
    internal enum Outcome
    {
        /// <summary>No handler, or it returned null: detailed built-in fallback.</summary>
        BuiltIn,
        /// <summary>The handler returned an element.</summary>
        AppElement,
        /// <summary>The handler threw: neutral fallback, no exception detail.</summary>
        HandlerFailed,
        /// <summary>The handler called <see cref="RenderError.Propagate"/>.</summary>
        Propagate,
    }

    /// <summary>A host's own handler wins; otherwise the app-wide default.</summary>
    internal static RenderErrorHandler? Resolve(RenderErrorHandler? hostHandler) =>
        hostHandler ?? ReactorApp.DefaultRenderErrorHandler;

    internal static bool IsPropagating(Exception ex) => s_propagating.TryGetValue(ex, out _);

    internal static bool WasReportedAsUnhandled(Exception ex) => s_reported.TryGetValue(ex, out _);

    /// <summary>
    /// Invokes <paramref name="handler"/> and classifies the result. A handler that throws is
    /// logged, any <see cref="RenderError.Propagate"/> it requested is cancelled, and the
    /// outcome is <see cref="Outcome.HandlerFailed"/>.
    /// </summary>
    internal static Outcome InvokeHandler(RenderErrorHandler? handler, RenderError error, ILogger? logger, out Element? appElement)
    {
        appElement = null;
        if (handler is null) return Outcome.BuiltIn;
        try
        {
            appElement = handler(error);
        }
        catch (Exception hx) when (hx is not OutOfMemoryException and not StackOverflowException)
        {
            error.CancelPropagation();
            appElement = null;
            logger?.LogError(hx, "RenderErrorHandler threw while handling {ExceptionType}; showing the neutral fallback",
                error.Exception.GetType().Name);
            global::System.Diagnostics.Debug.WriteLine(
                $"[Reactor] RenderErrorHandler threw ({hx.GetType().Name}: {hx.Message}); showing the neutral fallback.");
            return Outcome.HandlerFailed;
        }
        if (error.IsPropagationRequested) return Outcome.Propagate;
        return appElement is null ? Outcome.BuiltIn : Outcome.AppElement;
    }

    /// <summary>
    /// Builds the in-tree placeholder for a component whose render (or effect flush) threw.
    /// With no handler this is exactly the built-in <see cref="ErrorFallback.BuildElement"/>.
    /// </summary>
    internal static Element BuildInTreeFallback(RenderErrorHandler? handler, RenderError error, ILogger? logger)
    {
        switch (InvokeHandler(handler, error, logger, out var appElement))
        {
            case Outcome.AppElement:
                return Guard(appElement!);
            case Outcome.HandlerFailed:
                return ErrorFallback.BuildSafeElement(error.Exception);
            case Outcome.Propagate:
                RaiseUnhandled(error.Exception);
                return EmptyElement.Instance;
            default:
                return ErrorFallback.BuildElement(error.Exception);
        }
    }

    /// <summary>
    /// Wraps an app-supplied fallback in an internal error boundary whose own fallback is
    /// the neutral placeholder, so a fallback that itself throws degrades without exception
    /// detail instead of re-entering the handler (and looping).
    /// </summary>
    internal static Element Guard(Element appElement) =>
        new ErrorBoundaryElement(appElement, ErrorFallback.BuildSafeElement);

    /// <summary>
    /// Host-level counterpart of <see cref="BuildInTreeFallback"/>. Returns the content to
    /// install and, for an app element, the element tree it was mounted from (kept as the
    /// host's current tree so the next good render reconciles away from it). When the
    /// outcome is <see cref="Outcome.Propagate"/>, the content is null and the caller must
    /// install it before calling <see cref="RaiseUnhandled"/>.
    /// </summary>
    internal static (UIElement? Content, Element? Tree, bool Propagate) BuildHostFallback(
        RenderErrorHandler? handler, RenderError error, ILogger? logger, Func<Element, UIElement?> mount)
    {
        switch (InvokeHandler(handler, error, logger, out var appElement))
        {
            case Outcome.AppElement:
                var guarded = Guard(appElement!);
                try
                {
                    return (mount(guarded), guarded, false);
                }
                catch (Exception mountEx) when (mountEx is not OutOfMemoryException and not StackOverflowException)
                {
                    logger?.LogError(mountEx, "RenderErrorHandler fallback failed to mount; showing the neutral fallback");
                    return (ErrorFallback.BuildSafePanel(), null, false);
                }
            case Outcome.HandlerFailed:
                return (ErrorFallback.BuildSafePanel(), null, false);
            case Outcome.Propagate:
                return (null, null, true);
            default:
                return (ErrorFallback.BuildPanel(error.Exception), null, false);
        }
    }

    /// <summary>
    /// Reports one cleanup exception thrown during host/reconciler disposal (the handler is
    /// notified with <see cref="RenderErrorSource.Cleanup"/>; its return value is ignored).
    /// Returns the exception to rethrow once disposal has finished when the handler asked to
    /// propagate and the unhandled-exception path did not handle it; otherwise null.
    /// </summary>
    internal static ExceptionDispatchInfo? ReportCleanup(
        RenderErrorHandler handler, Exception ex, string? componentName, bool isHostLevel, ILogger? logger)
    {
        logger?.LogError(ex, "Effect cleanup threw during dispose: {ComponentName}", componentName ?? "(root)");
        var error = new RenderError(ex, RenderErrorSource.Cleanup, componentName, isHostLevel);
        if (InvokeHandler(handler, error, logger, out _) != Outcome.Propagate || TryReportUnhandled(ex))
            return null;
        s_propagating.AddOrUpdate(ex, s_marker);
        return ExceptionDispatchInfo.Capture(ex);
    }

    /// <summary>
    /// Runs <paramref name="context"/>'s cleanups. With no handler this is the unchanged
    /// <see cref="RenderContext.RunCleanups()"/> (the first throw escapes). With a handler
    /// every cleanup runs, each failure is reported, and the first unhandled propagation is
    /// kept in <paramref name="pending"/> for the caller to rethrow after disposal.
    /// </summary>
    internal static void RunCleanups(
        RenderContext? context, RenderErrorHandler? handler, string? componentName, bool isHostLevel,
        ILogger? logger, ref ExceptionDispatchInfo? pending)
    {
        if (context is null) return;
        if (handler is null)
        {
            context.RunCleanups();
            return;
        }
        ExceptionDispatchInfo? first = null;
        context.RunCleanups(ex => first ??= ReportCleanup(handler, ex, componentName, isHostLevel, logger));
        pending ??= first;
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
