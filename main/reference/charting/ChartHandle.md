---
title: "ChartHandle<T>"
description: "Handle returned by OnReady — exposes the underlying Canvas for escape-hatch scenarios."
sidebar:
  label: "ChartHandle"
---

```csharp
public sealed class ChartHandle<T>
```

Namespace: `Microsoft.UI.Reactor.Charting`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Charting](/microsoft-ui-reactor/main/controls/charting)

## Properties [#properties]

### `Canvas` [#canvas]

```csharp
public Canvas Canvas  // get
```

## Methods [#methods]

### `Redraw` [#redraw]

Re-renders the chart with new data. Prefer state-driven re-renders instead.

```csharp
public void Redraw(IReadOnlyList<T> data)
```

> **Obsolete.** Use state-driven re-renders (e.g. setData(newData)) instead of ChartHandle.Redraw. Charts are now native Reactor elements that diff efficiently.

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |
