---
title: "Component"
description: "Base class for stateful components (like React class components, but using hooks). Components hold a RenderContext that tracks their hook state across re-renders."
---

```csharp
public abstract class Component
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Components](/microsoft-ui-reactor/main/guides/components)

## Constructors [#constructors]

### `Component` [#component_1]

```csharp
protected Component()
```

## Methods [#methods]

### `PickFileAsync` [#pickfileasync]

Opens a single-file picker owned by this component's window. Not a hook: call from a UI-thread event handler.

```csharp
protected Task<PickFileResult?> PickFileAsync(FilePickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FilePickerOptions`](/microsoft-ui-reactor/main/reference/hosting/file-pickers#filepickeroptions) |  |

### `PickFolderAsync` [#pickfolderasync]

Opens a single-folder picker owned by this component's window. Not a hook: call from a UI-thread event handler.

```csharp
protected Task<PickFolderResult?> PickFolderAsync(FolderPickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FolderPickerOptions`](/microsoft-ui-reactor/main/reference/hosting/file-pickers#folderpickeroptions) |  |

### `PickMultipleFilesAsync` [#pickmultiplefilesasync]

Opens a multiple-file picker owned by this component's window. Not a hook: call from a UI-thread event handler.

```csharp
protected Task<IReadOnlyList<PickFileResult>> PickMultipleFilesAsync(FilePickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FilePickerOptions`](/microsoft-ui-reactor/main/reference/hosting/file-pickers#filepickeroptions) |  |

### `PickMultipleFoldersAsync` [#pickmultiplefoldersasync]

Opens a multiple-folder picker owned by this component's window. Not a hook: call from a UI-thread event handler.

```csharp
protected Task<IReadOnlyList<PickFolderResult>> PickMultipleFoldersAsync(FolderPickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FolderPickerOptions`](/microsoft-ui-reactor/main/reference/hosting/file-pickers#folderpickeroptions) |  |

### `PickSaveFileAsync` [#picksavefileasync]

Opens a save-file picker owned by this component's window. Not a hook: call from a UI-thread event handler.

```csharp
protected Task<PickFileResult?> PickSaveFileAsync(SaveFilePickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`SaveFilePickerOptions`](/microsoft-ui-reactor/main/reference/hosting/file-pickers#savefilepickeroptions) |  |

### `Render` [#render]

Override to describe the UI. Use UseState, UseEffect, etc. from the context. Must call hooks in the same order every render.

```csharp
public abstract Element Render()
```

### `ShouldUpdate` [#shouldupdate]

Controls whether this propless component should re-render when its parent re-renders. Default: false — propless components only re-render from their own state changes or context changes. Override and return true to always re-render when the parent re-renders.

```csharp
protected virtual bool ShouldUpdate()
```

## Hooks [#hooks]

- [`UseAnnounce`](/microsoft-ui-reactor/main/reference/hooks/UseAnnounce)
- [`UseBreakpoint`](/microsoft-ui-reactor/main/reference/hooks/UseBreakpoint)
- [`UseCallback`](/microsoft-ui-reactor/main/reference/hooks/UseCallback)
- [`UseClosingGuard`](/microsoft-ui-reactor/main/reference/hooks/UseClosingGuard)
- [`UseCollection`](/microsoft-ui-reactor/main/reference/hooks/UseCollection)
- [`UseColorScheme`](/microsoft-ui-reactor/main/reference/hooks/UseColorScheme)
- [`UseCommand`](/microsoft-ui-reactor/main/reference/hooks/UseCommand)
- [`UseContext`](/microsoft-ui-reactor/main/reference/hooks/UseContext)
- [`UseDataSource`](/microsoft-ui-reactor/main/reference/hooks/UseDataSource)
- [`UseDisplays`](/microsoft-ui-reactor/main/reference/hooks/UseDisplays)
- [`UseDpi`](/microsoft-ui-reactor/main/reference/hooks/UseDpi)
- [`UseEffect`](/microsoft-ui-reactor/main/reference/hooks/UseEffect)
- [`UseExternalStore`](/microsoft-ui-reactor/main/reference/hooks/UseExternalStore)
- [`UseHighContrast`](/microsoft-ui-reactor/main/reference/hooks/UseHighContrast)
- [`UseHighContrastScheme`](/microsoft-ui-reactor/main/reference/hooks/UseHighContrastScheme)
- [`UseInfiniteResource`](/microsoft-ui-reactor/main/reference/hooks/UseInfiniteResource)
- [`UseIntl`](/microsoft-ui-reactor/main/reference/hooks/UseIntl)
- [`UseIsActive`](/microsoft-ui-reactor/main/reference/hooks/UseIsActive)
- [`UseIsCovered`](/microsoft-ui-reactor/main/reference/hooks/UseIsCovered)
- [`UseIsDarkTheme`](/microsoft-ui-reactor/main/reference/hooks/UseIsDarkTheme)
- [`UseMemo`](/microsoft-ui-reactor/main/reference/hooks/UseMemo)
- [`UseMutation`](/microsoft-ui-reactor/main/reference/hooks/UseMutation)
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
- [`UseResource`](/microsoft-ui-reactor/main/reference/hooks/UseResource)
- [`UseState`](/microsoft-ui-reactor/main/reference/hooks/UseState)
- [`UseSystemBackButton`](/microsoft-ui-reactor/main/reference/hooks/UseSystemBackButton)
- [`UseTrayIcon`](/microsoft-ui-reactor/main/reference/hooks/UseTrayIcon)
- [`UseWindow`](/microsoft-ui-reactor/main/reference/hooks/UseWindow)
- [`UseWindowAspectRatio`](/microsoft-ui-reactor/main/reference/hooks/UseWindowAspectRatio)
- [`UseWindowDragMove`](/microsoft-ui-reactor/main/reference/hooks/UseWindowDragMove)
- [`UseWindowPosition`](/microsoft-ui-reactor/main/reference/hooks/UseWindowPosition)
- [`UseWindowSize`](/microsoft-ui-reactor/main/reference/hooks/UseWindowSize)
- [`UseWindowState`](/microsoft-ui-reactor/main/reference/hooks/UseWindowState)
