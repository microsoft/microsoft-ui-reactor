# Factories: Dialogs and flyouts

Content dialogs, flyouts, popups and teaching tips.

Each factory method returns an element record. Call them through `using static Microsoft.UI.Reactor.Factories;`.

**Learn more:** [Dialogs and Flyouts](../../dialogs-and-flyouts.md)

## `ContentDialog`

```csharp
ContentDialogElement ContentDialog(string title, Element content, string primaryButtonText = "OK")
```

**Returns:** [`ContentDialogElement`](../elements/dialogs-and-flyouts.md#contentdialogelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `title` | `string` |  |  |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `primaryButtonText` | `string` | `"OK"` |  |

## `ContentFlyout`

```csharp
ContentFlyoutElement ContentFlyout(Element content, FlyoutPlacementMode placement = (FlyoutPlacementMode)13)
```

**Returns:** [`ContentFlyoutElement`](../elements/dialogs-and-flyouts.md#contentflyoutelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `placement` | `FlyoutPlacementMode` | `(FlyoutPlacementMode)13` |  |

## `Flyout`

```csharp
FlyoutElement Flyout(Element target, Element flyoutContent)
```

**Returns:** [`FlyoutElement`](../elements/dialogs-and-flyouts.md#flyoutelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | [`Element`](../elements/Element.md) |  |
| `flyoutContent` | [`Element`](../elements/Element.md) |  |

## `Popup`

```csharp
PopupElement Popup(Element child, bool isOpen = false, Action? onClosed = null)
```

**Returns:** [`PopupElement`](../elements/dialogs-and-flyouts.md#popupelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `child` | [`Element`](../elements/Element.md) |  |  |
| `isOpen` | `bool` | `false` |  |
| `onClosed` | `Action?` | `null` |  |

## `TeachingTip`

```csharp
TeachingTipElement TeachingTip(string title, string? subtitle = null, ElementRef? target = null)
```

**Returns:** [`TeachingTipElement`](../elements/dialogs-and-flyouts.md#teachingtipelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `title` | `string` |  |  |
| `subtitle` | `string?` | `null` |  |
| `target` | [`ElementRef?`](../input/element-refs.md#elementref) | `null` |  |
