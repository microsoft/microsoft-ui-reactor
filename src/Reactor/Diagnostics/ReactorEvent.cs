using System.Diagnostics.Tracing;

namespace Microsoft.UI.Reactor.Diagnostics;

/// <summary>
/// A single event captured from the <c>Microsoft-UI-Reactor</c>
/// <see cref="EventSource"/>, delivered to subscribers of
/// <see cref="ReactorTrace.Subscribe(Action{ReactorEvent}, EventLevel, EventKeywords)"/>.
///
/// <para>
/// The payload arrays come from <see cref="EventWrittenEventArgs.Payload"/>
/// and <see cref="EventWrittenEventArgs.PayloadNames"/> directly — no
/// reflection is involved, so this surface is AOT/trim-clean.
/// </para>
///
/// <para>
/// PII discipline: every framework event has already stripped sensitive
/// fields (no <see cref="Exception.Message"/>, no window titles, no
/// instantiated route paths). Subscribers should respect that policy in
/// any downstream sink they forward to.
/// </para>
/// </summary>
/// <param name="EventId">Numeric event identifier (stable per event method;
/// see the EventId allocation comment in <c>ReactorEventSource</c>).</param>
/// <param name="EventName">The C# method name on <c>ReactorEventSource</c>
/// that emitted this event (e.g. <c>"ReconcileStart"</c>,
/// <c>"SwallowedError"</c>).</param>
/// <param name="Level">Severity (<see cref="EventLevel.Verbose"/> through
/// <see cref="EventLevel.Critical"/>).</param>
/// <param name="Keywords">The bitwise keyword(s) the event was emitted
/// under. ETW reserved bits in the high 4 bits may be set by the runtime;
/// subscribers should mask against the keyword they care about rather
/// than equality-test.</param>
/// <param name="TimestampUtc">UTC capture timestamp at delivery time.</param>
/// <param name="ThreadId">Managed thread id of the emission thread.</param>
/// <param name="Payload">Positional payload values in the order declared
/// by the event method's parameters. Always non-null but may be empty.</param>
/// <param name="PayloadNames">Parallel array of parameter names. Always
/// non-null; same length as <paramref name="Payload"/>.</param>
public readonly record struct ReactorEvent(
    int EventId,
    string EventName,
    EventLevel Level,
    EventKeywords Keywords,
    DateTime TimestampUtc,
    int ThreadId,
    IReadOnlyList<object?> Payload,
    IReadOnlyList<string> PayloadNames);
