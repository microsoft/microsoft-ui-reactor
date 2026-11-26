---
title: "ReactorEvent"
seo:
  description: "A single event captured from the Microsoft-UI-Reactor EventSource, delivered to subscribers of ReactorTrace.Subscribe."
---

A single event captured from the `Microsoft-UI-Reactor` [`EventSource`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventsource), delivered to subscribers of [`ReactorTrace.Subscribe`](/microsoft-ui-reactor/main/reference/diagnostics/ReactorTrace#subscribe).

The payload arrays come from [`EventWrittenEventArgs.Payload`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventwritteneventargs.payload) and [`EventWrittenEventArgs.PayloadNames`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventwritteneventargs.payloadnames) directly — no reflection is involved, so this surface is AOT/trim-clean.

PII discipline: every framework event has already stripped sensitive fields (no [`Exception.Message`](https://learn.microsoft.com/dotnet/api/system.exception.message), no window titles, no instantiated route paths). Subscribers should respect that policy in any downstream sink they forward to.

```csharp
public readonly record struct ReactorEvent(int EventId, string EventName, EventLevel Level, EventKeywords Keywords, DateTime TimestampUtc, int ThreadId, IReadOnlyList<object?> Payload, IReadOnlyList<string> PayloadNames)
```

Namespace: `Microsoft.UI.Reactor.Diagnostics`.

**Learn more:** [Diagnostics](/microsoft-ui-reactor/main/guides/diagnostics)

## Constructors [#constructors]

### `ReactorEvent` [#reactorevent_1]

A single event captured from the `Microsoft-UI-Reactor` [`EventSource`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventsource), delivered to subscribers of [`ReactorTrace.Subscribe`](/microsoft-ui-reactor/main/reference/diagnostics/ReactorTrace#subscribe).

The payload arrays come from [`EventWrittenEventArgs.Payload`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventwritteneventargs.payload) and [`EventWrittenEventArgs.PayloadNames`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventwritteneventargs.payloadnames) directly — no reflection is involved, so this surface is AOT/trim-clean.

PII discipline: every framework event has already stripped sensitive fields (no [`Exception.Message`](https://learn.microsoft.com/dotnet/api/system.exception.message), no window titles, no instantiated route paths). Subscribers should respect that policy in any downstream sink they forward to.

```csharp
public ReactorEvent(int EventId, string EventName, EventLevel Level, EventKeywords Keywords, DateTime TimestampUtc, int ThreadId, IReadOnlyList<object?> Payload, IReadOnlyList<string> PayloadNames)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `EventId` | `int` | Numeric event identifier (stable per event method; see the EventId allocation comment in `ReactorEventSource`). |
| `EventName` | `string` | The C# method name on `ReactorEventSource` that emitted this event (e.g. `"ReconcileStart"`, `"SwallowedError"`). |
| `Level` | `EventLevel` | Severity ([`EventLevel.Verbose`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventlevel.verbose) through [`EventLevel.Critical`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventlevel.critical)). |
| `Keywords` | `EventKeywords` | The bitwise keyword(s) the event was emitted under. ETW reserved bits in the high 4 bits may be set by the runtime; subscribers should mask against the keyword they care about rather than equality-test. |
| `TimestampUtc` | `DateTime` | UTC capture timestamp at delivery time. |
| `ThreadId` | `int` | Managed thread id of the emission thread. |
| `Payload` | `IReadOnlyList<object?>` | Positional payload values in the order declared by the event method's parameters. Always non-null but may be empty. |
| `PayloadNames` | `IReadOnlyList<string>` | Parallel array of parameter names. Always non-null; same length as `Payload`. |

## Properties [#properties]

### `EventId` [#eventid]

Numeric event identifier (stable per event method; see the EventId allocation comment in `ReactorEventSource`).

```csharp
public int EventId  // get, init
```

### `EventName` [#eventname]

The C# method name on `ReactorEventSource` that emitted this event (e.g. `"ReconcileStart"`, `"SwallowedError"`).

```csharp
public string EventName  // get, init
```

### `Keywords` [#keywords]

The bitwise keyword(s) the event was emitted under. ETW reserved bits in the high 4 bits may be set by the runtime; subscribers should mask against the keyword they care about rather than equality-test.

```csharp
public EventKeywords Keywords  // get, init
```

### `Level` [#level]

Severity ([`EventLevel.Verbose`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventlevel.verbose) through [`EventLevel.Critical`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventlevel.critical)).

```csharp
public EventLevel Level  // get, init
```

### `Payload` [#payload]

Positional payload values in the order declared by the event method's parameters. Always non-null but may be empty.

```csharp
public IReadOnlyList<object?> Payload  // get, init
```

### `PayloadNames` [#payloadnames]

Parallel array of parameter names. Always non-null; same length as `Payload`.

```csharp
public IReadOnlyList<string> PayloadNames  // get, init
```

### `ThreadId` [#threadid]

Managed thread id of the emission thread.

```csharp
public int ThreadId  // get, init
```

### `TimestampUtc` [#timestamputc]

UTC capture timestamp at delivery time.

```csharp
public DateTime TimestampUtc  // get, init
```
