---
title: "Factories: Dialogs and flyouts"
description: "Content dialogs, flyouts, popups and teaching tips."
sidebar:
  label: "Dialogs and flyouts"
---

Each factory method returns an element record. Most are on `Factories`, so `using static Microsoft.UI.Reactor.Factories;` covers them. A factory declared on another class names the `using static` it needs.

**Learn more:** [Dialogs and Flyouts](/microsoft-ui-reactor/main/controls/dialogs-and-flyouts)

## `ContentDialog` [#contentdialog]

```csharp
ContentDialogElement ContentDialog(string title, Element content, string primaryButtonText = "OK")
```

**Returns:** [`ContentDialogElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#contentdialogelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `title` | `string` |  |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `primaryButtonText` | `string` | `"OK"` |  |

## `ContentFlyout` [#contentflyout]

```csharp
ContentFlyoutElement ContentFlyout(Element content, FlyoutPlacementMode placement = (FlyoutPlacementMode)13)
```

**Returns:** [`ContentFlyoutElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#contentflyoutelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `placement` | `FlyoutPlacementMode` | `(FlyoutPlacementMode)13` |  |

## `Flyout` [#flyout]

```csharp
FlyoutElement Flyout(Element target, Element flyoutContent)
```

**Returns:** [`FlyoutElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#flyoutelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `flyoutContent` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `Popup` [#popup]

```csharp
PopupElement Popup(Element child, bool isOpen = false, Action? onClosed = null)
```

**Returns:** [`PopupElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#popupelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `child` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `isOpen` | `bool` | `false` |  |
| `onClosed` | `Action?` | `null` |  |

## `TeachingTip` [#teachingtip]

```csharp
TeachingTipElement TeachingTip(string title, string? subtitle = null, ElementRef? target = null)
```

**Returns:** [`TeachingTipElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#teachingtipelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `title` | `string` |  |  |
| `subtitle` | `string?` | `null` |  |
| `target` | [`ElementRef?`](/microsoft-ui-reactor/main/reference/input/element-refs#elementref) | `null` |  |
