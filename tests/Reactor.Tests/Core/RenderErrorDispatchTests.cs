using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

/// <summary>
/// Headless coverage for the render-error hook plumbing (issue #1291). Everything that needs a
/// live WinUI control (built-in fallback, host content swap, dispose) is in the selftest
/// <c>RenderErrorHandlerFixtures</c>.
/// </summary>
[Collection("RenderErrorGlobals")]
public class RenderErrorDispatchTests
{
    private sealed record ProbeElement(string Text) : Element;

    private static RenderError NewError(Exception? ex = null, RenderErrorSource source = RenderErrorSource.ComponentRender) =>
        new(ex ?? new InvalidOperationException("boom"), source, "Probe", isHostLevel: false);

    private static void WithUnhandledCallback(Func<Exception, bool>? callback, Action body)
    {
        var previous = ReactorApplication.OnUnhandledException;
        ReactorApplication.OnUnhandledException = callback;
        try { body(); }
        finally
        {
            ReactorApplication.OnUnhandledException = previous;
            // The in-flight marker is thread-static; don't leak it into the next test.
            RenderErrorDispatch.EndPropagation();
        }
    }

    // ── RenderError ──────────────────────────────────────────────────────────

    [Fact]
    public void RenderError_Exposes_Its_Inputs_And_Starts_Unpropagated()
    {
        var ex = new InvalidOperationException("boom");
        var error = new RenderError(ex, RenderErrorSource.Effects, "MyComponent", isHostLevel: true);

        Assert.Same(ex, error.Exception);
        Assert.Equal(RenderErrorSource.Effects, error.Source);
        Assert.Equal("MyComponent", error.ComponentName);
        Assert.True(error.IsHostLevel);
        Assert.False(error.IsPropagationRequested);

        error.Propagate();
        Assert.True(error.IsPropagationRequested);
    }

    [Fact]
    public void RenderError_Rejects_Null_Exception() =>
        Assert.Throws<ArgumentNullException>(() => new RenderError(null!, RenderErrorSource.Reconcile, null, isHostLevel: true));

    // ── InvokeHandler outcome table ─────────────────────────────────────────

    [Fact]
    public void InvokeHandler_No_Handler_Is_BuiltIn()
    {
        var outcome = RenderErrorDispatch.InvokeHandler(null, NewError(), logger: null, out var element);

        Assert.Equal(RenderErrorDispatch.Outcome.BuiltIn, outcome);
        Assert.Null(element);
    }

    [Fact]
    public void InvokeHandler_Null_Return_Is_BuiltIn()
    {
        var outcome = RenderErrorDispatch.InvokeHandler(_ => null, NewError(), logger: null, out var element);

        Assert.Equal(RenderErrorDispatch.Outcome.BuiltIn, outcome);
        Assert.Null(element);
    }

    [Fact]
    public void InvokeHandler_Returns_The_App_Element_And_Passes_The_Error_Through()
    {
        var error = NewError();
        RenderError? seen = null;
        var probe = new ProbeElement("custom");

        var outcome = RenderErrorDispatch.InvokeHandler(e => { seen = e; return probe; }, error, logger: null, out var element);

        Assert.Equal(RenderErrorDispatch.Outcome.AppElement, outcome);
        Assert.Same(probe, element);
        Assert.Same(error, seen);
    }

    [Fact]
    public void InvokeHandler_A_Throwing_Handler_Fails_Closed_Not_To_The_Detailed_Fallback()
    {
        var outcome = RenderErrorDispatch.InvokeHandler(
            _ => throw new InvalidOperationException("handler bug"), NewError(), logger: null, out var element);

        Assert.Equal(RenderErrorDispatch.Outcome.HandlerFailed, outcome);
        Assert.Null(element);
    }

    [Fact]
    public void InvokeHandler_Propagate_Wins_Over_A_Returned_Element()
    {
        var outcome = RenderErrorDispatch.InvokeHandler(
            e => { e.Propagate(); return new ProbeElement("ignored"); }, NewError(), logger: null, out _);

        Assert.Equal(RenderErrorDispatch.Outcome.Propagate, outcome);
    }

    [Fact]
    public void InvokeHandler_Propagate_Then_Throw_Cancels_The_Propagation()
    {
        var error = NewError();

        var outcome = RenderErrorDispatch.InvokeHandler(
            e => { e.Propagate(); throw new InvalidOperationException("after propagate"); }, error, logger: null, out _);

        Assert.Equal(RenderErrorDispatch.Outcome.HandlerFailed, outcome);
        Assert.False(error.IsPropagationRequested);
    }

