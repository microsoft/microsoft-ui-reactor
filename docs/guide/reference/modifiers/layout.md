# Control modifiers: Layout

Modifiers that only the controls in the Layout area have.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Layout](../../layout.md), [Flex Layout](../../flex-layout.md)

## `AnchorRatio`

```csharp
ScrollViewElement AnchorRatio(this ScrollViewElement el, double horizontal, double vertical)
```

Declared in `ElementExtensions`.

**Applies to:** [`ScrollViewElement`](../elements/layout.md#scrollviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `horizontal` | `double` |  |
| `vertical` | `double` |  |

## `ContentOrientation`

```csharp
ScrollViewElement ContentOrientation(this ScrollViewElement el, ScrollingContentOrientation orientation)
```

Declared in `ElementExtensions`.

**Applies to:** [`ScrollViewElement`](../elements/layout.md#scrollviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `orientation` | `ScrollingContentOrientation` |  |

## `Direction`

```csharp
ExpanderElement Direction(this ExpanderElement el, ExpandDirection dir)
```

Declared in `ElementExtensions`.

**Applies to:** [`ExpanderElement`](../elements/layout.md#expanderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `dir` | `ExpandDirection` |  |

## `HeaderContent`

Sets an Element header (overrides the string `Header` from the factory). The mounted child is assigned to WinUI's `Header` property, not its `HeaderTemplate` DataTemplate property.

```csharp
ExpanderElement HeaderContent(this ExpanderElement el, Element header)
```

Declared in `ElementExtensions`.

**Applies to:** [`ExpanderElement`](../elements/layout.md#expanderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `header` | [`Element`](../elements/Element.md) |  |

## `HorizontalScrollMode`

```csharp
ScrollViewElement HorizontalScrollMode(this ScrollViewElement el, ScrollingScrollMode mode)
ScrollViewerElement HorizontalScrollMode(this ScrollViewerElement el, ScrollMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`ScrollViewElement`](../elements/layout.md#scrollviewelement), [`ScrollViewerElement`](../elements/layout.md#scrollviewerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `ScrollingScrollMode` or `ScrollMode` |  |

## `IsExpandedChanged`

Wires the expand-state-changed handler. Passing `null` clears.

```csharp
ExpanderElement IsExpandedChanged(this ExpanderElement el, Action<bool>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`ExpanderElement`](../elements/layout.md#expanderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<bool>?` |  |

## `LightDismissOverlayMode`

How the light-dismiss overlay reacts to taps in Overlay mode.

```csharp
SplitViewElement LightDismissOverlayMode(this SplitViewElement el, LightDismissOverlayMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`SplitViewElement`](../elements/layout.md#splitviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `LightDismissOverlayMode` |  |

## `PaneBackground`

```csharp
SplitViewElement PaneBackground(this SplitViewElement el, ThemeRef theme)
SplitViewElement PaneBackground(this SplitViewElement el, Brush brush)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `PaneBackground(SplitViewElement, ThemeRef)` | Pane background bound to a WinUI theme resource (light/dark adaptive). Resolves via the same ThemeBindings pipeline that powers `Background(ThemeRef)`. |
| `PaneBackground(SplitViewElement, Brush)` | Pane background brush. |

**Applies to:** [`SplitViewElement`](../elements/layout.md#splitviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `theme` | [`ThemeRef`](../theming/ThemeRef.md) |  |
| `brush` | `Brush` |  |

## `Source`

Source UIElement that drives the parallax (typically a ScrollViewer / ListView).

```csharp
ParallaxViewElement Source(this ParallaxViewElement el, UIElement source)
```

Declared in `ElementExtensions`.

**Applies to:** [`ParallaxViewElement`](../elements/layout.md#parallaxviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `source` | `UIElement` |  |

## `Spacing`

```csharp
StackElement Spacing(this StackElement el, double spacing)
```

Declared in `ElementExtensions`.

**Applies to:** [`StackElement`](../elements/layout.md#stackelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `spacing` | `double` |  |

## `Stretch`

```csharp
ViewboxElement Stretch(this ViewboxElement el, Stretch stretch)
```

Declared in `ElementExtensions`.

**Applies to:** [`ViewboxElement`](../elements/layout.md#viewboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `stretch` | `Stretch` |  |

## `StretchDirection`

```csharp
ViewboxElement StretchDirection(this ViewboxElement el, StretchDirection direction)
```

Declared in `ElementExtensions`.

**Applies to:** [`ViewboxElement`](../elements/layout.md#viewboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `direction` | `StretchDirection` |  |

## `VerticalScrollMode`

```csharp
ScrollViewElement VerticalScrollMode(this ScrollViewElement el, ScrollingScrollMode mode)
ScrollViewerElement VerticalScrollMode(this ScrollViewerElement el, ScrollMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`ScrollViewElement`](../elements/layout.md#scrollviewelement), [`ScrollViewerElement`](../elements/layout.md#scrollviewerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `ScrollingScrollMode` or `ScrollMode` |  |

## `VerticalSourceEndOffset`

Vertical-axis source offset (in pixels) at which parallax ends.

```csharp
ParallaxViewElement VerticalSourceEndOffset(this ParallaxViewElement el, double offset)
```

Declared in `ElementExtensions`.

**Applies to:** [`ParallaxViewElement`](../elements/layout.md#parallaxviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `offset` | `double` |  |

## `VerticalSourceStartOffset`

Vertical-axis source offset (in pixels) at which parallax begins.

```csharp
ParallaxViewElement VerticalSourceStartOffset(this ParallaxViewElement el, double offset)
```

Declared in `ElementExtensions`.

**Applies to:** [`ParallaxViewElement`](../elements/layout.md#parallaxviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `offset` | `double` |  |

## `ViewChanged`

```csharp
ScrollViewElement ViewChanged(this ScrollViewElement el, Action? handler)
ScrollViewerElement ViewChanged(this ScrollViewerElement el, Action<ScrollViewerViewChangedEventArgs>? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `ViewChanged(ScrollViewElement, Action?)` | Wires the view-changed handler on the modern [`ScrollViewElement`](../elements/layout.md#scrollviewelement). The modern control reports only settled values. Passing `null` clears. |
| `ViewChanged(ScrollViewerElement, Action<ScrollViewerViewChangedEventArgs>?)` | Wires the view-changed handler. Inspect `args.IsIntermediate` to debounce until the scroll settles. Passing `null` clears. |

**Applies to:** [`ScrollViewElement`](../elements/layout.md#scrollviewelement), [`ScrollViewerElement`](../elements/layout.md#scrollviewerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` or `Action<ScrollViewerViewChangedEventArgs>?` |  |

## `ZoomMode`

```csharp
ScrollViewElement ZoomMode(this ScrollViewElement el, ScrollingZoomMode mode)
ScrollViewerElement ZoomMode(this ScrollViewerElement el, ZoomMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`ScrollViewElement`](../elements/layout.md#scrollviewelement), [`ScrollViewerElement`](../elements/layout.md#scrollviewerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `ScrollingZoomMode` or `ZoomMode` |  |

## `ZoomRange`

```csharp
ScrollViewElement ZoomRange(this ScrollViewElement el, double min, double max)
```

Declared in `ElementExtensions`.

**Applies to:** [`ScrollViewElement`](../elements/layout.md#scrollviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `min` | `double` |  |
| `max` | `double` |  |
