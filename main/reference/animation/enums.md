---
title: "Enums"
description: "The enumerations in this category."
---

**Learn more:** [Animation](/microsoft-ui-reactor/main/guides/animation)

## `AnimateProperty` [#animateproperty]

Flags enum specifying which compositor properties should have implicit animations applied via the .Animate() modifier.

```csharp
public enum AnimateProperty
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `Opacity` | `1` |  |
| `Offset` | `2` |  |
| `Scale` | `4` |  |
| `Rotation` | `8` |  |
| `CenterPoint` | `16` |  |
| `All` | `31` |  |

## `AnimationKind` [#animationkind]

Animation kinds that an ambient `Animations.Animate(...)` transaction can carry through a state-setter into the resulting diff. See spec 042 §6.

```csharp
public enum AnimationKind
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Values [#values_1]

| Name | Value | Description |
| --- | --- | --- |
| `None` | `0` | No animation; container changes apply instantly. |
| `Default` | `1` | Framework default — currently matches [`AnimationKind.EaseOut`](#values_1). |
| `Spring` | `2` | Critically-damped spring; expressive on insert/remove. |
| `EaseIn` | `3` | Standard quadratic ease-in. |
| `EaseOut` | `4` | Standard quadratic ease-out (matches WinUI `RepositionThemeTransition`). |
| `EaseInOut` | `5` | Standard ease-in-out. |