    // ── Resolution + guard ───────────────────────────────────────────────────

    [Fact]
    public void Resolve_Host_Handler_Wins_Then_App_Default_Then_Null()
    {
        var previous = ReactorApp.DefaultRenderErrorHandler;
        try
        {
            RenderErrorHandler hostHandler = _ => new ProbeElement("host");
            RenderErrorHandler appDefault = _ => new ProbeElement("default");

            ReactorApp.DefaultRenderErrorHandler = null;
            Assert.Null(RenderErrorDispatch.Resolve(null));
            Assert.Same(hostHandler, RenderErrorDispatch.Resolve(hostHandler));

            ReactorApp.DefaultRenderErrorHandler = appDefault;
            Assert.Same(appDefault, RenderErrorDispatch.Resolve(null));
            Assert.Same(hostHandler, RenderErrorDispatch.Resolve(hostHandler));
        }
        finally
        {
            ReactorApp.DefaultRenderErrorHandler = previous;
        }
    }

    [Fact]
    public void Guard_Wraps_The_App_Element_In_A_Boundary_Whose_Fallback_Is_The_Neutral_One()
    {
        var probe = new ProbeElement("custom");

        var guarded = RenderErrorDispatch.Guard(probe);

        var boundary = Assert.IsType<ErrorBoundaryElement>(guarded);
        Assert.Same(probe, boundary.Child);
        // Method-group identity: the guard must degrade to the detail-free placeholder, never
        // to ErrorFallback.BuildElement (which renders the exception text).
        Assert.Equal(nameof(ErrorFallback.BuildSafeElement), boundary.Fallback.Method.Name);
    }

    // ── RaiseUnhandled ───────────────────────────────────────────────────────

    [Fact]
    public void RaiseUnhandled_Returns_When_The_App_Handles_It()
    {
        var ex = new InvalidOperationException("handled");
        Exception? seen = null;
        WithUnhandledCallback(e => { seen = e; return true; }, () =>
        {
            RenderErrorDispatch.RaiseUnhandled(ex);

            Assert.Same(ex, seen);
            // Handled: nothing is rethrown, so nothing is marked in flight or declined.
            Assert.False(RenderErrorDispatch.IsPropagating(ex));
            Assert.False(RenderErrorDispatch.TryConsumeDeclined(ex));
        });
    }

    [Fact]
    public void RaiseUnhandled_Rethrows_The_Same_Exception_Marked_As_Propagating_When_Unhandled()
    {
        var ex = new InvalidOperationException("unhandled");
        WithUnhandledCallback(_ => false, () =>
        {
            var thrown = Assert.Throws<InvalidOperationException>(() => RenderErrorDispatch.RaiseUnhandled(ex));

            Assert.Same(ex, thrown);
            Assert.True(RenderErrorDispatch.IsPropagating(ex));
            // The declined mark is consumed by the first check (Application.UnhandledException).
            Assert.True(RenderErrorDispatch.TryConsumeDeclined(ex));
            Assert.False(RenderErrorDispatch.TryConsumeDeclined(ex));
        });
    }

    [Fact]
    public void RaiseUnhandled_With_No_App_Callback_Rethrows()
    {
        var ex = new InvalidOperationException("no callback");
        WithUnhandledCallback(null, () =>
            Assert.Same(ex, Assert.Throws<InvalidOperationException>(() => RenderErrorDispatch.RaiseUnhandled(ex))));
    }

    // ── Cleanup reporting ────────────────────────────────────────────────────

    [Fact]
    public void ReportCleanup_Notifies_The_Handler_With_Source_Cleanup_And_Ignores_Its_Return()
    {
        var ex = new InvalidOperationException("cleanup");
        RenderError? seen = null;

        var pending = RenderErrorDispatch.ReportCleanup(e => { seen = e; return new ProbeElement("ignored"); },
            ex, "Probe", isHostLevel: false, logger: null);

        Assert.Null(pending);
        Assert.NotNull(seen);
        Assert.Same(ex, seen!.Exception);
        Assert.Equal(RenderErrorSource.Cleanup, seen.Source);
        Assert.Equal("Probe", seen.ComponentName);
        Assert.False(seen.IsHostLevel);
    }

