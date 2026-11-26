---
title: "Factories: Status and info"
description: "Info bars, badges and progress indicators."
sidebar:
  label: "Status and info"
---

Each factory method returns an element record. Most are on `Factories`, so `using static Microsoft.UI.Reactor.Factories;` covers them. A factory declared on another class names the `using static` it needs.

**Learn more:** [Status and Info](/microsoft-ui-reactor/main/controls/status-and-info)

## `InfoBadge` [#infobadge]

```csharp
InfoBadgeElement InfoBadge()
InfoBadgeElement InfoBadge(int value)
```

**Returns:** [`InfoBadgeElement`](/microsoft-ui-reactor/main/reference/elements/status-and-info#infobadgeelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `int` |  |

## `InfoBar` [#infobar]

```csharp
InfoBarElement InfoBar(string? title = null, string? message = null)
```

**Returns:** [`InfoBarElement`](/microsoft-ui-reactor/main/reference/elements/status-and-info#infobarelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `title` | `string?` | `null` |  |
| `message` | `string?` | `null` |  |

## `Progress` [#progress]

Creates a determinate [`ProgressElement`](/microsoft-ui-reactor/main/reference/elements/status-and-info#progresselement) at the given value.

```csharp
ProgressElement Progress(double value)
```

**Returns:** [`ProgressElement`](/microsoft-ui-reactor/main/reference/elements/status-and-info#progresselement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `double` |  |

**Remarks**

The element reconciles to a WinUI `ProgressBar`. The Reactor name `Progress` is the short, intent-naming spelling — the WinUI name includes the visual shape (`Bar`) the way agents reach for a rendering primitive; Reactor calls it by what it does. [`ProgressRing`](#progressring) is the circular variant. (spec 039 §5 / §16)

## `ProgressIndeterminate` [#progressindeterminate]

Creates an indeterminate [`ProgressElement`](/microsoft-ui-reactor/main/reference/elements/status-and-info#progresselement) (animated bar with no value). The reconciler maps this to `ProgressBar.IsIndeterminate`.

```csharp
ProgressElement ProgressIndeterminate()
```

**Returns:** [`ProgressElement`](/microsoft-ui-reactor/main/reference/elements/status-and-info#progresselement).

**Remarks**

Reactor-original convenience for the indeterminate-bar case; see [`Progress`](#progress) for the naming rationale. (spec 039 §5 / §16)

## `ProgressRing` [#progressring]

```csharp
ProgressRingElement ProgressRing()
ProgressRingElement ProgressRing(double value)
```

**Returns:** [`ProgressRingElement`](/microsoft-ui-reactor/main/reference/elements/status-and-info#progressringelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `double` |  |
