using System;
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
    // Propagation state is thread-static (rendering and disposal are UI-thread synchronous)
    // and scoped to Reactor frames. Each outermost frame (render loop, host Dispose,
    // Reconciler.Dispose) opens a PropagationScope. Frames nest, because app code run by a
    // frame (a cleanup, a Render) can synchronously render or dispose another host, so every
    // piece of state is either saved/restored by the scope or reset only when a new
    // top-level dispatch begins.

    // The exception this frame is propagating. Disposal uses it to rethrow only one
    // propagated cleanup failure. Saved and restored by each scope, so a nested frame can
    // never clear an outer frame's marker.
    [ThreadStatic] private static Exception? t_propagating;

    // Exceptions ReactorApplication.OnUnhandledException declined during the current
    // top-level dispatch. They are on their way out, so every Reactor catch site lets them
    // pass (IsPropagating), including those of outer frames after a nested frame has ended,
    // and Application.UnhandledException does not ask the app a second time. Cleared when the
    // next top-level dispatch begins, so a later, unrelated throw of the same instance is an
    // ordinary error again.
    [ThreadStatic] private static List<Exception>? t_declined;

    // Open PropagationScopes on this thread; 0 means the next scope starts a new top-level
    // dispatch.
    [ThreadStatic] private static int t_scopeDepth;

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

    /// <summary>
    /// Whether <paramref name="ex"/> is on its way out to the unhandled-exception path and
    /// must not be caught and handled again: this frame's propagation, or any exception the
    /// app declined during the current dispatch (for example in a nested frame).
    /// </summary>
    internal static bool IsPropagating(Exception ex) =>
        ReferenceEquals(t_propagating, ex) || IndexOfDeclined(ex) >= 0;

    private static int IndexOfDeclined(Exception ex)
    {
        var declined = t_declined;
        if (declined is null) return -1;
        for (int i = 0; i < declined.Count; i++)
            if (ReferenceEquals(declined[i], ex)) return i;
        return -1;
    }

    /// <summary>
    /// Saves the current propagation marker and starts a fresh one; disposing restores the
    /// saved marker. See <see cref="t_propagating"/>. <c>default</c> is an inactive scope
    /// whose disposal does nothing, for callers that only sometimes open one.
    /// </summary>
    internal readonly struct PropagationScope : IDisposable
    {
        private readonly Exception? _outer;
        private readonly bool _active;

        internal PropagationScope(Exception? outer)
        {
            _outer = outer;
            _active = true;
        }

        public void Dispose()
        {
            if (!_active) return;
            t_propagating = _outer;
            t_scopeDepth--;
        }
    }

    /// <summary>
    /// Opens a propagation scope for an outermost Reactor frame — the render loop, host
    /// <c>Dispose</c>, a top-level <c>Reconciler.Reconcile</c>, or <c>Reconciler.Dispose</c>.
    /// Dispose the result in a <c>finally</c> (or a <c>using</c>).
    /// </summary>
    internal static PropagationScope EnterPropagationScope()
    {
        // A new top-level dispatch: whatever was declined in an earlier one has already
        // left Reactor (and, if WinUI surfaced it, reached Application.UnhandledException).
        if (t_scopeDepth++ == 0)
            t_declined?.Clear();
        var scope = new PropagationScope(t_propagating);
        t_propagating = null;
        return scope;
    }

    /// <summary>
    /// Whether <paramref name="ex"/> was already offered to
    /// <see cref="ReactorApplication.OnUnhandledException"/> and declined in this dispatch.
    /// Consumes the mark, so it suppresses at most one duplicate report.
    /// </summary>
    internal static bool TryConsumeDeclined(Exception ex)
    {
        int index = IndexOfDeclined(ex);
        if (index < 0) return false;
        t_declined!.RemoveAt(index);
        return true;
    }

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
        // A declined RenderError.Propagate() from nested Reactor work the handler started is
        // not a handler failure; it keeps going out like at every catch site.
        catch (Exception hx) when (hx is not OutOfMemoryException and not StackOverflowException && !IsPropagating(hx))
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
    /// install and, for an app element, the element tree it came from (kept as the host's
    /// current tree so the next render reconciles away from it). <paramref name="install"/>
    /// reconciles the guarded fallback against the host's current tree, so a fallback shown
    /// for a repeated failure updates the previous one in place, and the replaced tree is
    /// unmounted (running its cleanups) rather than leaked. When the handler's outcome shows
    /// no Reactor tree (it propagated, or it threw), <paramref name="releaseCurrent"/>
    /// unmounts the current tree for the same reason. When the outcome is
    /// <see cref="Outcome.Propagate"/>, the content is null and the caller must install it
    /// before calling <see cref="RaiseUnhandled"/>.
    /// </summary>
    /// <remarks>
    /// With no handler (or one returning <c>null</c>) the built-in panel replaces the content
    /// exactly as before #1291, current tree and theme listener included. Every other outcome
    /// replaces the tree (<c>ReplacesTree</c>), so the host moves its theme listener to the
    /// new content (or detaches it when there is none).
    /// </remarks>
    internal static (UIElement? Content, Element? Tree, bool Propagate, bool ReplacesTree) BuildHostFallback(
        RenderErrorHandler? handler, RenderError error, ILogger? logger,
        Func<Element, UIElement?> install, Action releaseCurrent)
    {
        switch (InvokeHandler(handler, error, logger, out var appElement))
        {
            case Outcome.AppElement:
                var guarded = Guard(appElement!);
                try
                {
                    return (install(guarded), guarded, false, true);
                }
                // A declined propagation from nested work started while installing (e.g. a
                // cleanup of the replaced tree) keeps going out, like at every catch site.
                catch (Exception mountEx) when (mountEx is not OutOfMemoryException and not StackOverflowException
                    && !IsPropagating(mountEx))
                {
                    // The reconcile against the current tree failed part-way, so that tree's
                    // state is unknown; it is not unmounted a second time.
                    logger?.LogError(mountEx, "RenderErrorHandler fallback failed to mount; showing the neutral fallback");
                    return (ErrorFallback.BuildSafePanel(), null, false, true);
                }
            case Outcome.HandlerFailed:
                Release(releaseCurrent, logger);
                return (ErrorFallback.BuildSafePanel(), null, false, true);
            case Outcome.Propagate:
                Release(releaseCurrent, logger);
                return (null, null, true, true);
            default:
                return (ErrorFallback.BuildPanel(error.Exception), null, false, false);
        }
    }

    private static void Release(Action releaseCurrent, ILogger? logger)
    {
        try
        {
            releaseCurrent();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException && !IsPropagating(ex))
        {
            // Best effort: the error being handled matters more than a failed teardown.
            logger?.LogError(ex, "Unmounting the failed tree threw; continuing with the render-error outcome");
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
        // Only one exception can leave disposal. If a propagation is already in flight on
        // this thread (an earlier cleanup of this context, or of the root before the
        // reconciler's), this one has been reported and offered to the app but is not
        // rethrown, so the in-flight marker keeps naming the exception that will be.
        if (t_propagating is not null)
            return null;
        return BeginPropagation(ex);
    }

    /// <summary>
    /// Runs <paramref name="context"/>'s cleanups during disposal. Every cleanup runs; the
    /// handler is resolved when each one fails, so a cleanup that sets or clears it affects
    /// the later ones. A failure with no handler at that moment escapes immediately, as
    /// before #1291. Otherwise it is reported, and the first propagation is kept in
    /// <paramref name="pending"/> for the caller to rethrow after disposal.
    /// </summary>
    internal static void RunCleanups(
        RenderContext? context, Func<RenderErrorHandler?> resolveHandler, string? componentName, bool isHostLevel,
        ILogger? logger, ref ExceptionDispatchInfo? pending)
    {
        if (context is null) return;
        ExceptionDispatchInfo? first = null;
        context.RunCleanupsIsolated(ex =>
        {
            ExceptionDispatchInfo? propagation;
            if (IsPropagating(ex))
            {
                // Already declined by the app in a nested frame the cleanup started: keep it
                // going out instead of reporting it again as this cleanup's own failure.
                propagation = ContinuePropagation(ex);
            }
            else if (resolveHandler() is { } handler)
            {
                // Report every failure; only the first propagation is rethrown.
                propagation = ReportCleanup(handler, ex, componentName, isHostLevel, logger);
            }
            else
            {
                // No handler when this cleanup failed: the pre-#1291 outcome, it escapes now.
                ExceptionDispatchInfo.Capture(ex).Throw();
                return;
            }
            first ??= propagation;
        });
        pending ??= first;
    }

    /// <summary>
    /// Delivers <paramref name="ex"/> to <see cref="ReactorApplication.OnUnhandledException"/>
    /// (logging through <see cref="ReactorApp.AppLogger"/>). Returns whether the app marked
    /// it handled.
    /// </summary>
    internal static bool TryReportUnhandled(Exception ex) => ReactorApplication.ReportUnhandled(ex);

    // The app declined the exception: mark it as this frame's propagation and as declined in
    // this dispatch (so every Reactor catch site lets it pass and Application.UnhandledException
    // does not ask again).
    private static ExceptionDispatchInfo BeginPropagation(Exception ex)
    {
        if (IndexOfDeclined(ex) < 0)
            (t_declined ??= new List<Exception>()).Add(ex);
        t_propagating = ex;
        return ExceptionDispatchInfo.Capture(ex);
    }

    // An exception that must leave disposal but was not (or not again) offered to the app.
    // Becomes this frame's propagation unless another one is already in flight.
    private static ExceptionDispatchInfo? ContinuePropagation(Exception ex)
    {
        if (t_propagating is not null && !ReferenceEquals(t_propagating, ex))
            return null;
        t_propagating = ex;
        return ExceptionDispatchInfo.Capture(ex);
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
        BeginPropagation(ex).Throw();
    }
}
