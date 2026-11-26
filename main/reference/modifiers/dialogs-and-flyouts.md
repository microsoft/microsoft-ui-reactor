---
title: "Control modifiers: Dialogs and flyouts"
description: "Modifiers that only the controls in the Dialogs and flyouts area have."
sidebar:
  label: "Dialogs and flyouts"
---

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Dialogs and Flyouts](/microsoft-ui-reactor/main/controls/dialogs-and-flyouts)

## `AreOpenCloseAnimationsEnabled` [#areopencloseanimationsenabled]

Whether the flyout animates on open/close.

```csharp
FlyoutElement AreOpenCloseAnimationsEnabled(this FlyoutElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`FlyoutElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#flyoutelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `HeroContent` [#herocontent]

Optional "hero" Element (image / banner) rendered above the title.

```csharp
TeachingTipElement HeroContent(this TeachingTipElement el, Element hero)
```

Declared in `ElementExtensions`.

**Applies to:** [`TeachingTipElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#teachingtipelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `hero` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `IsLightDismissEnabled` [#islightdismissenabled]

Sets whether the Popup closes on outside interaction.

```csharp
PopupElement IsLightDismissEnabled(this PopupElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`PopupElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#popupelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `IsPrimaryButtonEnabled` [#isprimarybuttonenabled]

Enables/disables the primary button while the dialog is open.

```csharp
ContentDialogElement IsPrimaryButtonEnabled(this ContentDialogElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ContentDialogElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#contentdialogelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `IsSecondaryButtonEnabled` [#issecondarybuttonenabled]

Enables/disables the secondary button while the dialog is open.

```csharp
ContentDialogElement IsSecondaryButtonEnabled(this ContentDialogElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`ContentDialogElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#contentdialogelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `LightDismiss` [#lightdismiss]

Sets whether the Popup closes on outside interaction.

```csharp
PopupElement LightDismiss(this PopupElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsLightDismissEnabled() — aligns with WinUI naming (see #268).

**Applies to:** [`PopupElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#popupelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `Offset` [#offset]

```csharp
PopupElement Offset(this PopupElement el, double horizontal, double vertical)
```

Declared in `ElementExtensions`.

**Applies to:** [`PopupElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#popupelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `horizontal` | `double` |  |
| `vertical` | `double` |  |

## `Opened` [#opened]

```csharp
ContentDialogElement Opened(this ContentDialogElement el, Action? handler)
FlyoutElement Opened(this FlyoutElement el, Action? handler)
PopupElement Opened(this PopupElement el, Action? handler)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Opened(ContentDialogElement, Action?)` | Wires the opened handler (fires after the dialog finishes opening). Passing `null` clears. |
| `Opened(FlyoutElement, Action?)` | Wires the opened handler. Passing `null` clears. |
| `Opened(PopupElement, Action?)` | Wires the opened handler. Passing `null` clears. |

**Applies to:** [`ContentDialogElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#contentdialogelement), [`FlyoutElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#flyoutelement), [`PopupElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#popupelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `OverlayInputPassThroughElement` [#overlayinputpassthroughelement]

Element whose input passes through the light-dismiss overlay.

```csharp
FlyoutElement OverlayInputPassThroughElement(this FlyoutElement el, Element passThrough)
```

Declared in `ElementExtensions`.

**Applies to:** [`FlyoutElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#flyoutelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `passThrough` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `PlacementMargin` [#placementmargin]

Extra margin around the tip when placed relative to its target.

```csharp
TeachingTipElement PlacementMargin(this TeachingTipElement el, Thickness margin)
```

Declared in `ElementExtensions`.

**Applies to:** [`TeachingTipElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#teachingtipelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `margin` | `Thickness` |  |

## `PreferredPlacement` [#preferredplacement]

Preferred placement edge.

```csharp
TeachingTipElement PreferredPlacement(this TeachingTipElement el, TeachingTipPlacementMode placement)
```

Declared in `ElementExtensions`.

**Applies to:** [`TeachingTipElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#teachingtipelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `placement` | `TeachingTipPlacementMode` |  |

## `ShowMode` [#showmode]

How the flyout reacts to outside clicks (Auto / Standard / Transient / TransientWithDismissOnPointerMoveAway).

```csharp
FlyoutElement ShowMode(this FlyoutElement el, FlyoutShowMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`FlyoutElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#flyoutelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `FlyoutShowMode` |  |

## `Target` [#target]

Control this tip anchors to, resolved by ElementRef regardless of mount order.

```csharp
TeachingTipElement Target(this TeachingTipElement el, ElementRef target)
```

Declared in `ElementExtensions`.

**Applies to:** [`TeachingTipElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#teachingtipelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | [`ElementRef`](/microsoft-ui-reactor/main/reference/input/element-refs#elementref) |  |
