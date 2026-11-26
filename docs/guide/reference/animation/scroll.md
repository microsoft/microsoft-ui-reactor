# Scroll-linked animation

Animations driven by the scroll position.

**Learn more:** [Animation](../../animation.md)

## `ScrollAnimationBuilder`

Fluent builder for constructing scroll-linked expression animations.

```csharp
public class ScrollAnimationBuilder
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `ScrollAnimationBuilder`

```csharp
public ScrollAnimationBuilder()
```

### Methods

#### `Build`

```csharp
public ScrollExpression[] Build()
```

#### `Expression`

Custom expression for advanced scenarios.

```csharp
public ScrollAnimationBuilder Expression(string property, string expression)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `property` | `string` |  |
| `expression` | `string` |  |

#### `FadeIn`

Fade in as scroll position increases from startOffset to endOffset.

```csharp
public ScrollAnimationBuilder FadeIn(float startOffset, float endOffset)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `startOffset` | `float` |  |
| `endOffset` | `float` |  |

#### `FadeOut`

Fade out as scroll position increases from startOffset to endOffset.

```csharp
public ScrollAnimationBuilder FadeOut(float startOffset, float endOffset)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `startOffset` | `float` |  |
| `endOffset` | `float` |  |

#### `Parallax`

Parallax effect: element translates at a fraction of scroll speed.

```csharp
public ScrollAnimationBuilder Parallax(float factor)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `factor` | `float` |  |

#### `ScaleRange`

Scale range: uniform scale interpolates between from and to as scroll moves from scrollStart to scrollEnd.

```csharp
public ScrollAnimationBuilder ScaleRange(float scrollStart, float scrollEnd, float from, float to)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `scrollStart` | `float` |  |
| `scrollEnd` | `float` |  |
| `from` | `float` |  |
| `to` | `float` |  |

## `ScrollAnimationConfig`

Configuration for scroll-linked expression animations.

```csharp
public record ScrollAnimationConfig(ScrollViewer ScrollViewer, ScrollExpression[] Expressions)
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `ScrollAnimationConfig`

Configuration for scroll-linked expression animations.

```csharp
public ScrollAnimationConfig(ScrollViewer ScrollViewer, ScrollExpression[] Expressions)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ScrollViewer` | `ScrollViewer` |  |
| `Expressions` | [`ScrollExpression[]`](#scrollexpression) |  |

### Properties

#### `Expressions`

```csharp
public ScrollExpression[] Expressions  // get, init
```

#### `ScrollViewer`

```csharp
public ScrollViewer ScrollViewer  // get, init
```

## `ScrollExpression`

A single scroll-linked expression animation binding.

```csharp
public record ScrollExpression(string Property, string Expression)
```

Namespace: `Microsoft.UI.Reactor.Animation`.

### Constructors

#### `ScrollExpression`

A single scroll-linked expression animation binding.

```csharp
public ScrollExpression(string Property, string Expression)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Property` | `string` |  |
| `Expression` | `string` |  |

### Properties

#### `Expression`

```csharp
public string Expression  // get, init
```

#### `Property`

```csharp
public string Property  // get, init
```
