---
title: "RenderContext"
description: "Passed to function components and provides access to hooks. Each component instance gets its own RenderContext which tracks hook call order."
---

```csharp
public sealed class RenderContext
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Components](/microsoft-ui-reactor/main/guides/components), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Constructors [#constructors]

### `RenderContext` [#rendercontext_1]

```csharp
public RenderContext()
```

## Methods [#methods]

### `PickFileAsync` [#pickfileasync]

Opens a single-file picker owned by this component's window.

```csharp
public Task<PickFileResult?> PickFileAsync(FilePickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FilePickerOptions`](/microsoft-ui-reactor/main/reference/hosting/file-pickers#filepickeroptions) |  |

**Remarks**

Not a hook: call from a UI-thread event handler, not during Render. The owner is the window whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns null.

### `PickFolderAsync` [#pickfolderasync]

Opens a single-folder picker owned by this component's window.

```csharp
public Task<PickFolderResult?> PickFolderAsync(FolderPickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FolderPickerOptions`](/microsoft-ui-reactor/main/reference/hosting/file-pickers#folderpickeroptions) |  |

**Remarks**

Not a hook: call from a UI-thread event handler, not during Render. The owner is the window whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns null.

### `PickMultipleFilesAsync` [#pickmultiplefilesasync]

Opens a multiple-file picker owned by this component's window.

```csharp
public Task<IReadOnlyList<PickFileResult>> PickMultipleFilesAsync(FilePickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FilePickerOptions`](/microsoft-ui-reactor/main/reference/hosting/file-pickers#filepickeroptions) |  |

**Remarks**

Not a hook: call from a UI-thread event handler, not during Render. The owner is the window whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns an empty list.

### `PickMultipleFoldersAsync` [#pickmultiplefoldersasync]

Opens a multiple-folder picker owned by this component's window.

```csharp
public Task<IReadOnlyList<PickFolderResult>> PickMultipleFoldersAsync(FolderPickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FolderPickerOptions`](/microsoft-ui-reactor/main/reference/hosting/file-pickers#folderpickeroptions) |  |

**Remarks**

Not a hook: call from a UI-thread event handler, not during Render. The owner is the window whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns an empty list.

### `PickSaveFileAsync` [#picksavefileasync]

Opens a save-file picker owned by this component's window.

```csharp
public Task<PickFileResult?> PickSaveFileAsync(SaveFilePickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`SaveFilePickerOptions`](/microsoft-ui-reactor/main/reference/hosting/file-pickers#savefilepickeroptions) |  |

**Remarks**

Not a hook: call from a UI-thread event handler, not during Render. The owner is the window whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns null. Selecting a target does not create or write the file; that is the caller's responsibility.

## Hooks [#hooks]

- [`UseBreakpoint`](/microsoft-ui-reactor/main/reference/hooks/UseBreakpoint)
- [`UseCallback`](/microsoft-ui-reactor/main/reference/hooks/UseCallback)
- [`UseClosingGuard`](/microsoft-ui-reactor/main/reference/hooks/UseClosingGuard)
- [`UseCollection`](/microsoft-ui-reactor/main/reference/hooks/UseCollection)
- [`UseColorScheme`](/microsoft-ui-reactor/main/reference/hooks/UseColorScheme)
- [`UseCommand`](/microsoft-ui-reactor/main/reference/hooks/UseCommand)
- [`UseContext`](/microsoft-ui-reactor/main/reference/hooks/UseContext)
- [`UseDisplays`](/microsoft-ui-reactor/main/reference/hooks/UseDisplays)
- [`UseDpi`](/microsoft-ui-reactor/main/reference/hooks/UseDpi)
- [`UseEffect`](/microsoft-ui-reactor/main/reference/hooks/UseEffect)
- [`UseExternalStore`](/microsoft-ui-reactor/main/reference/hooks/UseExternalStore)
- [`UseHighContrast`](/microsoft-ui-reactor/main/reference/hooks/UseHighContrast)
- [`UseHighContrastScheme`](/microsoft-ui-reactor/main/reference/hooks/UseHighContrastScheme)
- [`UseIntl`](/microsoft-ui-reactor/main/reference/hooks/UseIntl)
- [`UseIsActive`](/microsoft-ui-reactor/main/reference/hooks/UseIsActive)
- [`UseIsCovered`](/microsoft-ui-reactor/main/reference/hooks/UseIsCovered)
- [`UseIsDarkTheme`](/microsoft-ui-reactor/main/reference/hooks/UseIsDarkTheme)
- [`UseMemo`](/microsoft-ui-reactor/main/reference/hooks/UseMemo)
- [`UseNavigation`](/microsoft-ui-reactor/main/reference/hooks/UseNavigation)
- [`UseNavigationLifecycle`](/microsoft-ui-reactor/main/reference/hooks/UseNavigationLifecycle)
- [`UseObservable`](/microsoft-ui-reactor/main/reference/hooks/UseObservable)
- [`UseObservableProperty`](/microsoft-ui-reactor/main/reference/hooks/UseObservableProperty)
- [`UseObservableTree`](/microsoft-ui-reactor/main/reference/hooks/UseObservableTree)
- [`UseOpenWindow`](/microsoft-ui-reactor/main/reference/hooks/UseOpenWindow)
- [`UsePersisted`](/microsoft-ui-reactor/main/reference/hooks/UsePersisted)
- [`UseReducedMotion`](/microsoft-ui-reactor/main/reference/hooks/UseReducedMotion)
- [`UseReducer`](/microsoft-ui-reactor/main/reference/hooks/UseReducer)
- [`UseRef`](/microsoft-ui-reactor/main/reference/hooks/UseRef)
- [`UseState`](/microsoft-ui-reactor/main/reference/hooks/UseState)
- [`UseSystemBackButton`](/microsoft-ui-reactor/main/reference/hooks/UseSystemBackButton)
- [`UseTrayIcon`](/microsoft-ui-reactor/main/reference/hooks/UseTrayIcon)
- [`UseWindow`](/microsoft-ui-reactor/main/reference/hooks/UseWindow)
- [`UseWindowAspectRatio`](/microsoft-ui-reactor/main/reference/hooks/UseWindowAspectRatio)
- [`UseWindowDragMove`](/microsoft-ui-reactor/main/reference/hooks/UseWindowDragMove)
- [`UseWindowPosition`](/microsoft-ui-reactor/main/reference/hooks/UseWindowPosition)
- [`UseWindowSize`](/microsoft-ui-reactor/main/reference/hooks/UseWindowSize)
- [`UseWindowState`](/microsoft-ui-reactor/main/reference/hooks/UseWindowState)
