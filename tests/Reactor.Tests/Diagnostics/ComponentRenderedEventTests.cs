using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// <see cref="ReactorEventSource.ComponentRendered"/> (EventId 40) — the wire contract an
/// external inspector binds to by name, and the keyword/level gate that keeps it free
/// for everyone else.
///
/// <para>Each test emits with its own componentName discriminator, because the
/// provider is process-wide and other test classes emit concurrently.</para>
/// </summary>
public sealed class ComponentRenderedEventTests
{
    [Fact]
    public void Emits_EventId40_WithStablePayloadNamesAndValues()
    {
        const string name = "ComponentRenderedEventTests.Payload";
        using var collector = ReactorTraceCollector.Capture(
            EventLevel.Verbose, ReactorEventSource.Keywords.RenderDetail);

        ReactorEventSource.Log.ComponentRendered(name, 4242, ComponentRenderTrace.Reasons.Props, 17);

        var evt = Assert.Single(collector.ByName(nameof(ReactorEventSource.ComponentRendered)),
            e => (e.Payload[0] as string) == name);
        Assert.Equal(40, evt.EventId);
        Assert.Equal(EventLevel.Verbose, evt.Level);
        Assert.Equal(
            new[] { "componentName", "componentId", "reason", "elapsedMicroseconds" },
            evt.PayloadNames);
        Assert.Equal(4242L, evt.Payload[1]);
        Assert.Equal("props", evt.Payload[2]);
        Assert.Equal(17L, evt.Payload[3]);
        Assert.True((evt.Keywords & ReactorEventSource.Keywords.Render) != 0);
        Assert.True((evt.Keywords & ReactorEventSource.Keywords.RenderDetail) != 0);
    }

    [Fact]
    public void NullStrings_AreWrittenAsEmpty()
    {
        using var collector = ReactorTraceCollector.Capture(
            EventLevel.Verbose, ReactorEventSource.Keywords.RenderDetail);

        ReactorEventSource.Log.ComponentRendered(null!, 98765431, null!, 0);

        var evt = Assert.Single(collector.ByName(nameof(ReactorEventSource.ComponentRendered)),
            e => e.Payload[1] is long id && id == 98765431);
        Assert.Equal(string.Empty, evt.Payload[0]);
        Assert.Equal(string.Empty, evt.Payload[2]);
    }

    [Fact]
    public void RenderDetailOnly_DoesNotPayForTheRenderSpans()
    {
        // The point of the dedicated keyword: an overlay that only wants this event does
        // not also switch on ComponentRenderStart/Stop and EffectsFlushStart/Stop.
        const string name = "ComponentRenderedEventTests.DetailOnly";
        using var collector = ReactorTraceCollector.Capture(
            EventLevel.Verbose, ReactorEventSource.Keywords.RenderDetail);

        ReactorEventSource.Log.ComponentRenderStart(name, "self");
        ReactorEventSource.Log.ComponentRendered(name, 1, ComponentRenderTrace.Reasons.State, 1);

        // Positive control first: the same subscription does see the event it asked for.
        Assert.Contains(collector.ByName(nameof(ReactorEventSource.ComponentRendered)),
            e => (e.Payload[0] as string) == name);
        Assert.DoesNotContain(collector.ByName(nameof(ReactorEventSource.ComponentRenderStart)),
            e => (e.Payload[0] as string) == name);
    }

    [Fact]
    public void InformationalRenderCapture_DoesNotReceiveIt()
    {
        // Existing Render@Informational captures (dotnet-trace 0x2:4, the perf docs) must
        // keep their current volume.
        const string name = "ComponentRenderedEventTests.Informational";
        using var collector = ReactorTraceCollector.Capture(
            EventLevel.Informational, ReactorEventSource.Keywords.Render);

        ReactorEventSource.Log.ComponentRenderStart(name, "self");
        ReactorEventSource.Log.ComponentRendered(name, 1, ComponentRenderTrace.Reasons.State, 1);

        Assert.Contains(collector.ByName(nameof(ReactorEventSource.ComponentRenderStart)),
            e => (e.Payload[0] as string) == name);
        Assert.DoesNotContain(collector.ByName(nameof(ReactorEventSource.ComponentRendered)),
            e => (e.Payload[0] as string) == name);
    }

    [Fact]
    public void RootRenderDiagnostics_FirstIsMount_ThenByForce_SameId_ResetStartsOver()
    {
        // The host-root path (ReactorHost / ReactorHostControl, including their
        // render-threw branches) goes through this one helper.
        const string name = "ComponentRenderedEventTests.Root";
        using var collector = ReactorTraceCollector.Capture(
            EventLevel.Verbose, ReactorEventSource.Keywords.RenderDetail);

        var root = new RootRenderDiagnostics();
        Assert.True(root.TraceRendered(name, hotReloadRender: false, forcePending: false, elapsedMilliseconds: 1.5));
        long firstId = root.IdForTests;
        Assert.NotEqual(0, firstId);
        root.TraceRendered(name, hotReloadRender: false, forcePending: true, elapsedMilliseconds: 0);

        root.Reset();   // a new root component was mounted
        Assert.Equal(0, root.IdForTests);
        root.TraceRendered(name, hotReloadRender: false, forcePending: false, elapsedMilliseconds: 0);

        var events = collector.ByName(nameof(ReactorEventSource.ComponentRendered))
            .Where(e => (e.Payload[0] as string) == name)
            .ToList();
        Assert.Equal(new[] { "mount", "forced", "mount" }, events.Select(e => (string)e.Payload[2]!));
        Assert.Equal(firstId, events[0].Payload[1]);
        Assert.Equal(firstId, events[1].Payload[1]);
        Assert.NotEqual(firstId, events[2].Payload[1]);
        Assert.Equal(1500L, events[0].Payload[3]);
    }

    [Fact]
    public void RootRenderDiagnostics_HotReloadRetryIsReportedOnce()
    {
        // ReactorHostControl's hook-order retry is not a forced pass, so its root relies
        // solely on this marker to report the retry as hotReload rather than state.
        const string name = "ComponentRenderedEventTests.RootRetry";
        using var collector = ReactorTraceCollector.Capture(
            EventLevel.Verbose, ReactorEventSource.Keywords.RenderDetail);

        var root = new RootRenderDiagnostics();
        root.TraceRendered(name, hotReloadRender: false, forcePending: false, elapsedMilliseconds: 0);
        root.MarkHotReloadRetry();
        root.TraceRendered(name, hotReloadRender: false, forcePending: false, elapsedMilliseconds: 0);
        root.TraceRendered(name, hotReloadRender: false, forcePending: false, elapsedMilliseconds: 0);

        var reasons = collector.ByName(nameof(ReactorEventSource.ComponentRendered))
            .Where(e => (e.Payload[0] as string) == name)
            .Select(e => (string)e.Payload[2]!);
        Assert.Equal(new[] { "mount", "hotReload", "state" }, reasons);
    }

    [Fact]
    public void EventAttribute_DeclaresVerboseAndBothKeywords()
    {
        var attribute = typeof(ReactorEventSource)
            .GetMethod(nameof(ReactorEventSource.ComponentRendered))!
            .GetCustomAttributes(typeof(EventAttribute), inherit: false)
            .Cast<EventAttribute>()
            .Single();

        Assert.Equal(40, attribute.EventId);
        Assert.Equal(EventLevel.Verbose, attribute.Level);
        Assert.Equal(ReactorEventSource.Keywords.Render | ReactorEventSource.Keywords.RenderDetail, attribute.Keywords);
        Assert.Equal((EventKeywords)0x4000, ReactorEventSource.Keywords.RenderDetail);
    }
}
