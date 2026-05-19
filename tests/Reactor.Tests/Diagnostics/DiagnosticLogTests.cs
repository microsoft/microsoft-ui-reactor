using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Spec 044 §6.1 + §1.4 — verifies the DiagnosticLog helper routes
/// swallowed exceptions and HRESULTs to the SwallowedError / HResultFailed
/// events on Microsoft-UI-Reactor with the right payload shape and PII
/// discipline.
///
/// The listener is created per-test (constructor) and disposed in
/// <see cref="Dispose"/>. ReactorEventSource.Log is process-wide; other
/// test classes can emit while a fixture runs, so payload assertions
/// filter by EventName + a per-test discriminator (the operation string).
/// </summary>
public class DiagnosticLogTests : IDisposable
{
    private sealed class CapturingListener : EventListener
    {
        private readonly List<EventWrittenEventArgs> _events = new();

        public IReadOnlyList<EventWrittenEventArgs> Events
        {
            get { lock (_events) return _events.ToArray(); }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            lock (_events) _events.Add(eventData);
        }
    }

    private readonly CapturingListener _listener = new();

    public DiagnosticLogTests()
    {
        _listener.EnableEvents(
            ReactorEventSource.Log,
            EventLevel.Warning,
            ReactorEventSource.Keywords.Errors);
    }

    public void Dispose()
    {
        _listener.DisableEvents(ReactorEventSource.Log);
        _listener.Dispose();
    }

    [Fact]
    public void SwallowedError_writes_exception_type_to_payload_but_not_message()
    {
        const string operation = "DiagnosticLogTests.AppWindow.Close";
        const string secret = "absolute/path/to/user/secret.json";
        var ex = new InvalidOperationException(secret);

        DiagnosticLog.SwallowedError(LogCategory.Hosting, operation, ex);

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.SwallowedError)
            && string.Equals(e.Payload?[1] as string, operation, StringComparison.Ordinal));

        Assert.Equal((int)EventLevel.Warning, (int)evt.Level);
        // ETW reserved bits (0xF000000000000000) can ride along on
        // EventWrittenEventArgs.Keywords, so test the Errors bit by mask
        // rather than equality.
        Assert.True((evt.Keywords & ReactorEventSource.Keywords.Errors) == ReactorEventSource.Keywords.Errors);
        Assert.Equal(LogCategory.Hosting.ToString(), evt.Payload?[0]);
        Assert.Equal(operation, evt.Payload?[1]);
        Assert.Equal(nameof(InvalidOperationException), evt.Payload?[2]);

        // PII: the raw exception message must never reach the payload.
        foreach (var p in evt.Payload!)
            Assert.NotEqual(secret, p as string);
    }

    [Fact]
    public void HResultFailed_writes_hr_as_int_and_category_as_enum_tostring()
    {
        const string operation = "DiagnosticLogTests.JumpList.BeginList";

        DiagnosticLog.HResultFailed(LogCategory.Shell, operation, HResults.RPC_E_DISCONNECTED);

        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.HResultFailed)
            && string.Equals(e.Payload?[1] as string, operation, StringComparison.Ordinal));

        Assert.Equal((int)EventLevel.Warning, (int)evt.Level);
        Assert.Equal(LogCategory.Shell.ToString(), evt.Payload?[0]);
        Assert.Equal(operation, evt.Payload?[1]);
        Assert.Equal(HResults.RPC_E_DISCONNECTED, evt.Payload?[2]);
    }

    [Fact]
    public void Helpers_tolerate_null_operation_and_null_exception_without_throwing()
    {
        // Defensive: callers should pass real values, but null must not
        // crash the framework on its way through a swallowed catch site.
        DiagnosticLog.SwallowedError(LogCategory.Reactor, null!, null!);
        DiagnosticLog.HResultFailed(LogCategory.Reactor, null!, 0);
        // No assertion beyond "didn't throw" — the test simply must complete.
    }

    [Fact]
    public void Helpers_are_no_ops_on_payload_when_Errors_keyword_is_disabled()
    {
        const string operation = "DiagnosticLogTests.no-op-when-disabled";

        using var silent = new CapturingListener();
        // Enable a different keyword so the listener is "live" but Errors
        // is masked out — DiagnosticLog must not push anything into it.
        silent.EnableEvents(
            ReactorEventSource.Log,
            EventLevel.Verbose,
            ReactorEventSource.Keywords.Reconcile);

        try
        {
            DiagnosticLog.SwallowedError(LogCategory.Reactor, operation, new Exception("nope"));
            DiagnosticLog.HResultFailed(LogCategory.Reactor, operation, unchecked((int)0x80004005));
        }
        finally
        {
            silent.DisableEvents(ReactorEventSource.Log);
        }

        Assert.DoesNotContain(silent.Events, e =>
            (e.EventName == nameof(ReactorEventSource.SwallowedError)
             || e.EventName == nameof(ReactorEventSource.HResultFailed))
            && string.Equals(e.Payload?[1] as string, operation, StringComparison.Ordinal));
    }
}
