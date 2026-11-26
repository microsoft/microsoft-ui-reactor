# Navigation lifecycle

Contexts passed to the navigation lifecycle callbacks.

**Learn more:** [Navigation](../../navigation.md)

## `NavigatedFromContext`

Context passed to `onNavigatedFrom` lifecycle callbacks after a page is no longer active.

```csharp
public sealed class NavigatedFromContext
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Properties

#### `Mode`

```csharp
public NavigationMode Mode  // get
```

#### `Route`

```csharp
public object Route  // get
```

#### `TargetRoute`

```csharp
public object TargetRoute  // get
```

## `NavigatedToContext`

Context passed to `onNavigatedTo` lifecycle callbacks after a page becomes active.

```csharp
public sealed class NavigatedToContext
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Properties

#### `Mode`

```csharp
public NavigationMode Mode  // get
```

#### `PreviousRoute`

```csharp
public object? PreviousRoute  // get
```

#### `Route`

```csharp
public object Route  // get
```

## `NavigatingFromContext`

Context passed to navigation guards before a navigation occurs. Call [`NavigatingFromContext.Cancel`](#cancel) to prevent the navigation.

```csharp
public sealed class NavigatingFromContext
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Properties

#### `IsCancelled`

```csharp
public bool IsCancelled  // get
```

#### `Mode`

```csharp
public NavigationMode Mode  // get
```

#### `Route`

```csharp
public object Route  // get
```

#### `TargetRoute`

```csharp
public object TargetRoute  // get
```

### Methods

#### `Cancel`

```csharp
public void Cancel()
```

## `NavigatingToContext`

Context passed to `onNavigatingTo` destination-side guard callbacks before a page becomes active. Call [`NavigatingToContext.Cancel`](#cancel_1) to reject the navigation from the destination side.

```csharp
public sealed class NavigatingToContext
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Properties

#### `IsCancelled`

```csharp
public bool IsCancelled  // get
```

#### `Mode`

```csharp
public NavigationMode Mode  // get
```

#### `PreviousRoute`

```csharp
public object? PreviousRoute  // get
```

#### `Route`

```csharp
public object Route  // get
```

### Methods

#### `Cancel`

```csharp
public void Cancel()
```
