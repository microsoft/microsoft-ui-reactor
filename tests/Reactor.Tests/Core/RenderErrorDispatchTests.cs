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
        // The in-flight marker is thread-static; scope it so it can't leak into the next test.
        using var scope = RenderErrorDispatch.EnterPropagationScope();
        try { body(); }
        finally { ReactorApplication.OnUnhandledException = previous; }
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
            RenderErrorDispatch.RunCleanups(ctx, () => null, "Probe", isHostLevel: false, logger: null, ref pending));

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

        RenderErrorHandler handler = e => { log.Add(e); return null; };
        RenderErrorDispatch.RunCleanups(ctx, () => handler, "Probe", isHostLevel: true, logger: null, ref pending);

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
        bool markerOnRethrown = false;

        WithUnhandledCallback(_ => false, () =>
        {
            RenderErrorHandler handler = e => { reported.Add(e.Exception.Message); e.Propagate(); return null; };
            RenderErrorDispatch.RunCleanups(ctx, () => handler, "Probe",
                isHostLevel: true, logger: null, ref pending);
            // The in-flight marker names the exception that will be rethrown, not the last
            // one that asked to propagate.
            markerOnRethrown = pending is not null && RenderErrorDispatch.IsPropagating(pending.SourceException);
        });

        Assert.True(lastRan);
        Assert.NotNull(pending);
        Assert.Equal("first", pending!.SourceException.Message);
        // Every failure reaches the handler, including those after the first propagation.
        Assert.Equal(new[] { "first", "second" }, reported);
        Assert.True(markerOnRethrown);
    }

    [Fact]
    public void ReportCleanup_A_Second_Propagation_Does_Not_Displace_The_One_In_Flight()
    {
        var first = new InvalidOperationException("first");
        var second = new InvalidOperationException("second");
        RenderErrorHandler propagate = e => { e.Propagate(); return null; };
        WithUnhandledCallback(_ => false, () =>
        {
            var pendingFirst = RenderErrorDispatch.ReportCleanup(propagate, first, "Root", isHostLevel: true, logger: null);
            // e.g. the reconciler's child cleanups, disposed after the host's root cleanups.
            var pendingSecond = RenderErrorDispatch.ReportCleanup(propagate, second, "Child", isHostLevel: false, logger: null);

            Assert.Same(first, pendingFirst!.SourceException);
            Assert.Null(pendingSecond);
            Assert.True(RenderErrorDispatch.IsPropagating(first));
            Assert.False(RenderErrorDispatch.IsPropagating(second));
            Assert.False(RenderErrorDispatch.TryConsumeDeclined(second));
        });
    }

    [Fact]
    public void Propagation_Marker_Is_Scoped_Not_Permanent()
    {
        var ex = new InvalidOperationException("swallowed by the app");
        var previous = ReactorApplication.OnUnhandledException;
        ReactorApplication.OnUnhandledException = _ => false;
        try
        {
            using (RenderErrorDispatch.EnterPropagationScope())
            {
                Assert.Throws<InvalidOperationException>(() => RenderErrorDispatch.RaiseUnhandled(ex));
                Assert.True(RenderErrorDispatch.IsPropagating(ex));
            }

            // The next top-level dispatch starts clean: a later throw of the same instance is
            // an ordinary error again.
            using (RenderErrorDispatch.EnterPropagationScope())
            {
                Assert.False(RenderErrorDispatch.IsPropagating(ex));
                Assert.False(RenderErrorDispatch.TryConsumeDeclined(ex));
            }
        }
        finally
        {
            ReactorApplication.OnUnhandledException = previous;
        }
    }

    [Fact]
    public void Propagation_Scopes_Nest_So_An_Inner_Frame_Cannot_Clear_An_Outer_Marker()
    {
        var outer = new InvalidOperationException("outer");
        var inner = new InvalidOperationException("inner");
        RenderErrorHandler propagate = e => { e.Propagate(); return null; };
        WithUnhandledCallback(_ => false, () =>
        {
            // Outer frame: a dispose cleanup's propagation is in flight.
            Assert.NotNull(RenderErrorDispatch.ReportCleanup(propagate, outer, "Root", isHostLevel: true, logger: null));
            Assert.True(RenderErrorDispatch.IsPropagating(outer));

            // App cleanup code synchronously starts another frame (e.g. a new host's first
            // render), which propagates its own failure and ends.
            using (RenderErrorDispatch.EnterPropagationScope())
            {
                // A fresh frame: its own first propagation is not blocked by the outer one.
                Assert.NotNull(RenderErrorDispatch.ReportCleanup(propagate, inner, "Nested", isHostLevel: true, logger: null));
                Assert.True(RenderErrorDispatch.IsPropagating(inner));
            }

            // Back in the outer frame: its in-flight marker is intact, so a further outer
            // propagation is still not begun (one exception leaves disposal) ...
            Assert.Null(RenderErrorDispatch.ReportCleanup(propagate, new InvalidOperationException("third"),
                "Root", isHostLevel: true, logger: null));
            Assert.True(RenderErrorDispatch.IsPropagating(outer));
            // ... and the nested frame's declined exception, still unwinding through the outer
            // frame, is recognised so outer catch sites let it pass instead of handling it again.
            Assert.True(RenderErrorDispatch.IsPropagating(inner));
        });
    }

    [Fact]
    public void RunCleanups_A_Nested_Declined_Exception_Is_Not_Reported_Again_And_Still_Escapes()
    {
        var nested = new InvalidOperationException("declined in a nested frame");
        var log = new List<RenderError>();
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pending = null;
        WithUnhandledCallback(_ => false, () =>
        {
            var ctx = ContextWithCleanups(() =>
            {
                // App cleanup code that synchronously runs another Reactor frame, whose
                // propagated render error the app declines.
                using (RenderErrorDispatch.EnterPropagationScope())
                    RenderErrorDispatch.RaiseUnhandled(nested);
            });
            RenderErrorHandler handler = e => { log.Add(e); return null; };
            RenderErrorDispatch.RunCleanups(ctx, () => handler, "Outer", isHostLevel: true, logger: null, ref pending);
        });

        Assert.Empty(log);
        Assert.Same(nested, pending?.SourceException);
    }

    [Fact]
    public void RunCleanups_Resolves_The_Handler_For_Each_Failure()
    {
        var first = new List<string>();
        var second = new List<string>();
        RenderErrorHandler secondHandler = e => { second.Add(e.Exception.Message); return null; };
        RenderErrorHandler? current = null;
        RenderErrorHandler firstHandler = e => { first.Add(e.Exception.Message); current = secondHandler; return null; };
        current = firstHandler;
        var ctx = ContextWithCleanups(
            () => throw new InvalidOperationException("a"),
            () => throw new InvalidOperationException("b"));
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pending = null;

        RenderErrorDispatch.RunCleanups(ctx, () => current, "Probe", isHostLevel: true, logger: null, ref pending);

        Assert.Equal(new[] { "a" }, first);
        Assert.Equal(new[] { "b" }, second);
        Assert.Null(pending);
    }

    [Fact]
    public void RunCleanups_A_Handler_Removed_Mid_Disposal_Lets_The_Next_Failure_Escape_Immediately()
    {
        bool lastRan = false;
        RenderErrorHandler? current = null;
        RenderErrorHandler clearing = _ => { current = null; return null; };
        current = clearing;
        var ctx = ContextWithCleanups(
            () => throw new InvalidOperationException("reported"),
            () => throw new InvalidOperationException("after removal"),
            () => lastRan = true);
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pending = null;

        var escaped = Assert.Throws<InvalidOperationException>(() =>
            RenderErrorDispatch.RunCleanups(ctx, () => current, "Probe", isHostLevel: true, logger: null, ref pending));

        // No handler when it failed: the pre-#1291 outcome.
        Assert.Equal("after removal", escaped.Message);
        Assert.False(lastRan);
    }

    [Fact]
    public void RunCleanups_A_Handler_Installed_By_An_Earlier_Cleanup_Handles_A_Later_Failure()
    {
        var log = new List<string>();
        RenderErrorHandler? current = null;
        RenderErrorHandler installed = e => { log.Add(e.Exception.Message); return null; };
        bool lastRan = false;
        var ctx = ContextWithCleanups(
            () => current = installed,
            () => throw new InvalidOperationException("after install"),
            () => lastRan = true);
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pending = null;

        RenderErrorDispatch.RunCleanups(ctx, () => current, "Probe", isHostLevel: true, logger: null, ref pending);

        Assert.Equal(new[] { "after install" }, log);
        Assert.True(lastRan);
        Assert.Null(pending);
    }

    [Fact]
    public void BuildHostFallback_A_Declined_Propagation_From_Install_Escapes()
    {
        var declined = new InvalidOperationException("declined during install");
        WithUnhandledCallback(_ => false, () =>
        {
            // Nested Reactor work run while installing the fallback (e.g. a cleanup of the
            // replaced tree) propagated an error the app declined.
            Assert.Throws<InvalidOperationException>(() => RenderErrorDispatch.RaiseUnhandled(declined));
            bool released = false;

            var escaped = Assert.Throws<InvalidOperationException>(() => RenderErrorDispatch.BuildHostFallback(
                _ => new ProbeElement("fallback"), NewError(), logger: null,
                install: _ => throw declined,
                releaseCurrent: () => released = true));

            Assert.Same(declined, escaped);
            Assert.False(released);
        });
    }

    [Fact]
    public void Declined_Mark_Survives_Cleanup_Nested_In_An_Outer_Frame()
    {
        var ex = new InvalidOperationException("declined during a failed open");
        var previous = ReactorApplication.OnUnhandledException;
        ReactorApplication.OnUnhandledException = _ => false;
        try
        {
            // OpenWindowCore: the open and its failed-open cleanup share one outer frame.
            using (RenderErrorDispatch.EnterPropagationScope())
            {
                // The synchronous first render (RenderLoop) propagates; the app declines.
                using (RenderErrorDispatch.EnterPropagationScope())
                    Assert.Throws<InvalidOperationException>(() => RenderErrorDispatch.RaiseUnhandled(ex));

                // Failed-open cleanup (window Close/Dispose) runs host Dispose: a nested frame.
                using (RenderErrorDispatch.EnterPropagationScope()) { }
            }

            // The rethrow reaches Application.UnhandledException: still recognised, once.
            Assert.True(RenderErrorDispatch.TryConsumeDeclined(ex));
            Assert.False(RenderErrorDispatch.TryConsumeDeclined(ex));
        }
        finally
        {
            ReactorApplication.OnUnhandledException = previous;
        }
    }

    [Fact]
    public void Declined_Mark_Is_Cleared_By_A_New_Top_Level_Dispatch()
    {
        // Positive control for the test above: without the outer frame, the cleanup's scope
        // is a new top-level dispatch and clears the mark.
        var ex = new InvalidOperationException("declined, then a new dispatch");
        var previous = ReactorApplication.OnUnhandledException;
        ReactorApplication.OnUnhandledException = _ => false;
        try
        {
            using (RenderErrorDispatch.EnterPropagationScope())
                Assert.Throws<InvalidOperationException>(() => RenderErrorDispatch.RaiseUnhandled(ex));
            using (RenderErrorDispatch.EnterPropagationScope()) { }

            Assert.False(RenderErrorDispatch.TryConsumeDeclined(ex));
        }
        finally
        {
            ReactorApplication.OnUnhandledException = previous;
        }
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
