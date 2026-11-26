---
title: "ReactorTrace"
seo:
  description: "In-process subscription helper for the Microsoft-UI-Reactor EventSource. Lets an app developer (or an internal devtools surface — reactor.logs MCP, an ILogger bridge) observe framework events without writing an EventListener subclass."
---

In-process subscription helper for the `Microsoft-UI-Reactor` [`EventSource`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventsource). Lets an app developer (or an internal devtools surface — `reactor.logs` MCP, an `ILogger` bridge) observe framework events without writing an [`EventListener`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventlistener) subclass.

This is deliberately **not** a file-capture API. The .NET runtime already provides three:

- `DOTNET_EnableEventPipe` + `DOTNET_EventPipeOutputPath` environment variables (zero app code).
- `dotnet-trace collect --providers Microsoft-UI-Reactor`.
- Visual Studio Performance Profiler → Events Viewer.

See `docs/guide/diagnostics.md` for end-to-end examples.

```csharp
public static class ReactorTrace
```

Namespace: `Microsoft.UI.Reactor.Diagnostics`.

**Learn more:** [Diagnostics](/microsoft-ui-reactor/main/tools/diagnostics)

## Methods [#methods]

### `GetComponentControl` [#getcomponentcontrol]

Resolves the `componentId` carried by a `ComponentRendered` event (`Microsoft-UI-Reactor` EventId 40) to the realized control whose bounds are that component's — the component's wrapper, or for a host's root component the host's current root content control. Use it to flash the region that just re-rendered ("highlight updates").

Returns `null` for `0`, for an id this process never issued, once the component has unmounted or its control was collected, and while the control does not exist yet: a component's mount event is raised before its subtree mounts (the id is mapped once that finishes), and a root's first event before its reconcile pass has produced the content. An id from a render whose subtree was then discarded — caught by an `ErrorBoundary`, or a failure that propagated — may never resolve. So resolve after the pass completes (e.g. on the next dispatcher tick) rather than inside the event callback, and treat `null` as "nothing on screen for this id". Ids are only issued, and controls only recorded, while the event is enabled, so subscribe first.

Call on the UI thread; the returned control is a live WinUI object.

```csharp
public static UIElement? GetComponentControl(long componentId)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `componentId` | `long` | The event's `componentId` payload field. |

### `Subscribe` [#subscribe]

Subscribes to `Microsoft-UI-Reactor` events in-process. Returns an [`IDisposable`](https://learn.microsoft.com/dotnet/api/system.idisposable) token; dispose it to detach.

The callback fires on the emission thread — typically the UI dispatcher when an event originates from reconcile / render, or a thread-pool thread otherwise. Keep the callback work minimal and never let it throw across the framework boundary — a throwing subscriber propagates into [`EventSource`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventsource)'s emit path and surfaces as an ETW failure.

Multiple concurrent subscribers are supported. Each subscriber's `level` and `keywords` are independently active until the returned token is disposed; the runtime unions them when computing whether `IsEnabled` on the event source returns true. Be aware that a broad subscriber ([`EventLevel.Verbose`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventlevel.verbose) + all keywords) raises the cost of every hot-path call site on the framework for as long as it lives.

For writing a trace file, use one of:

- `DOTNET_EnableEventPipe` / `DOTNET_EventPipeOutputPath` env vars.
- `dotnet-trace collect --providers Microsoft-UI-Reactor`.
- Visual Studio Performance Profiler → Events Viewer.

```csharp
public static IDisposable Subscribe(Action<ReactorEvent> onEvent, EventLevel level = (EventLevel)5, EventKeywords keywords = (EventKeywords)-1)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `onEvent` | `Action<ReactorEvent>` |  | Callback invoked once per matching event. Must be non-null. |
| `level` | `EventLevel` | `(EventLevel)5` | Minimum severity to forward. Defaults to [`EventLevel.Verbose`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventlevel.verbose) so subscribers see everything (state changes, per-trampoline dispatches) unless they explicitly pass a stricter level. |
| `keywords` | `EventKeywords` | `(EventKeywords)-1` | Keyword mask to forward. Defaults to all keywords (`(EventKeywords)(-1)`). |

**Returns:** Disposable token. Disposing detaches the underlying [`EventListener`](https://learn.microsoft.com/dotnet/api/system.diagnostics.tracing.eventlistener); subsequent events do not reach `onEvent`.

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) | If `onEvent` is `null`. |

### `TryGetComponentId` [#trygetcomponentid]

The reverse of [`ReactorTrace.GetComponentControl`](#getcomponentcontrol): the `componentId` that `ComponentRendered` events use for the component rendering into `control` (a component's wrapper control). Lets an inspector filter the event stream down to the component a user selected.

Returns `false` when `control` is not a component wrapper, or when that component has not rendered since `ComponentRendered` was enabled. A host's root id is never returned here — the root content control is often also a child component's wrapper, and that child keeps the mapping.

Call on the UI thread: the id is read from the control's Reactor state.

```csharp
public static bool TryGetComponentId(UIElement control, out long componentId)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `control` | `UIElement` |  |
| `componentId` | `long` |  |

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) | If `control` is `null`. |
