---
title: "TreeChartElement<T>"
description: "Tree diagram element for Reactor's virtual tree — renders as native D3 elements."
sidebar:
  label: "TreeChartElement"
---

```csharp
public sealed class TreeChartElement<T>
```

Namespace: `Microsoft.UI.Reactor.Charting`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Charting](/microsoft-ui-reactor/main/controls/charting)

## Constructors [#constructors]

### `TreeChartElement` [#treechartelement]

```csharp
public TreeChartElement()
```

## Methods [#methods]

### `AlternateView` [#alternateview]

Enables alternate-view toggle (T / Alt+Shift+F11).

```csharp
public TreeChartElement<T> AlternateView(Element view)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `view` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

### `Description` [#description]

Overrides auto-generated accessible description/summary.

```csharp
public TreeChartElement<T> Description(string description)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `description` | `string` |  |

### `Height` [#height]

```csharp
public TreeChartElement<T> Height(double height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `height` | `double` |  |

### `LinkColor` [#linkcolor]

```csharp
public TreeChartElement<T> LinkColor(string color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `color` | `string` |  |

### `NodeColor` [#nodecolor]

```csharp
public TreeChartElement<T> NodeColor(string color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `color` | `string` |  |

### `NodeRadius` [#noderadius]

```csharp
public TreeChartElement<T> NodeRadius(double radius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `radius` | `double` |  |

### `OnReady` [#onready]

```csharp
public TreeChartElement<T> OnReady(Action<TreeChartHandle> callback)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `callback` | `Action<TreeChartHandle>` |  |

### `Title` [#title]

Sets visible title + accessible name for the chart.

```csharp
public TreeChartElement<T> Title(string title)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `title` | `string` |  |

### `ToElement` [#toelement]

```csharp
public Element ToElement()
```

### `Width` [#width]

```csharp
public TreeChartElement<T> Width(double width)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |

## Operators [#operators]

### `op_Implicit` [#op_implicit]

```csharp
public static implicit operator Element(TreeChartElement<T> chart)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `chart` | [`TreeChartElement<T>`](/microsoft-ui-reactor/main/reference/charting/TreeChartElement) |  |
