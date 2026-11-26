---
title: "Navigation diagnostics"
description: "Diagnostic events raised by the navigation system."
---

**Learn more:** [Navigation](/microsoft-ui-reactor/main/guides/navigation)

## `CacheDiagnosticEvent` [#cachediagnosticevent]

```csharp
public sealed class CacheDiagnosticEvent
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Constructors [#constructors]

#### `CacheDiagnosticEvent` [#cachediagnosticevent_1]

```csharp
public CacheDiagnosticEvent(object route)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `route` | `object` |  |

### Properties [#properties]

#### `Route` [#route]

```csharp
public object Route  // get
```

## `DeepLinkDiagnosticEvent` [#deeplinkdiagnosticevent]

```csharp
public sealed class DeepLinkDiagnosticEvent
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Constructors [#constructors_1]

#### `DeepLinkDiagnosticEvent` [#deeplinkdiagnosticevent_1]

```csharp
public DeepLinkDiagnosticEvent(string path, bool matched, int routeCount)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `path` | `string` |  |
| `matched` | `bool` |  |
| `routeCount` | `int` |  |

### Properties [#properties_1]

#### `Matched` [#matched]

```csharp
public bool Matched  // get
```

#### `Path` [#path]

```csharp
public string Path  // get
```

#### `RouteCount` [#routecount]

```csharp
public int RouteCount  // get
```

## `NavigationDiagnosticEvent` [#navigationdiagnosticevent]

```csharp
public sealed class NavigationDiagnosticEvent
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Constructors [#constructors_2]

#### `NavigationDiagnosticEvent` [#navigationdiagnosticevent_1]

```csharp
public NavigationDiagnosticEvent(object from, object to, NavigationMode mode)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `from` | `object` |  |
| `to` | `object` |  |
| `mode` | [`NavigationMode`](/microsoft-ui-reactor/main/reference/navigation/enums#navigationmode) |  |

### Properties [#properties_2]

#### `From` [#from]

```csharp
public object From  // get
```

#### `Mode` [#mode]

```csharp
public NavigationMode Mode  // get
```

#### `Reason` [#reason]

```csharp
public string? Reason  // get, init
```

#### `To` [#to]

```csharp
public object To  // get
```

## `NavigationDiagnostics` [#navigationdiagnostics]

Diagnostic events for the navigation system. Subscribe to these events to observe navigation operations for debugging and telemetry. All events fire synchronously on the UI thread.

Each event also emits to the `Microsoft-UI-Reactor` ETW provider under the `Keywords.Navigation` keyword (spec 044 §4.2). Use `dotnet-trace`/`PerfView` for capture in Release; the in-process [`ReactorTrace.Subscribe`](/microsoft-ui-reactor/main/reference/diagnostics/ReactorTrace#subscribe) helper exposes the same stream programmatically.

PII: ETW payloads carry only the destination route's type name (a developer-authored identifier), the navigation mode, and structural values like cache reasons. Concrete route instances, deep-link paths, and any user-controllable strings are intentionally omitted per §6.2.1.

```csharp
public static class NavigationDiagnostics
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Events [#events]

#### `CacheEviction` [#cacheeviction]

Fires when a page is evicted from the cache.

```csharp
public static event EventHandler<CacheDiagnosticEvent>? CacheEviction
```

#### `CacheHit` [#cachehit]

Fires on a cache hit during navigation.

```csharp
public static event EventHandler<CacheDiagnosticEvent>? CacheHit
```

#### `CacheMiss` [#cachemiss]

Fires on a cache miss during navigation.

```csharp
public static event EventHandler<CacheDiagnosticEvent>? CacheMiss
```

#### `DeepLinkResolved` [#deeplinkresolved]

Fires when a deep link resolves (match or miss).

```csharp
public static event EventHandler<DeepLinkDiagnosticEvent>? DeepLinkResolved
```

#### `NavigationCancelled` [#navigationcancelled]

Fires when a navigation guard cancels a navigation.

```csharp
public static event EventHandler<NavigationDiagnosticEvent>? NavigationCancelled
```

#### `NavigationCompleted` [#navigationcompleted]

Fires when a navigation completes successfully.

```csharp
public static event EventHandler<NavigationDiagnosticEvent>? NavigationCompleted
```

#### `NavigationRequested` [#navigationrequested]

Fires when a navigation is attempted (before guards run).

```csharp
public static event EventHandler<NavigationDiagnosticEvent>? NavigationRequested
```

#### `TransitionCompleted` [#transitioncompleted]

Fires when a transition animation completes.

```csharp
public static event EventHandler<TransitionDiagnosticEvent>? TransitionCompleted
```

#### `TransitionStarted` [#transitionstarted]

Fires when a transition animation starts.

```csharp
public static event EventHandler<TransitionDiagnosticEvent>? TransitionStarted
```

## `TransitionDiagnosticEvent` [#transitiondiagnosticevent]

```csharp
public sealed class TransitionDiagnosticEvent
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Constructors [#constructors_3]

#### `TransitionDiagnosticEvent` [#transitiondiagnosticevent_1]

```csharp
public TransitionDiagnosticEvent(NavigationTransition transition, NavigationMode mode)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `transition` | [`NavigationTransition`](/microsoft-ui-reactor/main/reference/navigation/transitions#navigationtransition) |  |
| `mode` | [`NavigationMode`](/microsoft-ui-reactor/main/reference/navigation/enums#navigationmode) |  |

### Properties [#properties_3]

#### `Mode` [#mode_1]

```csharp
public NavigationMode Mode  // get
```

#### `Transition` [#transition]

```csharp
public NavigationTransition Transition  // get
```
