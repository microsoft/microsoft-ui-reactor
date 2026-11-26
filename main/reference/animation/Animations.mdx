---
title: "Animations"
description: "Transactional animation entry points — the SwiftUI withAnimation { … } analog. See spec 042 §6."
---

```csharp
public static class Animations
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Animation](/microsoft-ui-reactor/main/guides/animation)

## Remarks [#remarks]

`Animate(...)` pushes an `AmbientAnimation` onto an [`AsyncLocal`](https://learn.microsoft.com/dotnet/api/system.threading.asynclocal-1) stack for the duration of the supplied action. State setters from `UseState` / `UseReducer` invoked inside the block snapshot the ambient synchronously so the resulting render observes the same intent even when the rerender hops a dispatcher. The reconciler consumes the snapshot when applying `KeyedListDiff` ops and `ChildReconciler` mount / move / unmount, configuring per-container animations to match.

`Animate` does **not** animate arbitrary property changes (color, size) on surviving leaves — that remains the job of per-element modifiers such as `OpacityTransition`. Scoping the transaction to keyed structural changes keeps the SwiftUI "withAnimation only animates layout-shape ops" contract intact. (spec 042 §6, scope discipline)

Named `Animations` (plural) instead of `Animation` to avoid collision with the existing `Microsoft.UI.Reactor.Animation` sub-namespace, which houses per-element animation modifiers.

## Methods [#methods]

### `Animate` [#animate]

#### `Animate(AnimationKind, Action)` [#animateanimationkind-action]

Wrap a state mutation in an ambient animation transaction.

```csharp
public static void Animate(AnimationKind kind, Action action)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `kind` | [`AnimationKind`](/microsoft-ui-reactor/main/reference/animation/enums#animationkind) | The animation kind that should apply to any container insert / move / remove ops produced by state setters invoked from `action`. Passing [`AnimationKind.None`](/microsoft-ui-reactor/main/reference/animation/enums#values_1) is meaningful: it explicitly suppresses any \*outer\* `Animate` for the scope of the inner block (nested calls stack like `using`). |
| `action` | `Action` | State mutation. Typically calls `setItems(...)` from a hook. |

**Example**

```csharp
Animate(AnimationKind.Spring, () => setItems([..items, x]));
```

#### `Animate<T>(AnimationKind, Func<T>)` [#animatetanimationkind-funct]

Wrap a value-producing state mutation in an ambient animation transaction. Returns the value produced by `func`.

```csharp
public static T Animate<T>(AnimationKind kind, Func<T> func)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `kind` | [`AnimationKind`](/microsoft-ui-reactor/main/reference/animation/enums#animationkind) |  |
| `func` | `Func<T>` |  |
