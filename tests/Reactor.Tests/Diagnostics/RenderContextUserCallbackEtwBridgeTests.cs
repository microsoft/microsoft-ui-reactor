using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Spec 044 Phase C §4.9 — regression guard that the user-callback
/// isolation sites in <see cref="RenderContext"/> (effect bodies, effect
/// cleanups, persisted-state save) route their swallowed exceptions
/// through <c>DiagnosticLog.SwallowedError(LogCategory.Reactor, ...)</c>
/// rather than the old <c>Debug.WriteLine</c> channel.
///
/// The broad <c>catch (Exception)</c> stays per §6.7.3 — a thrown user
/// effect / cleanup must not block the remaining slots from running. The
/// asserts here pin both the isolation behavior (sibling slots still run)
/// and the trace shape (stable operation labels and category).
/// </summary>
public class RenderContextUserCallbackEtwBridgeTests : IDisposable
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

    public RenderContextUserCallbackEtwBridgeTests()
    {
        _listener.EnableEvents(
            ReactorEventSource.Log,
            EventLevel.Verbose,
            ReactorEventSource.Keywords.Errors);
    }

    public void Dispose()
    {
        _listener.DisableEvents(ReactorEventSource.Log);
        _listener.Dispose();
    }

    private static EventWrittenEventArgs? FindSwallowedError(
        IReadOnlyList<EventWrittenEventArgs> events, string operation)
        => events.FirstOrDefault(e =>
            e.EventName == nameof(ReactorEventSource.SwallowedError)
            && (e.Payload?[1] as string) == operation);

    [Fact]
    public void Effect_body_throw_emits_SwallowedError_with_indexed_operation()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        ctx.UseEffect(() => throw new InvalidOperationException("boom"), "dep");

        ctx.FlushEffects();

        var evt = FindSwallowedError(_listener.Events, "UseEffect.effect[i=0]");
        Assert.NotNull(evt);
        Assert.Equal(nameof(LogCategory.Reactor), evt!.Payload?[0]);
        Assert.Equal(nameof(InvalidOperationException), evt.Payload?[2]);
        // PII: ex.Message must never reach the ETW payload.
        Assert.DoesNotContain("boom",
            string.Join("|", evt.Payload?.OfType<string>() ?? Array.Empty<string>()));
    }

    [Fact]
    public void Effect_cleanup_throw_during_flush_emits_SwallowedError_with_indexed_operation()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        ctx.UseEffect(() => () => throw new InvalidOperationException("cleanup-boom"), "dep-v1");
        ctx.FlushEffects();

        // Re-render with a new dep key so the cleanup queues for the next flush.
        ctx.BeginRender(() => { });
        ctx.UseEffect(() => { }, "dep-v2");
        ctx.FlushEffects();

        var evt = FindSwallowedError(_listener.Events, "UseEffect.cleanup[i=0]");
        Assert.NotNull(evt);
        Assert.Equal(nameof(LogCategory.Reactor), evt!.Payload?[0]);
        Assert.Equal(nameof(InvalidOperationException), evt.Payload?[2]);
    }

    [Fact]
    public void Cleanup_throw_during_RunCleanups_emits_SwallowedError_with_indexed_operation()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        ctx.UseEffect(() => () => throw new InvalidOperationException("teardown-boom"), "dep");
        ctx.FlushEffects();

        ctx.RunCleanups();

        var evt = FindSwallowedError(_listener.Events, "RunCleanups.effectCleanup[i=0]");
        Assert.NotNull(evt);
        Assert.Equal(nameof(LogCategory.Reactor), evt!.Payload?[0]);
        Assert.Equal(nameof(InvalidOperationException), evt.Payload?[2]);
    }

    [Fact]
    public void Effect_throw_does_not_block_subsequent_effects_on_same_flush()
    {
        var ctx = new RenderContext();
        bool secondEffectRan = false;

        ctx.BeginRender(() => { });
        ctx.UseEffect(() => throw new InvalidOperationException("boom"), "dep0");
        ctx.UseEffect(() => { secondEffectRan = true; }, "dep1");

        ctx.FlushEffects();

        Assert.True(secondEffectRan, "Sibling effect must run despite the first one throwing.");
        Assert.NotNull(FindSwallowedError(_listener.Events, "UseEffect.effect[i=0]"));
    }
}
