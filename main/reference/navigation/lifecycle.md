---
title: "Navigation lifecycle"
description: "Contexts passed to the navigation lifecycle callbacks."
---

**Learn more:** [Navigation](/microsoft-ui-reactor/main/guides/navigation)

## `NavigatedFromContext` [#navigatedfromcontext]

Context passed to `onNavigatedFrom` lifecycle callbacks after a page is no longer active.

```csharp
public sealed class NavigatedFromContext
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Properties [#properties]

#### `Mode` [#mode]

```csharp
public NavigationMode Mode  // get
```

#### `Route` [#route]

```csharp
public object Route  // get
```

#### `TargetRoute` [#targetroute]

```csharp
public object TargetRoute  // get
```

## `NavigatedToContext` [#navigatedtocontext]

Context passed to `onNavigatedTo` lifecycle callbacks after a page becomes active.

```csharp
public sealed class NavigatedToContext
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Properties [#properties_1]

#### `Mode` [#mode_1]

```csharp
public NavigationMode Mode  // get
```

#### `PreviousRoute` [#previousroute]

```csharp
public object? PreviousRoute  // get
```

#### `Route` [#route_1]

```csharp
public object Route  // get
```

## `NavigatingFromContext` [#navigatingfromcontext]

Context passed to navigation guards before a navigation occurs. Call [`NavigatingFromContext.Cancel`](#cancel) to prevent the navigation.

```csharp
public sealed class NavigatingFromContext
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Properties [#properties_2]

#### `IsCancelled` [#iscancelled]

```csharp
public bool IsCancelled  // get
```

#### `Mode` [#mode_2]

```csharp
public NavigationMode Mode  // get
```

#### `Route` [#route_2]

```csharp
public object Route  // get
```

#### `TargetRoute` [#targetroute_1]

```csharp
public object TargetRoute  // get
```

### Methods [#methods]

#### `Cancel` [#cancel]

```csharp
public void Cancel()
```

## `NavigatingToContext` [#navigatingtocontext]

Context passed to `onNavigatingTo` destination-side guard callbacks before a page becomes active. Call [`NavigatingToContext.Cancel`](#cancel_1) to reject the navigation from the destination side.

```csharp
public sealed class NavigatingToContext
```

Namespace: `Microsoft.UI.Reactor.Navigation`.

### Properties [#properties_3]

#### `IsCancelled` [#iscancelled_1]

```csharp
public bool IsCancelled  // get
```

#### `Mode` [#mode_3]

```csharp
public NavigationMode Mode  // get
```

#### `PreviousRoute` [#previousroute_1]

```csharp
public object? PreviousRoute  // get
```

#### `Route` [#route_3]

```csharp
public object Route  // get
```

### Methods [#methods_1]

#### `Cancel` [#cancel_1]

```csharp
public void Cancel()
```
