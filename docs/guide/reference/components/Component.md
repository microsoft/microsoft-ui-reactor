# Component

Base class for stateful components (like React class components, but using hooks). Components hold a RenderContext that tracks their hook state across re-renders.

```csharp
public abstract class Component
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Components](../../components.md)

## Constructors

### `Component`

```csharp
protected Component()
```

## Methods

### `PickFileAsync`

Opens a single-file picker owned by this component's window. Not a hook: call from a UI-thread event handler.

```csharp
protected Task<PickFileResult?> PickFileAsync(FilePickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FilePickerOptions`](../hosting/file-pickers.md#filepickeroptions) |  |

### `PickFolderAsync`

Opens a single-folder picker owned by this component's window. Not a hook: call from a UI-thread event handler.

```csharp
protected Task<PickFolderResult?> PickFolderAsync(FolderPickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FolderPickerOptions`](../hosting/file-pickers.md#folderpickeroptions) |  |

### `PickMultipleFilesAsync`

Opens a multiple-file picker owned by this component's window. Not a hook: call from a UI-thread event handler.

```csharp
protected Task<IReadOnlyList<PickFileResult>> PickMultipleFilesAsync(FilePickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FilePickerOptions`](../hosting/file-pickers.md#filepickeroptions) |  |

### `PickMultipleFoldersAsync`

Opens a multiple-folder picker owned by this component's window. Not a hook: call from a UI-thread event handler.

```csharp
protected Task<IReadOnlyList<PickFolderResult>> PickMultipleFoldersAsync(FolderPickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FolderPickerOptions`](../hosting/file-pickers.md#folderpickeroptions) |  |

### `PickSaveFileAsync`

Opens a save-file picker owned by this component's window. Not a hook: call from a UI-thread event handler.

```csharp
protected Task<PickFileResult?> PickSaveFileAsync(SaveFilePickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`SaveFilePickerOptions`](../hosting/file-pickers.md#savefilepickeroptions) |  |

### `Render`

Override to describe the UI. Use UseState, UseEffect, etc. from the context. Must call hooks in the same order every render.

```csharp
public abstract Element Render()
```

### `ShouldUpdate`

Controls whether this propless component should re-render when its parent re-renders. Default: false — propless components only re-render from their own state changes or context changes. Override and return true to always re-render when the parent re-renders.

```csharp
protected virtual bool ShouldUpdate()
```

## Hooks

- [`UseAnnounce`](../hooks/UseAnnounce.md)
- [`UseBreakpoint`](../hooks/UseBreakpoint.md)
- [`UseCallback`](../hooks/UseCallback.md)
- [`UseClosingGuard`](../hooks/UseClosingGuard.md)
- [`UseCollection`](../hooks/UseCollection.md)
- [`UseColorScheme`](../hooks/UseColorScheme.md)
- [`UseCommand`](../hooks/UseCommand.md)
- [`UseContext`](../hooks/UseContext.md)
- [`UseDataSource`](../hooks/UseDataSource.md)
- [`UseDisplays`](../hooks/UseDisplays.md)
- [`UseDpi`](../hooks/UseDpi.md)
- [`UseEffect`](../hooks/UseEffect.md)
- [`UseExternalStore`](../hooks/UseExternalStore.md)
- [`UseHighContrast`](../hooks/UseHighContrast.md)
- [`UseHighContrastScheme`](../hooks/UseHighContrastScheme.md)
- [`UseInfiniteResource`](../hooks/UseInfiniteResource.md)
- [`UseIntl`](../hooks/UseIntl.md)
- [`UseIsActive`](../hooks/UseIsActive.md)
- [`UseIsCovered`](../hooks/UseIsCovered.md)
- [`UseIsDarkTheme`](../hooks/UseIsDarkTheme.md)
- [`UseMemo`](../hooks/UseMemo.md)
- [`UseMutation`](../hooks/UseMutation.md)
- [`UseNavigation`](../hooks/UseNavigation.md)
- [`UseNavigationLifecycle`](../hooks/UseNavigationLifecycle.md)
- [`UseObservable`](../hooks/UseObservable.md)
- [`UseObservableProperty`](../hooks/UseObservableProperty.md)
- [`UseObservableTree`](../hooks/UseObservableTree.md)
- [`UseOpenWindow`](../hooks/UseOpenWindow.md)
- [`UsePersisted`](../hooks/UsePersisted.md)
- [`UseReducedMotion`](../hooks/UseReducedMotion.md)
- [`UseReducer`](../hooks/UseReducer.md)
- [`UseRef`](../hooks/UseRef.md)
- [`UseResource`](../hooks/UseResource.md)
- [`UseState`](../hooks/UseState.md)
- [`UseSystemBackButton`](../hooks/UseSystemBackButton.md)
- [`UseTrayIcon`](../hooks/UseTrayIcon.md)
- [`UseWindow`](../hooks/UseWindow.md)
- [`UseWindowAspectRatio`](../hooks/UseWindowAspectRatio.md)
- [`UseWindowDragMove`](../hooks/UseWindowDragMove.md)
- [`UseWindowPosition`](../hooks/UseWindowPosition.md)
- [`UseWindowSize`](../hooks/UseWindowSize.md)
- [`UseWindowState`](../hooks/UseWindowState.md)
