---
title: "Virtual list"
description: "Support types for the virtual list."
---

**Learn more:** [Controls](/microsoft-ui-reactor/main/controls)

## `SearchManager<T>` [#searchmanagert]

Manages async search with debounce and cancellation for AutoSuggest.

```csharp
public sealed class SearchManager<T> : IDisposable
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Constructors [#constructors]

#### `SearchManager` [#searchmanager]

```csharp
public SearchManager(Func<string, CancellationToken, Task<IReadOnlyList<T>>> search, int debounceMs = 300)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `search` | `Func<string, CancellationToken, Task<IReadOnlyList<T>>>` |  |  |
| `debounceMs` | `int` | `300` |  |

### Properties [#properties]

#### `ErrorText` [#errortext]

```csharp
public string? ErrorText  // get
```

#### `Results` [#results]

```csharp
public IReadOnlyList<T> Results  // get
```

#### `State` [#state]

```csharp
public SearchState State  // get
```

### Methods [#methods]

#### `Cancel` [#cancel]

Cancels any in-flight search and resets state.

```csharp
public void Cancel()
```

#### `Dispose` [#dispose]

Releases the resources that this object holds.

```csharp
public void Dispose()
```

#### `Search` [#search]

Triggers a debounced search. Cancels any in-flight search.

```csharp
public void Search(string query)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `query` | `string` |  |

### Events [#events]

#### `StateChanged` [#statechanged]

Fired when state changes (for triggering re-renders).

```csharp
public event Action? StateChanged
```

## `SearchState` [#searchstate]

State for the search operation: loading, results, error, or empty.

```csharp
public enum SearchState
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `Idle` | `0` |  |
| `Loading` | `1` |  |
| `Results` | `2` |  |
| `Empty` | `3` |  |
| `Error` | `4` |  |

## `VirtualListComponent` [#virtuallistcomponent]

Component that renders a virtualized list using WinUI's ItemsRepeater. Adapts the count-based VirtualListElement API to the ItemsRepeater infrastructure, supporting both fixed-height (O(1) offset) and variable-height modes.

```csharp
public class VirtualListComponent : Component<VirtualListElement>
```

Namespace: `Microsoft.UI.Reactor.Controls`. Inherits from [`Component<TProps>`](/microsoft-ui-reactor/main/reference/components/Component-1).

### Constructors [#constructors_1]

#### `VirtualListComponent` [#virtuallistcomponent_1]

```csharp
public VirtualListComponent()
```

### Methods [#methods_1]

#### `Render` [#render]

```csharp
public override Element Render()
```

## `VirtualListRef` [#virtuallistref]

Imperative handle for VirtualList scroll operations. Obtained via the VirtualListElement.Ref callback.

```csharp
public sealed class VirtualListRef
```

Namespace: `Microsoft.UI.Reactor.Controls`.

### Properties [#properties_1]

#### `Repeater` [#repeater]

The underlying ItemsRepeater, for advanced scenarios.

```csharp
public ItemsRepeater? Repeater  // get
```

#### `ScrollOffset` [#scrolloffset]

Gets the current vertical scroll offset.

```csharp
public double ScrollOffset  // get
```

### Methods [#methods_2]

#### `RestoreScrollOffset` [#restorescrolloffset]

Restores a previously saved scroll offset.

```csharp
public void RestoreScrollOffset(double offset)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `offset` | `double` |  |

#### `ScrollToIndex` [#scrolltoindex]

Programmatically scroll to bring the item at the given index into view.

```csharp
public void ScrollToIndex(int index)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |
