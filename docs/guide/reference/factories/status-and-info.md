# Factories: Status and info

Info bars, badges and progress indicators.

Each factory method returns an element record. Most are on `Factories`, so `using static Microsoft.UI.Reactor.Factories;` covers them. A factory declared on another class names the `using static` it needs.

**Learn more:** [Status and Info](../../status-and-info.md)

## `InfoBadge`

```csharp
InfoBadgeElement InfoBadge()
InfoBadgeElement InfoBadge(int value)
```

**Returns:** [`InfoBadgeElement`](../elements/status-and-info.md#infobadgeelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `int` |  |

## `InfoBar`

```csharp
InfoBarElement InfoBar(string? title = null, string? message = null)
```

**Returns:** [`InfoBarElement`](../elements/status-and-info.md#infobarelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `title` | `string?` | `null` |  |
| `message` | `string?` | `null` |  |

## `Progress`

Creates a determinate [`ProgressElement`](../elements/status-and-info.md#progresselement) at the given value.

```csharp
ProgressElement Progress(double value)
```

**Returns:** [`ProgressElement`](../elements/status-and-info.md#progresselement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `double` |  |

**Remarks**

The element reconciles to a WinUI `ProgressBar`. The Reactor name `Progress` is the short, intent-naming spelling — the WinUI name includes the visual shape (`Bar`) the way agents reach for a rendering primitive; Reactor calls it by what it does. [`ProgressRing`](#progressring) is the circular variant. (spec 039 §5 / §16)

## `ProgressIndeterminate`

Creates an indeterminate [`ProgressElement`](../elements/status-and-info.md#progresselement) (animated bar with no value). The reconciler maps this to `ProgressBar.IsIndeterminate`.

```csharp
ProgressElement ProgressIndeterminate()
```

**Returns:** [`ProgressElement`](../elements/status-and-info.md#progresselement).

**Remarks**

Reactor-original convenience for the indeterminate-bar case; see [`Progress`](#progress) for the naming rationale. (spec 039 §5 / §16)

## `ProgressRing`

```csharp
ProgressRingElement ProgressRing()
ProgressRingElement ProgressRing(double value)
```

**Returns:** [`ProgressRingElement`](../elements/status-and-info.md#progressringelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `double` |  |
