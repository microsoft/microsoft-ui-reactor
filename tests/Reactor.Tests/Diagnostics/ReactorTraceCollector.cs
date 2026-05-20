using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Spec 044 Phase I — test-only collector that wraps
/// <see cref="ReactorTrace.Subscribe"/> with a thread-safe captured-events
/// list. Lives in the test assembly because production code should never
/// hold a hot list of every event (memory + dispatcher cost).
///
/// <para>
/// Typical use:
/// <code>
/// using var collector = ReactorTraceCollector.Capture(
///     level: EventLevel.Verbose,
///     keywords: ReactorEventSource.Keywords.Reconcile);
/// // … exercise framework …
/// Assert.Collection(collector.Events, …);
/// </code>
/// </para>
///
/// <para>
/// Disposing detaches the underlying <see cref="EventListener"/>; subsequent
/// events do not append to <see cref="Events"/>, so later tests in the same
/// process cannot leak state through a left-over subscription.
/// </para>
/// </summary>
internal sealed class ReactorTraceCollector : IDisposable
{
    private readonly List<ReactorEvent> _events = new();
    private readonly object _gate = new();
    private readonly IDisposable _subscription;
    private int _disposed;

    private ReactorTraceCollector(EventLevel level, EventKeywords keywords)
    {
        _subscription = ReactorTrace.Subscribe(OnEvent, level, keywords);
    }

    /// <summary>
    /// Starts capturing. Defaults match <see cref="ReactorTrace.Subscribe"/>:
    /// <see cref="EventLevel.Verbose"/> and all keywords. Tests that only
    /// care about one subsystem should narrow the filter — it keeps the
    /// captured list shorter and avoids interleaving with concurrent
    /// unrelated activity.
    /// </summary>
    public static ReactorTraceCollector Capture(
        EventLevel level = EventLevel.Verbose,
        EventKeywords keywords = (EventKeywords)(-1))
        => new(level, keywords);

    /// <summary>
    /// Immutable snapshot of events captured so far, in arrival order.
    /// Safe to enumerate while the framework continues to emit because
    /// each access returns a fresh array.
    /// </summary>
    public IReadOnlyList<ReactorEvent> Events
    {
        get
        {
            lock (_gate) return _events.ToArray();
        }
    }

    /// <summary>
    /// Convenience: every captured event whose <c>EventName</c> matches
    /// <paramref name="name"/>.
    /// </summary>
    public IReadOnlyList<ReactorEvent> ByName(string name)
    {
        lock (_gate)
            return _events.Where(e => e.EventName == name).ToArray();
    }

    private void OnEvent(ReactorEvent e)
    {
        // ReactorTrace already wraps the callback in try/catch so a buggy
        // sink doesn't break other subscribers — keep this hot path as
        // small as possible.
        lock (_gate) _events.Add(e);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _subscription.Dispose();
    }
}
