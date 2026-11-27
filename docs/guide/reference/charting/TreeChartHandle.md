# TreeChartHandle

Handle returned by OnReady — exposes the underlying Canvas for escape-hatch scenarios.

```csharp
public sealed class TreeChartHandle
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

Re-renders the tree with new data. Prefer state-driven re-renders instead.

```csharp
public void Redraw<T>(T root)
```

> **Obsolete.** Use state-driven re-renders (e.g. setRoot(newRoot)) instead of TreeChartHandle.Redraw. Tree charts are now native Reactor elements that diff efficiently.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | `T` |  |
