# NavigationEventArgs\<TRoute\>

Event args fired after a successful navigation.

```csharp
public sealed record NavigationEventArgs<TRoute>(TRoute Route, TRoute PreviousRoute, NavigationMode Mode, NavigateOptions? Options = null)
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

**Learn more:** [Navigation](../../navigation.md)

## Constructors

### `NavigationEventArgs`

Event args fired after a successful navigation.

```csharp
public NavigationEventArgs(TRoute Route, TRoute PreviousRoute, NavigationMode Mode, NavigateOptions? Options = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Route` | `TRoute` |  |  |
| `PreviousRoute` | `TRoute` |  |  |
| `Mode` | [`NavigationMode`](enums.md#navigationmode) |  |  |
| `Options` | [`NavigateOptions?`](NavigateOptions.md) | `null` |  |

## Properties

### `Mode`

```csharp
public NavigationMode Mode  // get, init
```

### `Options`

```csharp
public NavigateOptions? Options  // get, init
```

### `PreviousRoute`

```csharp
public TRoute PreviousRoute  // get, init
```

### `Route`

```csharp
public TRoute Route  // get, init
```
