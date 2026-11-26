# Modifiers: Animation

Transitions, keyframes, layout animations, interaction states and scroll-linked animation.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Animation](../../animation.md)

## `Animate`

Enables implicit compositor animation on visual property changes. All visual property changes (Opacity, Scale, Rotation, Translation, CenterPoint) will animate using the specified curve.

```csharp
T Animate<T>(this T el, Curve curve, AnimateProperty properties = AnimateProperty.All) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `curve` | [`Curve`](../animation/curves.md#curve) |  |  |
| `properties` | [`AnimateProperty`](../animation/enums.md#animateproperty) | `AnimateProperty.All` |  |

## `BackgroundTransition`

Adds an implicit BrushTransition on Background. Only available on Grid and Stack (VStack/HStack) — WinUI only supports BackgroundTransition on Grid, StackPanel, and ContentPresenter.

```csharp
GridElement BackgroundTransition(this GridElement el, TimeSpan? duration = null)
StackElement BackgroundTransition(this StackElement el, TimeSpan? duration = null)
```

Declared in `ElementExtensions`.

**Applies to:** [`GridElement`](../elements/layout.md#gridelement), [`StackElement`](../elements/layout.md#stackelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `duration` | `TimeSpan?` | `null` |  |

## `ConnectedAnimation`

Declares this element as a participant in a connected animation. When the element is unmounted (e.g., parent container changes type), the reconciler captures its visual snapshot. When a new element with the same key is mounted, the snapshot animates to the new element's position. Both source and destination must use the same key string.

```csharp
T ConnectedAnimation<T>(this T el, string key) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |

## `ContentTransitions`

Custom `TransitionCollection` applied to the expanding content area.

```csharp
ExpanderElement ContentTransitions(this ExpanderElement el, TransitionCollection transitions)
```

Declared in `ElementExtensions`.

**Applies to:** [`ExpanderElement`](../elements/layout.md#expanderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `transitions` | `TransitionCollection` |  |

## `InteractionStates`

Declares zero-reconcile interaction state visual changes (hover, pressed, focused). The reconciler registers pointer event handlers that drive compositor animations and direct brush swaps — no state variables or re-renders needed.

```csharp
T InteractionStates<T>(this T el, Func<InteractionStatesBuilder, InteractionStatesBuilder> configure, Curve? curve = null) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `configure` | `Func<InteractionStatesBuilder, InteractionStatesBuilder>` |  |  |
| `curve` | [`Curve?`](../animation/curves.md#curve) | `null` |  |

## `ItemContainerTransitions`

Sets ItemContainerTransitions declaratively on ListView, GridView, etc.

```csharp
T ItemContainerTransitions<T>(this T el, params Transition[] transitions) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `transitions` | `Transition[]` |  |

## `Keyframes`

Attaches a trigger-based keyframe animation. The animation plays when the trigger value changes between renders.

```csharp
T Keyframes<T>(this T el, string name, object? trigger, Func<KeyframeBuilder, KeyframeBuilder> configure) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |
| `trigger` | `object?` |  |
| `configure` | `Func<KeyframeBuilder, KeyframeBuilder>` |  |

## `LayoutAnimation`

```csharp
T LayoutAnimation<T>(this T el) where T : Element
T LayoutAnimation<T>(this T el, LayoutAnimationConfig config) where T : Element
T LayoutAnimation<T>(this T el, TimeSpan duration) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `LayoutAnimation<T>(T)` | Enables smooth layout animation: when WinUI repositions this element (e.g., list reorder, grid reflow), it animates from the old position to the new position via the Composition layer. Default duration: 300ms. Elements should have stable keys (.WithKey()) for the reconciler to match them across reorders. |
| `LayoutAnimation<T>(T, LayoutAnimationConfig)` | Enables layout animation with a fully custom configuration. |
| `LayoutAnimation<T>(T, TimeSpan)` | Enables layout animation with a custom duration. |

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `config` | [`LayoutAnimationConfig`](../animation/implicit.md#layoutanimationconfig) |  |
| `duration` | `TimeSpan` |  |

## `OpacityTransition`

Adds an implicit ScalarTransition on Opacity. Applied by the reconciler after .Set() callbacks — always safe to combine.

```csharp
T OpacityTransition<T>(this T el, TimeSpan? duration = null) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `duration` | `TimeSpan?` | `null` |  |

## `RotationTransition`

Adds an implicit ScalarTransition on Rotation.

```csharp
T RotationTransition<T>(this T el, TimeSpan? duration = null) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `duration` | `TimeSpan?` | `null` |  |

## `ScaleTransition`

Adds an implicit Vector3Transition on Scale. Pass a pre-configured transition to set Components for axis-specific animation.

```csharp
T ScaleTransition<T>(this T el, Vector3Transition? transition = null) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `transition` | `Vector3Transition?` | `null` |  |

## `ScrollLinked`

Attaches scroll-linked expression animations driven by a ScrollViewer. Expressions run on the compositor at display refresh rate with zero managed code.

```csharp
T ScrollLinked<T>(this T el, ScrollViewer scrollViewer, Func<ScrollAnimationBuilder, ScrollAnimationBuilder> configure) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `scrollViewer` | `ScrollViewer` |  |
| `configure` | `Func<ScrollAnimationBuilder, ScrollAnimationBuilder>` |  |

## `SpringLayoutAnimation`

Enables layout animation with spring physics for a natural, bouncy feel.

```csharp
T SpringLayoutAnimation<T>(this T el, float dampingRatio = 0.6f, float period = 0.08f) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `dampingRatio` | `float` | `0.6f` |  |
| `period` | `float` | `0.08f` |  |

## `Stagger`

Adds incrementing animation delays to children in a container. Child N gets N \* delay applied to its compositor animations.

```csharp
T Stagger<T>(this T el, TimeSpan delay, Curve? curve = null) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `delay` | `TimeSpan` |  |  |
| `curve` | [`Curve?`](../animation/curves.md#curve) | `null` |  |

## `Transition`

Enables enter/exit transitions when this element is conditionally rendered. Enter: animates from initial state to visible. Exit: animates out before unmount.

```csharp
T Transition<T>(this T el, Transition transition, Curve? curve = null) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `transition` | [`Transition`](../animation/transitions.md#transition_1) |  |  |
| `curve` | [`Curve?`](../animation/curves.md#curve) | `null` |  |

## `TranslationTransition`

Adds an implicit Vector3Transition on Translation. Pass a pre-configured transition to set Components for axis-specific animation.

```csharp
T TranslationTransition<T>(this T el, Vector3Transition? transition = null) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `transition` | `Vector3Transition?` | `null` |  |

## `WithTransitions`

Sets theme transitions declaratively. The reconciler applies ChildrenTransitions on panels, ChildTransitions on borders, ContentTransitions on content controls. Works on any element type.

```csharp
T WithTransitions<T>(this T el, params Transition[] transitions) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `transitions` | `Transition[]` |  |
