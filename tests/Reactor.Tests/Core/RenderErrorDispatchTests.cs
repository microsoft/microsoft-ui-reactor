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

    [Fact]
    public void InvokeHandler_No_Handler_Means_BuiltIn() =>
        Assert.Null(RenderErrorDispatch.InvokeHandler(null, NewError(), logger: null));

    [Fact]
    public void InvokeHandler_Returns_The_App_Element_And_Passes_The_Error_Through()
    {
        var error = NewError();
        RenderError? seen = null;
        var probe = new ProbeElement("custom");

        var result = RenderErrorDispatch.InvokeHandler(e => { seen = e; return probe; }, error, logger: null);

        Assert.Same(probe, result);
        Assert.Same(error, seen);
    }

    [Fact]
    public void InvokeHandler_A_Throwing_Handler_Falls_Back_To_BuiltIn()
    {
        var result = RenderErrorDispatch.InvokeHandler(
            _ => throw new InvalidOperationException("handler bug"), NewError(), logger: null);

        Assert.Null(result);
    }

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
    public void Guard_Wraps_The_App_Element_In_A_BuiltIn_Boundary()
    {
        var probe = new ProbeElement("custom");

        var guarded = RenderErrorDispatch.Guard(probe);

        var boundary = Assert.IsType<ErrorBoundaryElement>(guarded);
        Assert.Same(probe, boundary.Child);
        Assert.NotNull(boundary.Fallback);
    }

    [Fact]
    public void RaiseUnhandled_Returns_When_The_App_Handles_It()
    {
        var previous = ReactorApplication.OnUnhandledException;
        var ex = new InvalidOperationException("handled");
        Exception? seen = null;
        try
        {
            ReactorApplication.OnUnhandledException = e => { seen = e; return true; };

            RenderErrorDispatch.RaiseUnhandled(ex);

            Assert.Same(ex, seen);
            Assert.True(RenderErrorDispatch.WasReportedAsUnhandled(ex));
            Assert.False(RenderErrorDispatch.IsPropagating(ex));
        }
        finally
        {
            ReactorApplication.OnUnhandledException = previous;
        }
    }

    [Fact]
    public void RaiseUnhandled_Rethrows_The_Same_Exception_Marked_As_Propagating_When_Unhandled()
    {
        var previous = ReactorApplication.OnUnhandledException;
        var ex = new InvalidOperationException("unhandled");
        try
        {
            ReactorApplication.OnUnhandledException = _ => false;

            var thrown = Assert.Throws<InvalidOperationException>(() => RenderErrorDispatch.RaiseUnhandled(ex));

            Assert.Same(ex, thrown);
            Assert.True(RenderErrorDispatch.IsPropagating(ex));
            Assert.True(RenderErrorDispatch.WasReportedAsUnhandled(ex));
        }
        finally
        {
            ReactorApplication.OnUnhandledException = previous;
        }
    }

    [Fact]
    public void RaiseUnhandled_With_No_App_Callback_Rethrows()
    {
        var previous = ReactorApplication.OnUnhandledException;
        var ex = new InvalidOperationException("no callback");
        try
        {
            ReactorApplication.OnUnhandledException = null;
            Assert.Same(ex, Assert.Throws<InvalidOperationException>(() => RenderErrorDispatch.RaiseUnhandled(ex)));
        }
        finally
        {
            ReactorApplication.OnUnhandledException = previous;
        }
    }

    [Fact]
    public void ReportCleanup_Without_A_Handler_Tells_The_Caller_To_Rethrow()
    {
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pending = null;

        bool reported = RenderErrorDispatch.ReportCleanup(null, new InvalidOperationException("c"), "Probe",
            isHostLevel: true, logger: null, ref pending);

        Assert.False(reported);
        Assert.Null(pending);
    }

    [Fact]
    public void ReportCleanup_Notifies_The_Handler_With_Source_Cleanup_And_Ignores_Its_Return()
    {
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pending = null;
        var ex = new InvalidOperationException("cleanup");
        RenderError? seen = null;

        bool reported = RenderErrorDispatch.ReportCleanup(e => { seen = e; return new ProbeElement("ignored"); },
            ex, "Probe", isHostLevel: false, logger: null, ref pending);

        Assert.True(reported);
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
        var previous = ReactorApplication.OnUnhandledException;
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pending = null;
        var ex = new InvalidOperationException("cleanup");
        try
        {
            ReactorApplication.OnUnhandledException = _ => false;

            bool reported = RenderErrorDispatch.ReportCleanup(e => { e.Propagate(); return null; },
                ex, "Probe", isHostLevel: true, logger: null, ref pending);

            Assert.True(reported);
            Assert.NotNull(pending);
            Assert.Same(ex, pending!.SourceException);
            Assert.True(RenderErrorDispatch.IsPropagating(ex));
        }
        finally
        {
            ReactorApplication.OnUnhandledException = previous;
        }
    }

    [Fact]
    public void ReportCleanup_Handled_Propagation_Leaves_Nothing_Pending()
    {
        var previous = ReactorApplication.OnUnhandledException;
        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo? pending = null;
        try
        {
            ReactorApplication.OnUnhandledException = _ => true;

            RenderErrorDispatch.ReportCleanup(e => { e.Propagate(); return null; },
                new InvalidOperationException("cleanup"), "Probe", isHostLevel: true, logger: null, ref pending);

            Assert.Null(pending);
        }
        finally
        {
            ReactorApplication.OnUnhandledException = previous;
        }
    }

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
