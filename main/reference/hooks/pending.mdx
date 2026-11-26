---
title: "Pending"
description: "The Pending element that shows a fallback while resources below it load."
---

**Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## `PendingComponent` [#pendingcomponent]

Component backing the `Pending` element. Hosts a [`PendingScope`](#pendingscope), subscribes to its [`PendingScope.Changed`](#changed) event, and chooses between [`PendingProps.Fallback`](#fallback) and [`PendingProps.Child`](#child).

```csharp
public sealed class PendingComponent : Component<PendingProps>
```

Namespace: `Microsoft.UI.Reactor.Hooks`. Inherits from [`Component<TProps>`](/microsoft-ui-reactor/main/reference/components/Component-1).

### Remarks [#remarks]

The child subtree is always rendered. When the scope reports any loading resource, the child's `Visibility` is flipped to `Collapsed` and the fallback is shown instead. Because the subtree stays mounted, its hooks keep running, their fetches complete in the background, and the UI swaps to the real content on the next render frame after the scope reports clean.

### Constructors [#constructors]

#### `PendingComponent` [#pendingcomponent_1]

```csharp
public PendingComponent()
```

### Methods [#methods]

#### `Render` [#render]

```csharp
public override Element Render()
```

## `PendingFactory` [#pendingfactory]

Entry-point factory for the `Pending` bubble-up element. See `docs/specs/020-async-resources-design.md` §10 for the full mechanism.

```csharp
public static class PendingFactory
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Methods [#methods_1]

#### `Pending` [#pending_1]

Wraps `child` with a fresh [`PendingScope`](#pendingscope). Renders `fallback` instead of `child` while any `UseResource`/`UseInfiniteResource` in the subtree is in the `Loading` state. `Reloading(previous)` does **not** trigger the fallback — spec §10.1.

```csharp
public static Element Pending(Element fallback, Element child)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fallback` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `child` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

**Remarks**

The child subtree is always mounted so its hooks register with the scope. The element simply chooses which rendered tree to show — there is no unwinding of rendering, and no reconciler involvement.

Marked `[ReactorSourceTransparent]` (see [`ReactorSourceTransparentAttribute`](/microsoft-ui-reactor/main/reference/diagnostics/ReactorSourceTransparentAttribute), spec 010) because it is a pure forwarder: the element is really built by the `Component<,>` call on the next line, inside Reactor's own assembly, where no consumer call site exists to intercept. Without the annotation a `Pending(...)` element reports no location at all. With it, the interceptor in the consumer's compilation stamps the line they wrote `Pending(` on, which is the answer they were looking for.

## `PendingProps` [#pendingprops]

Props for [`PendingComponent`](#pendingcomponent).

```csharp
public sealed record PendingProps(Element Fallback, Element Child)
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Constructors [#constructors_1]

#### `PendingProps` [#pendingprops_1]

Props for [`PendingComponent`](#pendingcomponent).

```csharp
public PendingProps(Element Fallback, Element Child)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Fallback` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `Child` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

### Properties [#properties]

#### `Child` [#child]

```csharp
public Element Child  // get, init
```

#### `Fallback` [#fallback]

```csharp
public Element Fallback  // get, init
```

## `PendingScope` [#pendingscope]

Shared loading-state ref-count consumed by the `Pending` element and populated by `UseResource` / `UseInfiniteResource` hooks inside the scope. When the scope observes **any** registered resource in the `Loading` state (not `Reloading`), the owning `Pending` element renders its fallback instead of the child subtree.

```csharp
public sealed class PendingScope
```

Namespace: `Microsoft.UI.Reactor.Hooks`.

### Remarks [#remarks_1]

**Semantics.** Only `Loading` triggers the fallback — spec §10.1. A `Reloading(previous)` is "we already have something to show" and the subtree continues to render normally.

**Threading.** All members are thread-safe. [`PendingScope.Changed`](#changed) fires on the thread that caused the mutation — consumers (typically `Pending`'s re-render trigger) marshal it to the dispatcher themselves.

**Scope nesting.** Each `Pending` provides a fresh scope to its subtree, so nested `Pending`s are independent. A hook registers only with its nearest ancestor scope.

### Constructors [#constructors_2]

#### `PendingScope` [#pendingscope_1]

```csharp
public PendingScope()
```

### Properties [#properties_1]

#### `AnyLoading` [#anyloading]

True iff any tracked token is currently `Loading`.

```csharp
public bool AnyLoading  // get
```

#### `Count` [#count]

Snapshot the number of registered tokens (loading or not). Diagnostic only.

```csharp
public int Count  // get
```

### Methods [#methods_2]

#### `Register` [#register]

Start tracking `token` with the given initial `isLoading` state. A hook typically uses its own `this`-equivalent as the token.

```csharp
public void Register(object token, bool isLoading)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `token` | `object` |  |
| `isLoading` | `bool` |  |

#### `SetLoading` [#setloading]

Update `token`'s loading state. Silently ignored if the token was never registered (defensive — avoids forcing the caller to track whether they registered).

```csharp
public void SetLoading(object token, bool isLoading)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `token` | `object` |  |
| `isLoading` | `bool` |  |

#### `Unregister` [#unregister]

Stop tracking `token`. Idempotent.

```csharp
public void Unregister(object token)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `token` | `object` |  |

### Events [#events]

#### `Changed` [#changed]

Fires when a resource joins, leaves, or changes its loading state.

```csharp
public event Action? Changed
```
