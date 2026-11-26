# RenderContext

Passed to function components and provides access to hooks. Each component instance gets its own RenderContext which tracks hook call order.

```csharp
public sealed class RenderContext
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Components](../../components.md), [Hooks](../../hooks.md)

## Constructors

### `RenderContext`

```csharp
public RenderContext()
```

## Methods

### `PickFileAsync`

Opens a single-file picker owned by this component's window.

```csharp
public Task<PickFileResult?> PickFileAsync(FilePickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FilePickerOptions`](../hosting/file-pickers.md#filepickeroptions) |  |

**Remarks**

Not a hook: call from a UI-thread event handler, not during Render. The owner is the window whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns null.

### `PickFolderAsync`

Opens a single-folder picker owned by this component's window.

```csharp
public Task<PickFolderResult?> PickFolderAsync(FolderPickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FolderPickerOptions`](../hosting/file-pickers.md#folderpickeroptions) |  |

**Remarks**

Not a hook: call from a UI-thread event handler, not during Render. The owner is the window whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns null.

### `PickMultipleFilesAsync`

Opens a multiple-file picker owned by this component's window.

```csharp
public Task<IReadOnlyList<PickFileResult>> PickMultipleFilesAsync(FilePickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FilePickerOptions`](../hosting/file-pickers.md#filepickeroptions) |  |

**Remarks**

Not a hook: call from a UI-thread event handler, not during Render. The owner is the window whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns an empty list.

### `PickMultipleFoldersAsync`

Opens a multiple-folder picker owned by this component's window.

```csharp
public Task<IReadOnlyList<PickFolderResult>> PickMultipleFoldersAsync(FolderPickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`FolderPickerOptions`](../hosting/file-pickers.md#folderpickeroptions) |  |

**Remarks**

Not a hook: call from a UI-thread event handler, not during Render. The owner is the window whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns an empty list.

### `PickSaveFileAsync`

Opens a save-file picker owned by this component's window.

```csharp
public Task<PickFileResult?> PickSaveFileAsync(SaveFilePickerOptions options)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `options` | [`SaveFilePickerOptions`](../hosting/file-pickers.md#savefilepickeroptions) |  |

**Remarks**

Not a hook: call from a UI-thread event handler, not during Render. The owner is the window whose host renders this component (for ReactorHostControl, the window it is loaded in). Throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) once the component unmounts, its window closes or its host is disposed, or while its ReactorHostControl is not loaded. Cancellation returns null. Selecting a target does not create or write the file; that is the caller's responsibility.

## Hooks

- [`UseBreakpoint`](../hooks/UseBreakpoint.md)
- [`UseCallback`](../hooks/UseCallback.md)
- [`UseClosingGuard`](../hooks/UseClosingGuard.md)
- [`UseCollection`](../hooks/UseCollection.md)
- [`UseColorScheme`](../hooks/UseColorScheme.md)
- [`UseCommand`](../hooks/UseCommand.md)
- [`UseContext`](../hooks/UseContext.md)
- [`UseDisplays`](../hooks/UseDisplays.md)
- [`UseDpi`](../hooks/UseDpi.md)
- [`UseEffect`](../hooks/UseEffect.md)
- [`UseExternalStore`](../hooks/UseExternalStore.md)
- [`UseHighContrast`](../hooks/UseHighContrast.md)
- [`UseHighContrastScheme`](../hooks/UseHighContrastScheme.md)
- [`UseIntl`](../hooks/UseIntl.md)
- [`UseIsActive`](../hooks/UseIsActive.md)
- [`UseIsCovered`](../hooks/UseIsCovered.md)
- [`UseIsDarkTheme`](../hooks/UseIsDarkTheme.md)
- [`UseMemo`](../hooks/UseMemo.md)
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
- [`UseState`](../hooks/UseState.md)
- [`UseSystemBackButton`](../hooks/UseSystemBackButton.md)
- [`UseTrayIcon`](../hooks/UseTrayIcon.md)
- [`UseWindow`](../hooks/UseWindow.md)
- [`UseWindowAspectRatio`](../hooks/UseWindowAspectRatio.md)
- [`UseWindowDragMove`](../hooks/UseWindowDragMove.md)
- [`UseWindowPosition`](../hooks/UseWindowPosition.md)
- [`UseWindowSize`](../hooks/UseWindowSize.md)
- [`UseWindowState`](../hooks/UseWindowState.md)
