using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Diagnostics;

/// <summary>
/// In-process subscription helper for the <c>Microsoft-UI-Reactor</c>
/// <see cref="EventSource"/>. Lets an app developer (or an internal
/// devtools surface — <c>reactor.logs</c> MCP, an <c>ILogger</c> bridge)
/// observe framework events without writing an <see cref="EventListener"/>
/// subclass.
///
/// <para>
/// This is deliberately <b>not</b> a file-capture API. The .NET runtime
/// already provides three:
/// </para>
/// <list type="bullet">
///   <item><c>DOTNET_EnableEventPipe</c> + <c>DOTNET_EventPipeOutputPath</c>
///   environment variables (zero app code).</item>
///   <item><c>dotnet-trace collect --providers Microsoft-UI-Reactor</c>.</item>
///   <item>Visual Studio Performance Profiler → Events Viewer.</item>
/// </list>
/// <para>
/// See <c>docs/guide/diagnostics.md</c> for end-to-end examples.
/// </para>
/// </summary>
public static class ReactorTrace
{
    /// <summary>
    /// Subscribes to <c>Microsoft-UI-Reactor</c> events in-process.
    /// Returns an <see cref="IDisposable"/> token; dispose it to detach.
    ///
    /// <para>
    /// The callback fires on the emission thread — typically the UI
    /// dispatcher when an event originates from reconcile / render, or a
    /// thread-pool thread otherwise. Keep the callback work minimal and
    /// never let it throw across the framework boundary; the
    /// implementation wraps every invocation in a <c>try/catch</c> so a
    /// buggy subscriber cannot deadlock the dispatcher or break other
    /// subscribers, but exceptions are silently dropped (a faulty sink
    /// should not turn into an ETW failure mode).
    /// </para>
    ///
    /// <para>
    /// Multiple concurrent subscribers are supported. Each subscriber's
    /// <paramref name="level"/> and <paramref name="keywords"/> are
    /// independently active until the returned token is disposed; the
    /// runtime unions them when computing whether <c>IsEnabled</c> on the
    /// event source returns true. Be aware that a broad subscriber
    /// (<see cref="EventLevel.Verbose"/> + all keywords) raises the cost
    /// of every hot-path call site on the framework for as long as it
    /// lives.
    /// </para>
    ///
    /// <para>
    /// For writing a trace file, use one of:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>DOTNET_EnableEventPipe</c> / <c>DOTNET_EventPipeOutputPath</c>
    ///   env vars.</item>
    ///   <item><c>dotnet-trace collect --providers Microsoft-UI-Reactor</c>.</item>
    ///   <item>Visual Studio Performance Profiler → Events Viewer.</item>
    /// </list>
    /// </summary>
    /// <param name="onEvent">Callback invoked once per matching event.
    /// Must be non-null.</param>
    /// <param name="level">Minimum severity to forward. Defaults to
    /// <see cref="EventLevel.Verbose"/> so subscribers see everything
    /// (state changes, per-trampoline dispatches) unless they explicitly
    /// pass a stricter level.</param>
    /// <param name="keywords">Keyword mask to forward. Defaults to all
    /// keywords (<c>(EventKeywords)(-1)</c>).</param>
    /// <returns>Disposable token. Disposing detaches the underlying
    /// <see cref="EventListener"/>; subsequent events do not reach
    /// <paramref name="onEvent"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="onEvent"/>
    /// is <c>null</c>.</exception>
    // <snippet:subscribe-shape>
    public static IDisposable Subscribe(
        Action<ReactorEvent> onEvent,
        EventLevel level = EventLevel.Verbose,
        EventKeywords keywords = (EventKeywords)(-1))
    {
        ArgumentNullException.ThrowIfNull(onEvent);
        return new Subscription(onEvent, level, keywords);
    }
    // </snippet:subscribe-shape>

    private sealed class Subscription : EventListener
    {
        // Configured target. Captured at construction so OnEventWritten
        // can short-circuit on level/keywords without re-reading the
        // EventListener's session-level filter (which the runtime may
        // expose as a different mask than what we requested).
        private readonly Action<ReactorEvent> _callback;
        private readonly EventLevel _level;
        private readonly EventKeywords _keywords;

