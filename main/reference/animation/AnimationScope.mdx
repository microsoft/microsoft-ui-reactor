---
title: "AnimationScope"
description: "Provides ambient animation context via [ThreadStatic] fields. When a curve is active, property changes in the reconciler's ApplyModifiers route through compositor animations instead of direct property assignment."
---

```csharp
public static class AnimationScope
```

Namespace: `Microsoft.UI.Reactor.Animation`.

**Learn more:** [Animation](/microsoft-ui-reactor/main/guides/animation)

## Properties [#properties]

### `Current` [#current]

Gets the current ambient animation curve, or null if none.

```csharp
public static Curve? Current  // get
```

### `HasScope` [#hasscope]

Returns true if inside a WithAnimation scope (even if curve is null for explicit suppression).

```csharp
public static bool HasScope  // get
```

## Methods [#methods]

### `WithAnimation` [#withanimation]

Runs the action with the given curve as the ambient animation. Property changes in the reconciler will animate using this curve. Nesting is supported: inner scopes override outer ones, restoring correctly in finally. Pass null to explicitly suppress animation (e.g., inside an outer animated scope).

```csharp
public static void WithAnimation(Curve? curve, Action action)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `curve` | [`Curve?`](/microsoft-ui-reactor/main/reference/animation/curves#curve) |  |
| `action` | `Action` |  |

### `WithAnimationAsync` [#withanimationasync]

Async variant: runs the action with ambient animation, then returns a Task that completes when all compositor animations started during the scope finish. Uses CompositionScopedBatch for completion tracking.

```csharp
public static Task WithAnimationAsync(Curve? curve, Action action)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `curve` | [`Curve?`](/microsoft-ui-reactor/main/reference/animation/curves#curve) |  |
| `action` | `Action` |  |
