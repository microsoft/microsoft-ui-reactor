# PieChartHandle\<T\>

Handle returned by OnReady — exposes the underlying Canvas for escape-hatch scenarios.

```csharp
public sealed class PieChartHandle<T>
```

Namespace: `Microsoft.UI.Reactor.Charting`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Charting](../../charting.md)

## Properties

### `Canvas`

```csharp
public Canvas Canvas  // get
```

## Methods

### `Redraw`

Re-renders the chart with new data. Prefer state-driven re-renders instead.

```csharp
public void Redraw(IReadOnlyList<T> data)
```

> **Obsolete.** Use state-driven re-renders (e.g. setData(newData)) instead of PieChartHandle.Redraw. Charts are now native Reactor elements that diff efficiently.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |
