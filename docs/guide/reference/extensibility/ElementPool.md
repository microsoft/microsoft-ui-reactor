# ElementPool

Per-CLR-type pool (cap 32) that recycles unmounted WinUI FrameworkElement instances. V1: pools only non-interactive controls (no event handlers to worry about).

```csharp
public sealed class ElementPool : IDisposable
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Element Pool](../../element-pool.md)

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

Return an element to the pool after unmount. Cleans it first. Silently drops if the type is not poolable or the pool is full.

```csharp
public void Return(FrameworkElement element)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `element` | `FrameworkElement` |  |

### `TryRent`

Try to rent an element of the given type from the pool. Returns null if the pool is empty or the type is not poolable.

```csharp
public FrameworkElement? TryRent(Type type)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `type` | `Type` |  |