    [Fact]
    public void ReportCleanup_Unhandled_Propagation_Is_Deferred_To_The_Caller()
    {
        var ex = new InvalidOperationException("cleanup");
        WithUnhandledCallback(_ => false, () =>
        {
            var pending = RenderErrorDispatch.ReportCleanup(e => { e.Propagate(); return null; },
                ex, "Probe", isHostLevel: true, logger: null);

            Assert.NotNull(pending);
            Assert.Same(ex, pending!.SourceException);
            Assert.True(RenderErrorDispatch.IsPropagating(ex));
        });
    }

    [Fact]
    public void ReportCleanup_Handled_Propagation_Leaves_Nothing_Pending()
    {
        WithUnhandledCallback(_ => true, () =>
            Assert.Null(RenderErrorDispatch.ReportCleanup(e => { e.Propagate(); return null; },
                new InvalidOperationException("cleanup"), "Probe", isHostLevel: true, logger: null)));
    }

    private static RenderContext ContextWithCleanups(params Action[] cleanups)
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        foreach (var cleanup in cleanups)
            ctx.UseEffect(() => cleanup, Array.Empty<object>());
        ctx.FlushEffects();
        return ctx;
    }

    [Fact]
    public void RunCleanups_Without_A_Handler_Keeps_The_First_Throw_Escaping()
    {
        bool secondRan = false;
        var ctx = ContextWithCleanups(
            () => throw new InvalidOperationException("first"),
            () => secondRan = true);
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pending = null;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RenderErrorDispatch.RunCleanups(ctx, handler: null, "Probe", isHostLevel: false, logger: null, ref pending));

        Assert.Equal("first", ex.Message);
        Assert.False(secondRan);
    }

    [Fact]
    public void RunCleanups_With_A_Handler_Runs_Every_Cleanup_And_Reports_Each_Failure()
    {
        bool lastRan = false;
        var ctx = ContextWithCleanups(
            () => throw new InvalidOperationException("first"),
            () => throw new InvalidOperationException("second"),
            () => lastRan = true);
        var log = new List<RenderError>();
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pending = null;

        RenderErrorDispatch.RunCleanups(ctx, e => { log.Add(e); return null; }, "Probe", isHostLevel: true, logger: null, ref pending);

        Assert.True(lastRan);
        Assert.Null(pending);
        Assert.Equal(new[] { "first", "second" }, log.Select(e => e.Exception.Message));
        Assert.All(log, e => Assert.Equal(RenderErrorSource.Cleanup, e.Source));
    }

    [Fact]
    public void RunCleanups_With_A_Handler_Keeps_The_First_Unhandled_Propagation_And_Still_Drains()
    {
        bool lastRan = false;
        var ctx = ContextWithCleanups(
            () => throw new InvalidOperationException("first"),
            () => throw new InvalidOperationException("second"),
            () => lastRan = true);
        var reported = new List<string>();
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pending = null;

        WithUnhandledCallback(_ => false, () =>
            RenderErrorDispatch.RunCleanups(ctx, e => { reported.Add(e.Exception.Message); e.Propagate(); return null; }, "Probe",
                isHostLevel: true, logger: null, ref pending));

        Assert.True(lastRan);
        Assert.NotNull(pending);
        Assert.Equal("first", pending!.SourceException.Message);
        // Every failure reaches the handler, including those after the first propagation.
        Assert.Equal(new[] { "first", "second" }, reported);
    }

    [Fact]
    public void Propagation_Marker_Is_Scoped_Not_Permanent()
    {
        var ex = new InvalidOperationException("swallowed by the app");
        WithUnhandledCallback(_ => false, () =>
        {
            Assert.Throws<InvalidOperationException>(() => RenderErrorDispatch.RaiseUnhandled(ex));
            Assert.True(RenderErrorDispatch.IsPropagating(ex));

            // The outermost Reactor frame ends the scope once the exception has left.
            RenderErrorDispatch.EndPropagation();

            // A later throw of the same instance is an ordinary error again.
            Assert.False(RenderErrorDispatch.IsPropagating(ex));
        });
    }

    // ── WindowSpec ───────────────────────────────────────────────────────────

    [Fact]
    public void WindowSpec_RenderErrorHandler_Defaults_To_Null_And_Participates_In_Equality()
    {
        var spec = new WindowSpec();
        Assert.Null(spec.RenderErrorHandler);

        RenderErrorHandler handler = _ => null;
        var withHandler = spec with { RenderErrorHandler = handler };

        Assert.Same(handler, withHandler.RenderErrorHandler);
        Assert.NotEqual(spec, withHandler);
        Assert.Equal(spec, withHandler with { RenderErrorHandler = null });
    }
}
