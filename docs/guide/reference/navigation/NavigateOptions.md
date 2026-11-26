# NavigateOptions

Options for a single navigation action.

```csharp
public sealed record NavigateOptions
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

**Learn more:** [Navigation](../../navigation.md)

## Constructors

### `NavigateOptions`

```csharp
public NavigateOptions()
```

## Properties

### `ConnectedAnimationKey`

Optional key of the shared element on both pages. Independent of [`NavigateOptions.Transition`](#transition); use [`NavigationTransition.None`](transitions.md#none) for shared-element motion without an accompanying page transition. The key and explicit transition are remembered for back/forward navigation within this session, but are not part of serialized navigation state.

```csharp
public string? ConnectedAnimationKey  // get, init
```

### `PushToBackStack`

When true (default), the current route is pushed onto the back stack. When false, the current route is replaced (no back stack entry created).

```csharp
public bool PushToBackStack  // get, init
```

### `Transition`

Transition override for this navigation. Null uses the host default.

```csharp
public NavigationTransition? Transition  // get, init
```
