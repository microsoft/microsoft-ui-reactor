# ElementPool

Per-CLR-type pool (cap 32 per lane) that recycles unmounted WinUI FrameworkElement instances.

```csharp
public sealed class ElementPool : IDisposable
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Element Pool](../../element-pool.md)

## Remarks

Each type has two lanes (spec 066 §7.2). The plain lane holds controls whose composition visual was never taken. The visual lane holds controls whose visual was taken, reset to a fresh control's pixels by [`ElementPool.ResetTakenVisual`](ElementPool.md). Such a control still refuses the facade transforms and the XAML implicit transitions, so only an element that takes the visual itself and has no setters rents from it.

## Constructors

### `ElementPool`

```csharp
public ElementPool()
```

## Properties

### `Enabled`

When false, TryRent always returns null and Return is a no-op. Useful for scenarios like the live previewer where recycled controls with stale property state can cause visual glitches.

```csharp
public bool Enabled  // get, set
```

## Methods

### `Clear`

Empties all per-type stacks and releases the scratch panel. Called from [`Reconciler.Dispose`](Reconciler.md#dispose) to release pooled elements.

```csharp
public void Clear()
```

### `Dispose`

Releases the resources that this object holds.

```csharp
public void Dispose()
```

### `Return`

Return an element to the pool after unmount. Cleans it first. Silently drops if the type is not poolable or its lane is full.

```csharp
public void Return(FrameworkElement element)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `element` | `FrameworkElement` |  |

### `TryRent`

Try to rent an element of the given type from the plain lane. Returns null if the lane is empty or the type is not poolable.

```csharp
public FrameworkElement? TryRent(Type type)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `type` | `Type` |  |
