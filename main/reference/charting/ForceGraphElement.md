---
title: "ForceGraphElement"
description: "Force-directed graph element for Reactor's virtual tree. Pure renderer — draws nodes, links, labels from a ForceSimulation's current state. Interaction (drag, animation) is the caller's responsibility."
---

Note: ForceGraph intentionally uses XamlHostElement for 60fps direct manipulation via SyncPositions(). See docs/specs/016-native-chart-migration.md §5, Option A.

```csharp
public sealed class ForceGraphElement
```

Namespace: `Microsoft.UI.Reactor.Charting`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Charting](/microsoft-ui-reactor/main/controls/charting)

## Constructors [#constructors]

### `ForceGraphElement` [#forcegraphelement_1]

```csharp
public ForceGraphElement()
```

## Methods [#methods]

### `AlternateView` [#alternateview]

Enables alternate-view toggle (T / Alt+Shift+F11).

```csharp
public ForceGraphElement AlternateView(Element view)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `view` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

### `Charge` [#charge]

```csharp
public ForceGraphElement Charge(double strength)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `strength` | `double` |  |

### `Description` [#description]

Overrides auto-generated accessible description/summary.

```csharp
public ForceGraphElement Description(string description)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `description` | `string` |  |

### `Distance` [#distance]

```csharp
public ForceGraphElement Distance(double distance)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `distance` | `double` |  |

### `Height` [#height]

```csharp
public ForceGraphElement Height(double height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `height` | `double` |  |

### `Iterations` [#iterations]

```csharp
public ForceGraphElement Iterations(int count)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `count` | `int` |  |

### `LinkColor` [#linkcolor]

```csharp
public ForceGraphElement LinkColor(string color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `color` | `string` |  |

### `NodeColor` [#nodecolor]

```csharp
public ForceGraphElement NodeColor(string color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `color` | `string` |  |

### `OnReady` [#onready]

Called after the graph is rendered with a handle that exposes the simulation, WinUI elements, and a `SyncPositions` method. Use this to wire up drag behaviour, animation timers, or anything else from the call site.

```csharp
public ForceGraphElement OnReady(Action<ForceGraphHandle> callback)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `callback` | `Action<ForceGraphHandle>` |  |

### `Title` [#title]

Sets visible title + accessible name for the graph.

```csharp
public ForceGraphElement Title(string title)
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
public ForceGraphElement Width(double width)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |

## Operators [#operators]

### `op_Implicit` [#op_implicit]

```csharp
public static implicit operator Element(ForceGraphElement chart)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `chart` | [`ForceGraphElement`](/microsoft-ui-reactor/main/reference/charting/ForceGraphElement) |  |
