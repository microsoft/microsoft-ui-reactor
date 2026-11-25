# Navigation transitions

Transitions that play when the navigation host changes pages.

**Learn more:** [Navigation](../../navigation.md)

## `ConnectedTransition`

Shared-element navigation animation, coordinated by NavigationHost.

```csharp
public sealed record ConnectedTransition : NavigationTransition
```

Namespace: `Microsoft.UI.Reactor.Navigation`. Inherits from [`NavigationTransition`](#navigationtransition).

### Constructors

#### `ConnectedTransition`

```csharp
public ConnectedTransition()
```

> **Obsolete.** Constructors of types with required members are not supported in this version of your compiler.

### Properties

#### `AnimationKey`

```csharp
public string AnimationKey  // get, init
```

## `DrillInTransition`

WinUI drill-in transition. Supplying a duration opts into Reactor's customizable symmetric drill-in behavior.

```csharp
public sealed record DrillInTransition : NavigationTransition
```

Namespace: `Microsoft.UI.Reactor.Navigation`. Inherits from [`NavigationTransition`](#navigationtransition).

### Constructors

#### `DrillInTransition`

```csharp
public DrillInTransition()
```

### Properties

#### `Duration`

```csharp
public TimeSpan? Duration  // get, init
```

## `EntranceTransition`

Entrance transition — slide up + fade in on the incoming page. Mirrors WinUI's `EntranceNavigationTransitionInfo`, the animation a `Frame` plays when `Navigate` is called without a `NavigationTransitionInfo`.

```csharp
public sealed record EntranceTransition : NavigationTransition
```

Namespace: `Microsoft.UI.Reactor.Navigation`. Inherits from [`NavigationTransition`](#navigationtransition).

### Constructors

#### `EntranceTransition`

```csharp
public EntranceTransition()
```

## `FadeTransition`

Crossfade transition — animate opacity on both visuals.

```csharp
public sealed record FadeTransition : NavigationTransition
```

Namespace: `Microsoft.UI.Reactor.Navigation`. Inherits from [`NavigationTransition`](#navigationtransition).

### Constructors

#### `FadeTransition`

```csharp
public FadeTransition()
```

### Properties

#### `Duration`

```csharp
public TimeSpan? Duration  // get, init
```

## `NavigationTransition`

Abstract base for navigation transition definitions. Concrete types describe the animation; the TransitionEngine (Phase 4) executes them. Static factory methods provide a convenient API.

```csharp
public abstract record NavigationTransition
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Constructors

#### `NavigationTransition`

```csharp
protected NavigationTransition()
```

### Methods

#### `Connected`

Animates the element marked with the same ConnectedAnimation key on both pages. This selects shared-element-only motion. To compose with a page transition, use [`NavigateOptions.ConnectedAnimationKey`](NavigateOptions.md#connectedanimationkey) alongside [`NavigateOptions.Transition`](NavigateOptions.md#transition). Missing or ambiguous elements fall back to the selected page transition (an instant swap for this method). Per-navigation keys are remembered for back/forward navigation within this session.

```csharp
public static NavigationTransition Connected(string animationKey)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `animationKey` | `string` |  |

#### `DrillIn`

Drill-in (scale + fade from center) transition.

```csharp
public static NavigationTransition DrillIn(TimeSpan? duration = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `duration` | `TimeSpan?` | `null` |  |

#### `Entrance`

Entrance transition — the incoming page slides up a short distance and fades in. This is WinUI's own default page transition (`EntranceNavigationTransitionInfo`, the "page refresh" animation), and the motion [`NavigationTransition.Default`](#default) currently resolves to.

```csharp
public static NavigationTransition Entrance()
```

#### `Fade`

Crossfade transition.

```csharp
public static NavigationTransition Fade(TimeSpan? duration = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `duration` | `TimeSpan?` | `null` |  |

#### `Slide`

Slide transition. With no arguments this is WinUI's slide specification — vertical, 600 ms, exponential easing.

```csharp
public static NavigationTransition Slide(SlideDirection direction = SlideDirection.FromBottom, TimeSpan? duration = null, CompositionEasingFunction? easing = null, float? distance = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `direction` | [`SlideDirection`](#slidedirection) | `SlideDirection.FromBottom` |  |
| `duration` | `TimeSpan?` | `null` |  |
| `easing` | `CompositionEasingFunction?` | `null` |  |
| `distance` | `float?` | `null` |  |

**Remarks**

Supplying `duration`, `easing`, or `distance` switches to Reactor's customizable simultaneous slide, which is a structurally different animation — not the WinUI slide with one value changed. [`SlideDirection.FromTop`](#values) is Reactor-only and always takes that path. Note the default direction is [`SlideDirection.FromBottom`](#values); pass [`SlideDirection.FromRight`](#values) explicitly for a horizontal push.

#### `Spring`

Spring-physics slide transition. A Reactor extension with no WinUI counterpart, so it keeps a horizontal default rather than following [`NavigationTransition.Slide`](#slide)'s vertical one.

```csharp
public static NavigationTransition Spring(float dampingRatio = 0.6f, float period = 0.08f, SlideDirection direction = SlideDirection.FromRight)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `dampingRatio` | `float` | `0.6f` |  |
| `period` | `float` | `0.08f` |  |
| `direction` | [`SlideDirection`](#slidedirection) | `SlideDirection.FromRight` |  |

### Fields

#### `Default`

The transition a host or navigation gets when it doesn't ask for one. Currently resolves to [`NavigationTransition.Entrance`](#entrance), which is what WinUI's `Frame` plays when `Navigate` is called with no `NavigationTransitionInfo`.

```csharp
public static readonly NavigationTransition Default
```

**Remarks**

This is a policy alias, not a motion — it means "whatever Reactor defaults to". Use [`NavigationTransition.Entrance`](#entrance) when you want the entrance motion specifically and don't want the call site to silently follow a future change of default.

#### `None`

No animation — instant swap.

```csharp
public static readonly NavigationTransition None
```

## `SlideDirection`

Direction for slide-based transitions.

```csharp
public enum SlideDirection
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `FromRight` | `0` |  |
| `FromLeft` | `1` |  |
| `FromBottom` | `2` |  |
| `FromTop` | `3` |  |

## `SlideTransition`

Slide transition — animate offset and opacity.

```csharp
public sealed record SlideTransition : NavigationTransition
```

Namespace: `Microsoft.UI.Reactor.Navigation`. Inherits from [`NavigationTransition`](#navigationtransition).

### Constructors

#### `SlideTransition`

```csharp
public SlideTransition()
```

### Properties

#### `Direction`

```csharp
public SlideDirection Direction  // get, init
```

#### `Distance`

Custom slide distance in pixels. Supplying one selects Reactor's customizable slide, where it is the distance travelled; leaving it null keeps whichever path the other properties select — WinUI's specification when they are all null, otherwise the customizable slide's own 200px default.

```csharp
public float? Distance  // get, init
```

#### `Duration`

```csharp
public TimeSpan? Duration  // get, init
```

#### `Easing`

```csharp
public CompositionEasingFunction? Easing  // get, init
```

## `SpringSlideTransition`

Spring-physics slide transition. Unlike [`SlideTransition`](#slidetransition) this is a Reactor extension with no WinUI counterpart, so it keeps its own horizontal default rather than following WinUI's vertical slide specification.

```csharp
public sealed record SpringSlideTransition : NavigationTransition
```

Namespace: `Microsoft.UI.Reactor.Navigation`. Inherits from [`NavigationTransition`](#navigationtransition).

### Constructors

#### `SpringSlideTransition`

```csharp
public SpringSlideTransition()
```

### Properties

#### `DampingRatio`

```csharp
public float DampingRatio  // get, init
```

#### `Direction`

```csharp
public SlideDirection Direction  // get, init
```

#### `Period`

```csharp
public float Period  // get, init
```

## `SuppressTransition`

No animation — instant swap.

```csharp
public sealed record SuppressTransition : NavigationTransition
```

Namespace: `Microsoft.UI.Reactor.Navigation`. Inherits from [`NavigationTransition`](#navigationtransition).

### Constructors

#### `SuppressTransition`

```csharp
public SuppressTransition()
```
