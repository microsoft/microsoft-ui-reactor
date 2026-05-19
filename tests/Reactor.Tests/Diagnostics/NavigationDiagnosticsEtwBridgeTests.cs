using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Navigation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Spec 044 Phase C §4.2 — regression guard that the internal
/// <see cref="NavigationDiagnostics"/> entry points emit the
/// corresponding typed events on <see cref="ReactorEventSource"/>
/// (Navigation keyword). Replaces the previous <c>Debug.WriteLine</c>
/// channel with a Release-visible ETW stream.
///
/// Also guards spec §6.2.1: <c>OnDeepLinkResolved</c> must NOT put the
/// raw input path on the ETW payload — only the resolution outcome.
/// </summary>
public class NavigationDiagnosticsEtwBridgeTests : IDisposable
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

    public NavigationDiagnosticsEtwBridgeTests()
    {
        _listener.EnableEvents(
            ReactorEventSource.Log,
            EventLevel.Verbose,
            ReactorEventSource.Keywords.Navigation);
    }

    public void Dispose()
    {
        _listener.DisableEvents(ReactorEventSource.Log);
        _listener.Dispose();
    }

    // The bridge derives the route "template" from the destination route's
    // CLR type. A per-test discriminator class therefore yields a unique
    // payload string, so concurrent NavigationDiagnostics traffic from
    // other tests can't false-positive these assertions.
    private sealed class RouteFor_NavigationRequested { }
    private sealed class RouteFor_NavigationCompleted { }
    private sealed class RouteFor_NavigationCancelled { }
    private sealed class RouteFor_CacheHit { }
    private sealed class RouteFor_CacheMiss { }
    private sealed class RouteFor_CacheEvict { }

    // Per-test transition subclass so the captured event payload's
    // transition-type name is unambiguously this test's.
    private sealed record DiscriminatorTransition : NavigationTransition;

    [Fact]
    public void OnNavigationRequested_emits_typed_event_with_route_template()
    {
        NavigationDiagnostics.OnNavigationRequested(
            from: new object(),
            to: new RouteFor_NavigationRequested(),
            mode: NavigationMode.Push);

        var template = typeof(RouteFor_NavigationRequested).FullName!;
        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationRequested)
            && (e.Payload?[0] as string) == template);
        Assert.Equal((int)EventLevel.Informational, (int)evt.Level);
    }

    [Fact]
    public void OnNavigationCompleted_emits_typed_event_with_route_template()
    {
        NavigationDiagnostics.OnNavigationCompleted(
            from: new object(),
            to: new RouteFor_NavigationCompleted(),
            mode: NavigationMode.Pop);

        var template = typeof(RouteFor_NavigationCompleted).FullName!;
        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationCompleted)
            && (e.Payload?[0] as string) == template);
        // durationMs payload slot is reserved; the bridge passes 0 until
        // timing is wired through (see NavigationDiagnostics comment).
        Assert.Equal(0d, evt.Payload?[1]);
    }

    [Fact]
    public void OnNavigationCancelled_emits_typed_event_with_reason()
    {
        NavigationDiagnostics.OnNavigationCancelled(
            from: new object(),
            to: new RouteFor_NavigationCancelled(),
            mode: NavigationMode.Push,
            reason: "guard-blocked");

        var template = typeof(RouteFor_NavigationCancelled).FullName!;
        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationCancelled)
            && (e.Payload?[0] as string) == template);
        Assert.Equal("guard-blocked", evt.Payload?[1]);
    }

    [Fact]
    public void OnCache_hit_miss_evict_emit_at_verbose_level()
    {
        NavigationDiagnostics.OnCacheHit(new RouteFor_CacheHit());
        NavigationDiagnostics.OnCacheMiss(new RouteFor_CacheMiss());
        NavigationDiagnostics.OnCacheEviction(new RouteFor_CacheEvict());

        var hit = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationCacheHit)
            && (e.Payload?[0] as string) == typeof(RouteFor_CacheHit).FullName);
        Assert.Equal((int)EventLevel.Verbose, (int)hit.Level);

        var miss = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationCacheMiss)
            && (e.Payload?[0] as string) == typeof(RouteFor_CacheMiss).FullName);
        Assert.Equal((int)EventLevel.Verbose, (int)miss.Level);

        var evict = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationCacheEvict)
            && (e.Payload?[0] as string) == typeof(RouteFor_CacheEvict).FullName);
        Assert.Equal((int)EventLevel.Verbose, (int)evict.Level);
    }

    [Fact]
    public void OnTransitionStarted_and_Completed_emit_type_and_mode()
    {
        var transition = new DiscriminatorTransition();
        NavigationDiagnostics.OnTransitionStarted(transition, NavigationMode.Push);
        NavigationDiagnostics.OnTransitionCompleted(transition, NavigationMode.Pop);

        var started = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationTransitionStarted)
            && (e.Payload?[0] as string) == nameof(DiscriminatorTransition));
        Assert.Equal(nameof(NavigationMode.Push), started.Payload?[1]);

        var completed = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationTransitionCompleted)
            && (e.Payload?[0] as string) == nameof(DiscriminatorTransition));
        Assert.Equal(nameof(NavigationMode.Pop), completed.Payload?[1]);
    }

    [Fact]
    public void OnDeepLinkResolved_match_emits_outcome_only_no_path()
    {
        // PII guard (spec §6.2.1): the path is attacker-controllable input
        // and must NOT appear in the ETW payload.
        const string secretPath = "/deep-link-secret-token-9d3b2f1c";
        NavigationDiagnostics.OnDeepLinkResolved(secretPath, matched: true, routeCount: 3);

        // The matched-true / routeCount=3 combination is unique enough in
        // this listener (this test owns the keyword filter) to disambiguate.
        var evt = Assert.Single(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationDeepLinkResolved)
            && Equals(e.Payload?[0], true)
            && Equals(e.Payload?[1], 3));

        Assert.Equal((int)EventLevel.Informational, (int)evt.Level);
        Assert.DoesNotContain(evt.Payload!, p => p is string s && s.Contains("secret-token", StringComparison.Ordinal));
        if (evt.PayloadNames is { } names)
        {
            Assert.DoesNotContain(names, n => n.Equals("path", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void OnDeepLinkResolved_miss_emits_false_and_zero_routes()
    {
        NavigationDiagnostics.OnDeepLinkResolved("/nope", matched: false, routeCount: 0);

        Assert.Contains(_listener.Events, e =>
            e.EventName == nameof(ReactorEventSource.NavigationDeepLinkResolved)
            && Equals(e.Payload?[0], false)
            && Equals(e.Payload?[1], 0));
    }
}
