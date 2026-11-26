# NavigationState\<TRoute\>

A snapshot of a [`NavigationHandle<TRoute>`](NavigationHandle.md)'s full state: the back stack, the currently active route, and the forward stack. Obtained from [`NavigationHandle.GetState`](NavigationHandle.md#getstate) and restored via [`NavigationHandle.SetState`](NavigationHandle.md#setstate).

```csharp
public sealed record NavigationState<TRoute>(IReadOnlyList<TRoute> BackStack, TRoute Current, IReadOnlyList<TRoute> ForwardStack) where TRoute : notnull
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

**Learn more:** [Navigation](../../navigation.md)

## Remarks

Reactor intentionally does not pick a serialization format for navigation state. This record is a plain POCO with no serializer-specific attributes — callers can persist it however they like: JSON via `System.Text.Json` (preferably with a source-generated `JsonSerializerContext` for AOT-safety), MessagePack, or by hand. Avoid insecure or obsolete serializers.

## Constructors

### `NavigationState`

A snapshot of a [`NavigationHandle<TRoute>`](NavigationHandle.md)'s full state: the back stack, the currently active route, and the forward stack. Obtained from [`NavigationHandle.GetState`](NavigationHandle.md#getstate) and restored via [`NavigationHandle.SetState`](NavigationHandle.md#setstate).

```csharp
public NavigationState(IReadOnlyList<TRoute> BackStack, TRoute Current, IReadOnlyList<TRoute> ForwardStack)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `BackStack` | `IReadOnlyList<TRoute>` |  |
| `Current` | `TRoute` |  |
| `ForwardStack` | `IReadOnlyList<TRoute>` |  |

**Remarks**

Reactor intentionally does not pick a serialization format for navigation state. This record is a plain POCO with no serializer-specific attributes — callers can persist it however they like: JSON via `System.Text.Json` (preferably with a source-generated `JsonSerializerContext` for AOT-safety), MessagePack, or by hand. Avoid insecure or obsolete serializers.

## Properties

### `BackStack`

```csharp
public IReadOnlyList<TRoute> BackStack  // get, init
```

### `Current`

```csharp
public TRoute Current  // get, init
```

### `ForwardStack`

```csharp
public IReadOnlyList<TRoute> ForwardStack  // get, init
```
