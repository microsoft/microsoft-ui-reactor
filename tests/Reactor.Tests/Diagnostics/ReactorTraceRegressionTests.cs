using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Spec 044 Phase I (§8.2) — regression guards that pin the contracts a
/// future change is most likely to break by accident:
///
/// <list type="bullet">
///   <item>Reconcile fires a Start/Stop pair under the Reconcile keyword.</item>
///   <item>RenderError carries the exception type but never the raw
///   <see cref="Exception.Message"/>.</item>
///   <item>Every <see cref="LogCategory"/> routes through
///   <c>DiagnosticLog.SwallowedError</c> to a captured
///   <c>SwallowedError</c> event.</item>
///   <item>The MCP selector is hashed before it ever reaches the trace.</item>
///   <item>When a keyword is disabled, the framework-side helper allocates
///   nothing on the hot path.</item>
/// </list>
/// </summary>
public class ReactorTraceRegressionTests
{
    [Fact]
    public void Reconcile_emits_start_stop_pair()
    {
        using var collector = ReactorTraceCollector.Capture(
            level: EventLevel.Informational,
            keywords: ReactorEventSource.Keywords.Reconcile);

        ReactorEventSource.Log.ReconcileStart("PhaseITestRoot");
        ReactorEventSource.Log.ReconcileStop(
            elementsDiffed: 3, elementsSkipped: 1,
            uiElementsCreated: 2, uiElementsModified: 1);

        var start = Assert.Single(
            collector.ByName(nameof(ReactorEventSource.ReconcileStart)),
            e => (e.Payload[0] as string) == "PhaseITestRoot");
        var stop = Assert.Single(
            collector.ByName(nameof(ReactorEventSource.ReconcileStop)),
            e => (int)(e.Payload[0] ?? -1) == 3);

        Assert.Equal(EventLevel.Informational, start.Level);
        Assert.Equal(EventLevel.Informational, stop.Level);
        Assert.True((start.Keywords & ReactorEventSource.Keywords.Reconcile) != 0);
        Assert.True((stop.Keywords & ReactorEventSource.Keywords.Reconcile) != 0);
    }

