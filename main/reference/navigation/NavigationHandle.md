---
title: "NavigationHandle<TRoute>"
description: "Public API for controlling navigation. Wraps a NavigationStack with a safe, read-heavy interface. Obtained via UseNavigation hook."
sidebar:
  label: "NavigationHandle"
---

```csharp
public sealed class NavigationHandle<TRoute> where TRoute : notnull
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

**Learn more:** [Navigation](/microsoft-ui-reactor/main/guides/navigation)

## Remarks [#remarks]

**Thread safety (issue #234).** The mutating methods ([`NavigationHandle.Navigate`](#navigate), [`NavigationHandle.GoBack`](#goback), [`NavigationHandle.GoForward`](#goforward), [`NavigationHandle.Replace`](#replace), [`NavigationHandle.Reset`](#reset), [`NavigationHandle.PopTo`](#popto), [`NavigationHandle.SetState`](#setstate)) are thread-safe by default. When called from the UI thread the cost is a single thread-id compare. When called off-thread (e.g. from `Task.Run` or after `await … ConfigureAwait(false)`) the whole mutation — stack edit, [`NavigationHandle.Navigated`](#navigated)/`RouteChanged` events, and the component re-render — is auto-marshaled onto the captured UI dispatcher, mirroring the `UseState`/`UseReducer` contract from issue #212. An off-thread call throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) loudly — rather than corrupting the back/forward stacks — in two cases: when no UI dispatcher is available (unit-test / headless contexts), and when the captured dispatcher refuses the enqueue (its `TryEnqueue` returns `false`, e.g. once it has begun shutting down near window close). The bool-returning mutators return `true` when an off-thread call is accepted and scheduled; the actual guard/empty-stack outcome is then resolved on the UI thread. Off-thread callers must therefore not treat that `true` as confirmation that the navigation actually happened — only that it was scheduled. (On the UI thread the bool keeps its original meaning: whether the navigation succeeded.)

## Properties [#properties]

### `BackStack` [#backstack]

Readonly view of the back stack.

```csharp
public IReadOnlyList<TRoute> BackStack  // get
```

### `CanGoBack` [#cangoback]

True if there are entries in the back stack.

```csharp
public bool CanGoBack  // get
```

### `CanGoForward` [#cangoforward]

True if there are entries in the forward stack.

```csharp
public bool CanGoForward  // get
```

### `CurrentRoute` [#currentroute]

The currently active route.

```csharp
public TRoute CurrentRoute  // get
```

### `Depth` [#depth]

Total depth: back stack count + 1 (current).

```csharp
public int Depth  // get
```

### `ForwardStack` [#forwardstack]

Readonly view of the forward stack.

```csharp
public IReadOnlyList<TRoute> ForwardStack  // get
```

## Methods [#methods]

### `GetState` [#getstate]

Returns a snapshot of the full navigation state — back stack, current route, and forward stack — as a plain POCO. Persist it however you like (JSON, MessagePack, hand-rolled binary): Reactor intentionally does not pick a serialization format for you.

```csharp
public NavigationState<TRoute> GetState()
```

**Remarks**

For JSON persistence, declare a `JsonSerializerContext` covering [`NavigationState<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/NavigationState) and your route type so the call is AOT-safe. For polymorphic route hierarchies, annotate the base route type with `[JsonPolymorphic]` and `[JsonDerivedType]`.

### `GoBack` [#goback]

Go back to the previous route.

```csharp
public bool GoBack()
```

**Returns:** On the UI thread, `true` if navigation succeeded, or `false` if the back stack is empty or a guard cancelled it. When called off the UI thread, `true` means the call was marshaled and scheduled onto the UI dispatcher — not that it succeeded; the empty-stack/guard outcome is resolved later on the UI thread (see the type-level thread-safety remarks).

### `GoForward` [#goforward]

Go forward to the next route in the forward stack.

```csharp
public bool GoForward()
```

**Returns:** On the UI thread, `true` if navigation succeeded, or `false` if the forward stack is empty or a guard cancelled it. When called off the UI thread, `true` means the call was marshaled and scheduled onto the UI dispatcher — not that it succeeded; the empty-stack/guard outcome is resolved later on the UI thread (see the type-level thread-safety remarks).

### `Navigate` [#navigate]

Navigate to a new route. By default pushes the current route onto the back stack. If [`NavigateOptions.PushToBackStack`](/microsoft-ui-reactor/main/reference/navigation/NavigateOptions#pushtobackstack) is false, replaces the current route instead.

```csharp
public bool Navigate(TRoute route, NavigateOptions? options = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `route` | `TRoute` |  |  |
| `options` | [`NavigateOptions?`](/microsoft-ui-reactor/main/reference/navigation/NavigateOptions) | `null` |  |

**Returns:** On the UI thread, `true` if the navigation succeeded, or `false` if a guard cancelled it. When called off the UI thread, `true` means the call was marshaled and scheduled onto the UI dispatcher — not that it succeeded; the real outcome is resolved later on the UI thread (see the type-level thread-safety remarks).

### `PopTo` [#popto]

Pop entries from the back stack until the predicate matches.

```csharp
public bool PopTo(Func<TRoute, bool> predicate)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `predicate` | `Func<TRoute, bool>` |  |

**Returns:** On the UI thread, `true` if a matching entry was popped to, or `false` if no back-stack entry matches the predicate or a guard cancelled it. When called off the UI thread, `true` means the call was marshaled and scheduled onto the UI dispatcher — not that it succeeded; the real outcome is resolved later on the UI thread (see the type-level thread-safety remarks).

### `Replace` [#replace]

Replace the current route without modifying back/forward stacks.

```csharp
public bool Replace(TRoute route)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `route` | `TRoute` |  |

**Returns:** On the UI thread, `true` if the route was replaced, or `false` if a guard cancelled it. When called off the UI thread, `true` means the call was marshaled and scheduled onto the UI dispatcher — not that it succeeded; the real outcome is resolved later on the UI thread (see the type-level thread-safety remarks).

### `Reset` [#reset]

Reset the entire stack to a single root route. Clears back and forward stacks.

```csharp
public bool Reset(TRoute route)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `route` | `TRoute` |  |

**Returns:** On the UI thread, `true` if the stack was reset, or `false` if a guard cancelled it. When called off the UI thread, `true` means the call was marshaled and scheduled onto the UI dispatcher — not that it succeeded; the real outcome is resolved later on the UI thread (see the type-level thread-safety remarks).

### `SetState` [#setstate]

Restores a previously captured [`NavigationState<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/NavigationState). Replaces the back stack, current route, and forward stack, then fires [`NavigationHandle.Navigated`](#navigated) with [`NavigationMode.Reset`](/microsoft-ui-reactor/main/reference/navigation/enums#values_1).

```csharp
public void SetState(NavigationState<TRoute> state)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `state` | [`NavigationState<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/NavigationState) |  |

## Events [#events]

### `Navigated` [#navigated]

Fired after every successful navigation with details about the transition.

```csharp
public event Action<NavigationEventArgs<TRoute>>? Navigated
```
