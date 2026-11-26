# ForceGraphHandle

Exposes the live simulation and WinUI elements so callers can implement drag, animation, hover, or any other interaction outside the library.

```csharp
public sealed class ForceGraphHandle
```

Namespace: `Microsoft.UI.Reactor.Charting`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Charting](../../charting.md)

## Properties

### `Canvas`

```csharp
public Canvas Canvas  // get
```

### `LinkLines`

```csharp
public Line[] LinkLines  // get
```

### `NodeEllipses`

```csharp
public Ellipse[] NodeEllipses  // get
```

### `NodeLabels`

```csharp
public TextBlock?[] NodeLabels  // get
```

### `Simulation`

```csharp
public ForceSimulation Simulation  // get
```

## Methods

### `SyncPositions`

Pushes current ForceNode positions into the WinUI elements (ellipses, labels, link endpoints). Call this after Simulation.Tick() to animate the display.

```csharp
public void SyncPositions()
```
