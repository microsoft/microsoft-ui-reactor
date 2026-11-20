# Modifiers: Tooltips and flyouts

Tooltips, flyouts and context flyouts attached to any element.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Dialogs and Flyouts](../../dialogs-and-flyouts.md)

## `ToolTip`

Text tooltip with an explicit `ToolTipService.Placement`. Usage: `Button("Save", onSave).ToolTip("Save (Ctrl+S)", PlacementMode.Right)`

```csharp
T ToolTip<T>(this T el, string tip) where T : Element
T ToolTip<T>(this T el, string tip, PlacementMode placement) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `tip` | `string` |  |
| `placement` | `PlacementMode` |  |

## `ToolTipPlacement`

Sets `ToolTipService.Placement` — which side of the element the tooltip opens on. Pairs with either [`ToolTip`](#tooltip) or [`WithToolTip`](#withtooltip).

`PlacementMode` is the tooltip-specific enum and has exactly five members — `Top`, `Bottom`, `Left`, `Right`, `Mouse`. The edge-aligned placements (`TopEdgeAlignedLeft` and friends) live on the separate `FlyoutPlacementMode` enum and are not expressible for tooltips — use a flyout if you need them.

```csharp
T ToolTipPlacement<T>(this T el, PlacementMode placement) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `placement` | `PlacementMode` |  |

## `ToolTipPlacementTarget`

Sets `ToolTipService.PlacementTarget` — position the tooltip relative to another element instead of the one it is attached to. Take the ref with `UseElementRef<T>()` and attach it to the target via `.Ref(...)`.

```csharp
T ToolTipPlacementTarget<T>(this T el, ElementRef target) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | [`ElementRef`](../input/element-refs.md#elementref) |  |

## `WithContextFlyout`

```csharp
T WithContextFlyout<T>(this T el, Element contextFlyout) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `contextFlyout` | [`Element`](../elements/Element.md) |  |

## `WithFlyout`

```csharp
T WithFlyout<T>(this T el, Element flyout) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `flyout` | [`Element`](../elements/Element.md) |  |

## `WithToolTip`

Rich (Element content) tooltip with an explicit `ToolTipService.Placement`.

```csharp
T WithToolTip<T>(this T el, Element tooltip) where T : Element
T WithToolTip<T>(this T el, Element tooltip, PlacementMode placement) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `tooltip` | [`Element`](../elements/Element.md) |  |
| `placement` | `PlacementMode` |  |
