# Implicit and theme transitions

Transitions WinUI runs when a property or a child collection changes.

**Learn more:** [Animation](../../animation.md)

## `ImplicitTransitions`

Declarative implicit transition configuration for a UIElement. Each property maps to a WinUI implicit transition property on UIElement/Panel. Null means "don't set this transition".

```csharp
public record ImplicitTransitions
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `ImplicitTransitions`

```csharp
public ImplicitTransitions()
```

### Properties

#### `Background`

```csharp
public BrushTransition? Background  // get, init
```

#### `Opacity`

```csharp
public ScalarTransition? Opacity  // get, init
```

#### `Rotation`

```csharp
public ScalarTransition? Rotation  // get, init
```

#### `Scale`

```csharp
public Vector3Transition? Scale  // get, init
```

#### `Translation`

```csharp
public Vector3Transition? Translation  // get, init
```

## `LayoutAnimationConfig`

Configuration for Composition-layer layout animations. When applied to an element, the reconciler sets up implicit animations on the element's Visual so that layout-driven Offset (position) and optionally Size changes animate smoothly. Runs entirely on the Composition thread — zero managed-code involvement during animation.

Limitations: - Hit-testing uses the final layout position, not the animated visual position. - Elements must have stable keys (.WithKey()) for the reconciler to match them across reorders. - Size animation is cosmetic: content does not re-layout during the Size animation. - Only handles position changes for persistent elements; use theme transitions for enter/exit.

```csharp
public record LayoutAnimationConfig
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `LayoutAnimationConfig`

```csharp
public LayoutAnimationConfig()
```

### Properties

#### `AnimateOffset`

Animate Offset (position) changes. Default: true.

```csharp
public bool AnimateOffset  // get, init
```

#### `AnimateSize`

Animate Size changes. Default: false (content won't re-layout during animation).

```csharp
public bool AnimateSize  // get, init
```

#### `DampingRatio`

Spring damping ratio (0..1). Only used when UseSpring is true. Default: 0.6.

```csharp
public float DampingRatio  // get, init
```

#### `Duration`

Duration of the layout animation. Default: 300ms.

```csharp
public TimeSpan Duration  // get, init
```

#### `Period`

Spring period in seconds. Only used when UseSpring is true. Default: 0.08.

```csharp
public float Period  // get, init
```

#### `UseSpring`

When true, use a spring natural motion animation instead of linear keyframes.

```csharp
public bool UseSpring  // get, init
```

## `ThemeTransitions`

Declarative theme transition configuration. Children applies to Panel.ChildrenTransitions / Border.ChildTransitions / ContentControl.ContentTransitions. ItemContainer applies to ItemsControl.ItemContainerTransitions. The reconciler picks the correct property based on control type.

```csharp
public record ThemeTransitions
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `ThemeTransitions`

```csharp
public ThemeTransitions()
```

### Properties

#### `Children`

```csharp
public Transition[]? Children  // get, init
```

#### `ItemContainer`

```csharp
public Transition[]? ItemContainer  // get, init
```
