# Navigation diagnostics

Diagnostic events raised by the navigation system.

**Learn more:** [Navigation](../../navigation.md)

## `CacheDiagnosticEvent`

```csharp
public sealed class CacheDiagnosticEvent
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Constructors

#### `CacheDiagnosticEvent`

```csharp
public CacheDiagnosticEvent(object route)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `route` | `object` |  |

### Properties

#### `Route`

```csharp
public object Route  // get
```

## `DeepLinkDiagnosticEvent`

```csharp
public sealed class DeepLinkDiagnosticEvent
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Constructors

#### `DeepLinkDiagnosticEvent`

```csharp
public DeepLinkDiagnosticEvent(string path, bool matched, int routeCount)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `path` | `string` |  |
| `matched` | `bool` |  |
| `routeCount` | `int` |  |

### Properties

#### `Matched`

```csharp
public bool Matched  // get
```

#### `Path`

```csharp
public string Path  // get
```

#### `RouteCount`

```csharp
public int RouteCount  // get
```

## `NavigationDiagnosticEvent`

```csharp
public sealed class NavigationDiagnosticEvent
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Constructors

#### `NavigationDiagnosticEvent`

```csharp
public NavigationDiagnosticEvent(object from, object to, NavigationMode mode)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `from` | `object` |  |
| `to` | `object` |  |
| `mode` | [`NavigationMode`](enums.md#navigationmode) |  |

### Properties

#### `From`

```csharp
public object From  // get
```

#### `Mode`

```csharp
public NavigationMode Mode  // get
```

#### `Reason`

```csharp
public string? Reason  // get, init
```

#### `To`

```csharp
public object To  // get
```

## `NavigationDiagnostics`

Diagnostic events for the navigation system. Subscribe to these events to observe navigation operations for debugging and telemetry. All events fire synchronously on the UI thread.

Each event also emits to the `Microsoft-UI-Reactor` ETW provider under the `Keywords.Navigation` keyword (spec 044 §4.2). Use `dotnet-trace`/`PerfView` for capture in Release; the in-process [`ReactorTrace.Subscribe`](../diagnostics/ReactorTrace.md#subscribe) helper exposes the same stream programmatically.

PII: ETW payloads carry only the destination route's type name (a developer-authored identifier), the navigation mode, and structural values like cache reasons. Concrete route instances, deep-link paths, and any user-controllable strings are intentionally omitted per §6.2.1.

```csharp
public static class NavigationDiagnostics
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Events

#### `CacheEviction`

Fires when a page is evicted from the cache.

```csharp
public static event EventHandler<CacheDiagnosticEvent>? CacheEviction
```

#### `CacheHit`

Fires on a cache hit during navigation.

```csharp
public static event EventHandler<CacheDiagnosticEvent>? CacheHit
```

#### `CacheMiss`

Fires on a cache miss during navigation.

```csharp
public static event EventHandler<CacheDiagnosticEvent>? CacheMiss
```

#### `DeepLinkResolved`

Fires when a deep link resolves (match or miss).

```csharp
public static event EventHandler<DeepLinkDiagnosticEvent>? DeepLinkResolved
```

#### `NavigationCancelled`

Fires when a navigation guard cancels a navigation.

```csharp
public static event EventHandler<NavigationDiagnosticEvent>? NavigationCancelled
```

#### `NavigationCompleted`

Fires when a navigation completes successfully.

```csharp
public static event EventHandler<NavigationDiagnosticEvent>? NavigationCompleted
```

#### `NavigationRequested`

Fires when a navigation is attempted (before guards run).

```csharp
public static event EventHandler<NavigationDiagnosticEvent>? NavigationRequested
```

#### `TransitionCompleted`

Fires when a transition animation completes.

```csharp
public static event EventHandler<TransitionDiagnosticEvent>? TransitionCompleted
```

#### `TransitionStarted`

Fires when a transition animation starts.

```csharp
public static event EventHandler<TransitionDiagnosticEvent>? TransitionStarted
```

## `TransitionDiagnosticEvent`

```csharp
public sealed class TransitionDiagnosticEvent
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Constructors

#### `TransitionDiagnosticEvent`

```csharp
public TransitionDiagnosticEvent(NavigationTransition transition, NavigationMode mode)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `transition` | [`NavigationTransition`](transitions.md#navigationtransition) |  |
| `mode` | [`NavigationMode`](enums.md#navigationmode) |  |

### Properties

#### `Mode`

```csharp
public NavigationMode Mode  // get
```

#### `Transition`

```csharp
public NavigationTransition Transition  // get
```