        // The underlying EventSource we listen to. EventListener delivers
        // OnEventSourceCreated for every existing source at construction
        // time and for any source created later; capture our reference so
        // Dispose can deterministically call DisableEvents.
        private EventSource? _source;
        private int _disposed;

        public Subscription(Action<ReactorEvent> callback, EventLevel level, EventKeywords keywords)
        {
            _callback = callback;
            _level = level;
            _keywords = keywords;
            _initialized = true;

            // The base EventListener ctor calls OnEventSourceCreated for
            // every existing source *before* the derived ctor body runs,
            // so _level/_keywords are default(0) at that point. We capture
            // the source reference there but defer EnableEvents until here,
            // where the fields are properly assigned.
            if (_source is not null)
            {
                // OnEventSourceCreated already captured the source but
                // called EnableEvents with default filters — re-enable
                // with the correct level/keywords now.
                EnableEvents(_source, _level, _keywords);
            }
            else
            {
                // Belt-and-braces: if we somehow missed it (e.g., this
                // listener construction races with first touch of
                // ReactorEventSource), enable explicitly.
                EnableEvents(ReactorEventSource.Log, _level, _keywords);
                _source = ReactorEventSource.Log;
            }
        }

        private volatile bool _initialized;

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Microsoft-UI-Reactor")
            {
                _source = eventSource;

                // If the derived ctor has already run (late source
                // creation), enable immediately with correct filters.
                // Otherwise, just capture the reference — the ctor will
                // call EnableEvents once _level/_keywords are assigned.
                if (_initialized)
                {
                    EnableEvents(eventSource, _level, _keywords);
                }
                else
                {
                    // Enable with permissive defaults so we don't miss
                    // events between base-ctor and derived-ctor; the
                    // derived ctor will re-enable with correct filters.
                    EnableEvents(eventSource, EventLevel.LogAlways, (EventKeywords)(-1));
                }
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            // Defense in depth: even though the runtime gates events by
            // (level, keywords) on the EventListener, double-check here
            // so a runtime that delivers a too-verbose event (or a high-
            // bit ETW reserved keyword) doesn't blow past our contract.
            if ((int)eventData.Level > (int)_level) return;
            if (_keywords != (EventKeywords)(-1)
                && (eventData.Keywords & _keywords) == 0
                && eventData.Keywords != 0)
            {
                return;
            }

            ReactorEvent reactorEvent;
            try
            {
                reactorEvent = new ReactorEvent(
                    EventId: eventData.EventId,
                    EventName: eventData.EventName ?? string.Empty,
                    Level: eventData.Level,
                    Keywords: eventData.Keywords,
                    TimestampUtc: DateTime.UtcNow,
                    ThreadId: Environment.CurrentManagedThreadId,
                    Payload: (IReadOnlyList<object?>?)eventData.Payload ?? Array.Empty<object?>(),
                    PayloadNames: (IReadOnlyList<string>?)eventData.PayloadNames ?? Array.Empty<string>());
            }
            catch
            {
                // Defensive: don't let a malformed event break the
                // EventSource.WriteEvent path. The framework's own emit
                // sites are well-behaved, but custom EventSource methods
                // added later could conceivably push a null Payload.
                return;
            }

            try
            {
                _callback(reactorEvent);
            }
            catch
            {
                // Per the doc contract: a buggy subscriber must not
                // propagate to EventSource.WriteEvent (which would
                // surface as an ETW emission failure) or to other
                // subscribers. Swallow silently here.
            }
        }

        public override void Dispose()
        {
            // EventListener.Dispose is idempotent per its docs, but we
            // guard the DisableEvents call to be safe across racing
            // disposes from the user side.
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                base.Dispose();
                return;
            }

            try
            {
                if (_source is not null)
                    DisableEvents(_source);
            }
            catch
            {
                // EventSource has already been disposed by the runtime
                // (process shutdown). Nothing to do.
            }

            base.Dispose();
        }
    }
}
