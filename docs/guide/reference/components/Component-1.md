# Component\<TProps\>

Base class for components that receive typed props (e.g., navigation parameters). Props are set by the host before rendering.

```csharp
public abstract class Component<TProps> : Component
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Component`](Component.md).

**Learn more:** [Components](../../components.md)

## Constructors

### `Component`

```csharp
protected Component()
```

## Properties

### `Props`

The typed props passed to this component by its parent or host.

```csharp
public TProps Props  // get
```

## Methods

### `ShouldUpdate`

Controls whether this component should re-render when its parent re-renders with new props. Default: structural equality via record Equals — record props get auto-comparison for free; class props need an Equals override. Override for custom comparison logic.

```csharp
protected virtual bool ShouldUpdate(TProps? oldProps, TProps? newProps)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `oldProps` | `TProps?` |  |
| `newProps` | `TProps?` |  |
