---
title: "Modifiers: Tooltips and flyouts"
description: "Tooltips, flyouts and context flyouts attached to any element."
sidebar:
  label: "Tooltips and flyouts"
---

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Dialogs and Flyouts](/microsoft-ui-reactor/main/controls/dialogs-and-flyouts)

## `ToolTip` [#tooltip]

```csharp
T ToolTip<T>(this T el, string tip) where T : Element
T ToolTip<T>(this T el, string tip, PlacementMode placement) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `ToolTip<T>(T, string)` |  |
| `ToolTip<T>(T, string, PlacementMode)` | Text tooltip with an explicit `ToolTipService.Placement`. Usage: `Button("Save", onSave).ToolTip("Save (Ctrl+S)", PlacementMode.Right)` |

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `tip` | `string` |  |
| `placement` | `PlacementMode` |  |

## `ToolTipPlacement` [#tooltipplacement]

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

## `ToolTipPlacementTarget` [#tooltipplacementtarget]

Sets `ToolTipService.PlacementTarget` — position the tooltip relative to another element instead of the one it is attached to. Take the ref with `UseElementRef<T>()` and attach it to the target via `.Ref(...)`.

```csharp
T ToolTipPlacementTarget<T>(this T el, ElementRef target) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | [`ElementRef`](/microsoft-ui-reactor/main/reference/input/element-refs#elementref) |  |

## `WithContextFlyout` [#withcontextflyout]

```csharp
T WithContextFlyout<T>(this T el, Element contextFlyout) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `contextFlyout` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `WithFlyout` [#withflyout]

```csharp
T WithFlyout<T>(this T el, Element flyout) where T : Element
```

Declared in `ElementExtensions`.

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `flyout` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `WithToolTip` [#withtooltip]

```csharp
T WithToolTip<T>(this T el, Element tooltip) where T : Element
T WithToolTip<T>(this T el, Element tooltip, PlacementMode placement) where T : Element
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `WithToolTip<T>(T, Element)` |  |
| `WithToolTip<T>(T, Element, PlacementMode)` | Rich (Element content) tooltip with an explicit `ToolTipService.Placement`. |

**Applies to:** any element.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `tooltip` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `placement` | `PlacementMode` |  |