    [Fact]
    public void RenderError_carries_exception_type_but_not_message()
    {
        using var collector = ReactorTraceCollector.Capture(
            level: EventLevel.Error,
            keywords: ReactorEventSource.Keywords.Errors);

        // Pass a message that would be obviously fingerprintable if it
        // ever leaked — if the assertion below ever flips, the leak shows
        // up at the failure site without scanning for a salted token.
        const string secret = "DO_NOT_PERSIST_TO_TRACE_phase-i-secret";
        ReactorEventSource.Log.RenderError(
            componentName: "PhaseITestComponent",
            exceptionType: nameof(InvalidOperationException),
            message: secret);

        var hit = Assert.Single(
            collector.ByName(nameof(ReactorEventSource.RenderError)),
            e => (e.Payload[0] as string) == "PhaseITestComponent");

        Assert.Equal(nameof(InvalidOperationException), hit.Payload[1]);
        // Third payload slot exists for ABI stability but is stripped at
        // the emit site (security TASK-064 / spec §6.2.1).
        Assert.Equal(string.Empty, hit.Payload[2]);
        Assert.DoesNotContain(secret, string.Join("|", hit.Payload.Select(p => p?.ToString() ?? "")), StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> EveryLogCategory()
    {
        // Walks the LogCategory enum so a new category added later
        // automatically gets covered by this regression test; if a new
        // entry breaks (e.g. someone added it without wiring the
        // ToString shape) we want the failure here, not in a customer
        // report.
        foreach (var name in Enum.GetNames<LogCategory>())
            yield return new object[] { name };
    }

    [Theory]
    [MemberData(nameof(EveryLogCategory))]
    public void SwallowedError_smoke_for_each_log_category(string categoryName)
    {
        var category = Enum.Parse<LogCategory>(categoryName);

        using var collector = ReactorTraceCollector.Capture(
            level: EventLevel.Warning,
            keywords: ReactorEventSource.Keywords.Errors);

        var operation = $"PhaseI.{categoryName}.smoke";
        DiagnosticLog.SwallowedError(category, operation, new InvalidOperationException("ignored-pii"));

        var hit = Assert.Single(
            collector.ByName(nameof(ReactorEventSource.SwallowedError)),
            e => (e.Payload[1] as string) == operation);

        Assert.Equal(categoryName, hit.Payload[0]);
        Assert.Equal(nameof(InvalidOperationException), hit.Payload[2]);
    }

    [Fact]
    public void Mcp_selector_is_hashed_not_emitted_raw()
    {
        using var collector = ReactorTraceCollector.Capture(
            level: EventLevel.Informational,
            keywords: ReactorEventSource.Keywords.Mcp);

        // A selector that would be unambiguously identifying if it leaked.
        const string sensitiveSelector = "[Text*='alice@contoso.example.invalid']";
        ReactorEventSource.Log.McpCallStart("test.tool", sensitiveSelector);

        var hit = Assert.Single(
            collector.ByName(nameof(ReactorEventSource.McpCallStart)),
            e => (e.Payload[0] as string) == "test.tool");

        var fingerprint = Assert.IsType<string>(hit.Payload[1]);
        Assert.StartsWith("sha1:", fingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", fingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("@", fingerprint, StringComparison.Ordinal);
        // A SHA-1 prefix of 8 bytes hex-encodes to 16 chars; "sha1:" + 16.
        Assert.Equal("sha1:".Length + 16, fingerprint.Length);
    }

    [Fact]
    public void DisabledKeyword_skips_ReactorEventSource_WriteEvent_payload_marshal()
    {
        // The hot path we care about for cost-of-disabled is the
        // EventSource-side gate: when no listener has Keywords.Errors at
        // Warning, ReactorEventSource.Log.SwallowedError must return
        // immediately, with no WriteEvent payload marshalling.
        //
        // We can't *force* the runtime keyword bit off mid-test (every
        // running EventListener contributes its mask), so we approximate
        // by capturing on an unrelated keyword (Reconcile). If
        // Keywords.Errors actually became enabled by some background
        // listener, the sanity check at the end would fire.
        //
        // Why not measure DiagnosticLog.SwallowedError directly: the
        // public helper additionally runs a `[Conditional("DEBUG")]`
        // mirror that emits a string-interpolated `Debug.WriteLine` so
        // contributors see the failure in their Output window. That
        // mirror is deliberate cost in DEBUG and is compiled out in
        // Release; measuring it would make this test config-dependent.
        // The "cost-of-disabled" contract that matters in Release
        // (where customers run) is the EventSource gate, exercised
        // below.
        using var unrelated = ReactorTraceCollector.Capture(
            level: EventLevel.Verbose,
            keywords: ReactorEventSource.Keywords.Reconcile);

        // Precondition: confirm Errors really IS disabled inside this
        // measurement window. xunit can run other test classes in
        // parallel and one of them might hold its own EventListener with
        // Keywords.Errors open — the runtime unions every active
        // listener's mask, so a remote test's collector keeps our gate
        // hot. If that race fires, the alloc cap is meaningless: skip
        // the measurement rather than emit a flaky failure.
        if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Errors))
        {
            // The gate is meaningfully tested whenever it IS disabled —
            // some other test run in this process will hit that path.
            // Returning early here is honest about the precondition.
            return;
        }

        // Warm up — JIT first call, allow the runtime to settle so the
        // measurement window only captures the steady state.
        for (int i = 0; i < 100; i++)
            ReactorEventSource.Log.SwallowedError("Reactor", "PhaseI.NoAlloc.warmup", "InvalidOperationException");

        var before = GC.GetAllocatedBytesForCurrentThread();
        const int iterations = 10_000;
        for (int i = 0; i < iterations; i++)
            ReactorEventSource.Log.SwallowedError("Reactor", "PhaseI.NoAlloc", "InvalidOperationException");
        var delta = GC.GetAllocatedBytesForCurrentThread() - before;

        // The gated path SHOULD be zero. Allow up to one byte per
        // iteration for transient runtime bookkeeping (boxed enums in
        // EventListener admin, etc.). A regression that re-introduces
        // WriteEvent argument marshalling outside the gate would burn
        // dozens of bytes per call and blow well past this cap.
        Assert.True(delta <= iterations,
            $"ReactorEventSource.SwallowedError leaked {delta} B over {iterations} disabled-keyword calls (cap: {iterations}).");

        // Sanity check: nothing leaked into the unrelated keyword stream
        // (would mean Errors became enabled mid-test, invalidating this
        // measurement).
        Assert.Empty(unrelated.ByName(nameof(ReactorEventSource.SwallowedError)));
    }
}
