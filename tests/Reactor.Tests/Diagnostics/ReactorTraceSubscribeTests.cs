using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Spec 044 Phase D (§5.3) — verifies the in-process
/// <see cref="ReactorTrace.Subscribe"/> helper:
///
/// 1. delivers events with the right shape;
/// 2. detaches on Dispose;
/// 3. tolerates concurrent subscribers independently;
/// 4. swallows subscriber exceptions;
/// 5. respects the keywords filter; and
/// 6. respects the level filter.
/// </summary>
public class ReactorTraceSubscribeTests
{
    [Fact]
    public void Subscribe_delivers_event_with_id_name_level_and_payload()
    {
        var captured = new List<ReactorEvent>();
        const string component = "ReactorTraceSubscribeTests.deliver";

        using (ReactorTrace.Subscribe(
            evt => { lock (captured) captured.Add(evt); },
            EventLevel.Verbose,
            ReactorEventSource.Keywords.Errors))
        {
            ReactorEventSource.Log.RenderError(component, nameof(InvalidOperationException), "ignored-pii");
        }

        ReactorEvent[] hits;
        lock (captured)
            hits = captured.Where(e =>
                e.EventName == nameof(ReactorEventSource.RenderError)
                && (e.Payload.Count > 0 && (e.Payload[0] as string) == component)).ToArray();

        var evt = Assert.Single(hits);
        Assert.Equal(9, evt.EventId);
        Assert.Equal(EventLevel.Error, evt.Level);
        Assert.Equal(component, evt.Payload[0]);
        Assert.Equal(nameof(InvalidOperationException), evt.Payload[1]);
        Assert.NotNull(evt.PayloadNames);
        Assert.Equal(evt.Payload.Count, evt.PayloadNames.Count);
    }

    [Fact]
    public void Dispose_detaches_subscription()
    {
        var captured = new List<ReactorEvent>();
        const string component = "ReactorTraceSubscribeTests.dispose";

        var token = ReactorTrace.Subscribe(
            evt => { lock (captured) captured.Add(evt); },
            EventLevel.Verbose,
            ReactorEventSource.Keywords.Errors);

        ReactorEventSource.Log.RenderError(component, "Boom", string.Empty);

        token.Dispose();

        int countBeforeSecondEmit;
        lock (captured)
            countBeforeSecondEmit = captured.Count(e =>
                e.EventName == nameof(ReactorEventSource.RenderError)
                && (e.Payload.Count > 0 && (e.Payload[0] as string) == component));

        // Emit again after dispose — must not reach the callback.
        ReactorEventSource.Log.RenderError(component, "Boom2", string.Empty);

        int countAfterSecondEmit;
        lock (captured)
            countAfterSecondEmit = captured.Count(e =>
                e.EventName == nameof(ReactorEventSource.RenderError)
                && (e.Payload.Count > 0 && (e.Payload[0] as string) == component));

        Assert.Equal(1, countBeforeSecondEmit);
        Assert.Equal(countBeforeSecondEmit, countAfterSecondEmit);
    }

    [Fact]
    public void Two_concurrent_subscribers_each_see_the_event()
    {
        var a = new List<ReactorEvent>();
        var b = new List<ReactorEvent>();
        const string component = "ReactorTraceSubscribeTests.concurrent";

        using (ReactorTrace.Subscribe(
            evt => { lock (a) a.Add(evt); },
            EventLevel.Verbose,
            ReactorEventSource.Keywords.Errors))
        using (ReactorTrace.Subscribe(
            evt => { lock (b) b.Add(evt); },
            EventLevel.Verbose,
            ReactorEventSource.Keywords.Errors))
        {
            ReactorEventSource.Log.RenderError(component, "Boom", string.Empty);
        }

        Assert.Contains(a, e =>
            e.EventName == nameof(ReactorEventSource.RenderError)
            && (e.Payload[0] as string) == component);
        Assert.Contains(b, e =>
            e.EventName == nameof(ReactorEventSource.RenderError)
            && (e.Payload[0] as string) == component);
    }

    [Fact]
    public void Throwing_subscriber_does_not_break_other_subscribers()
    {
        var good = new List<ReactorEvent>();
        const string component = "ReactorTraceSubscribeTests.throws";

        using (ReactorTrace.Subscribe(
            _ => throw new InvalidOperationException("bad subscriber"),
            EventLevel.Verbose,
            ReactorEventSource.Keywords.Errors))
        using (ReactorTrace.Subscribe(
            evt => { lock (good) good.Add(evt); },
            EventLevel.Verbose,
            ReactorEventSource.Keywords.Errors))
        {
            // Should not throw out to the test even though the first
            // subscriber's callback raises.
            ReactorEventSource.Log.RenderError(component, "Boom", string.Empty);
        }

        Assert.Contains(good, e =>
            e.EventName == nameof(ReactorEventSource.RenderError)
            && (e.Payload[0] as string) == component);
    }

    [Fact]
    public void Subscriber_with_Errors_keyword_does_not_receive_Reconcile_events()
    {
        var captured = new List<ReactorEvent>();
        const string discriminator = "ReactorTraceSubscribeTests.errors-only";

        using (ReactorTrace.Subscribe(
            evt => { lock (captured) captured.Add(evt); },
            EventLevel.Verbose,
            ReactorEventSource.Keywords.Errors))
        {
            // Reconcile keyword — should be filtered out.
            ReactorEventSource.Log.ReconcileStart(discriminator + ".root");
            // Errors keyword — should be delivered.
            ReactorEventSource.Log.SwallowedError(
                "Reactor", discriminator + ".op", "X");
        }

        Assert.DoesNotContain(captured, e =>
            e.EventName == nameof(ReactorEventSource.ReconcileStart)
            && (e.Payload[0] as string) == discriminator + ".root");
        Assert.Contains(captured, e =>
            e.EventName == nameof(ReactorEventSource.SwallowedError)
            && (e.Payload[1] as string) == discriminator + ".op");
    }

    [Fact]
    public void Subscriber_with_Warning_level_does_not_receive_Verbose_events()
    {
        var captured = new List<ReactorEvent>();
        const string discriminator = "ReactorTraceSubscribeTests.warning-only";

        using (ReactorTrace.Subscribe(
            evt => { lock (captured) captured.Add(evt); },
            EventLevel.Warning,
            (EventKeywords)(-1)))
        {
            // Verbose — should be filtered.
            ReactorEventSource.Log.NavigationCacheHit(discriminator + ".route");
            // Warning — should pass.
            ReactorEventSource.Log.IntlMissingKey(discriminator + ".key", "en-US", true);
        }

        Assert.DoesNotContain(captured, e =>
            e.EventName == nameof(ReactorEventSource.NavigationCacheHit)
            && (e.Payload[0] as string) == discriminator + ".route");
        Assert.Contains(captured, e =>
            e.EventName == nameof(ReactorEventSource.IntlMissingKey)
            && (e.Payload[0] as string) == discriminator + ".key");
    }

    [Fact]
    public void Subscribe_throws_on_null_callback()
    {
        Assert.Throws<ArgumentNullException>(
            () => ReactorTrace.Subscribe(null!));
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var token = ReactorTrace.Subscribe(_ => { });
        token.Dispose();
        // Calling Dispose a second time must not throw.
        token.Dispose();
    }
}
