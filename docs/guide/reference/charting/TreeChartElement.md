# TreeChartElement\<T\>

Tree diagram element for Reactor's virtual tree — renders as native D3 elements.

```csharp
public sealed class TreeChartElement<T>
```

Namespace: `Microsoft.UI.Reactor.Charting`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Charting](../../charting.md)

## Constructors

### `TreeChartElement`

```csharp
public TreeChartElement()
```

## Methods

### `AlternateView`

Enables alternate-view toggle (T / Alt+Shift+F11).

```csharp
public TreeChartElement<T> AlternateView(Element view)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `view` | [`Element`](../elements/Element.md) |  |

### `Description`

Overrides auto-generated accessible description/summary.

```csharp
public TreeChartElement<T> Description(string description)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `description` | `string` |  |

### `Height`

```csharp
public TreeChartElement<T> Height(double height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `height` | `double` |  |

### `LinkColor`

```csharp
public TreeChartElement<T> LinkColor(string color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `color` | `string` |  |

### `NodeColor`

```csharp
public TreeChartElement<T> NodeColor(string color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `color` | `string` |  |

### `NodeRadius`

```csharp
public TreeChartElement<T> NodeRadius(double radius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `radius` | `double` |  |

### `OnReady`

```csharp
public TreeChartElement<T> OnReady(Action<TreeChartHandle> callback)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `callback` | `Action<TreeChartHandle>` |  |

### `Title`

Sets visible title + accessible name for the chart.

```csharp
public TreeChartElement<T> Title(string title)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `title` | `string` |  |

### `ToElement`

```csharp
public Element ToElement()
```

### `Width`

```csharp
public TreeChartElement<T> Width(double width)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |

## Operators

### `op_Implicit`

```csharp
public static implicit operator Element(TreeChartElement<T> chart)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `chart` | [`TreeChartElement<T>`](TreeChartElement.md) |  |
