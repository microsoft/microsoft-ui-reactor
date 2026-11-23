# File pickers

Options for the open file, save file and folder pickers.

## `FilePickerOptions`

Options for [`RenderContext.PickFileAsync`](../components/RenderContext.md#pickfileasync) and [`RenderContext.PickMultipleFilesAsync`](../components/RenderContext.md#pickmultiplefilesasync).

```csharp
public sealed record FilePickerOptions(IReadOnlyList<string>? FileTypeFilter = null, PickerLocationId SuggestedStartLocation = default, string CommitButtonText = "")
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `FilePickerOptions`

Options for [`RenderContext.PickFileAsync`](../components/RenderContext.md#pickfileasync) and [`RenderContext.PickMultipleFilesAsync`](../components/RenderContext.md#pickmultiplefilesasync).

```csharp
public FilePickerOptions(IReadOnlyList<string>? FileTypeFilter = null, PickerLocationId SuggestedStartLocation = default, string CommitButtonText = "")
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `FileTypeFilter` | `IReadOnlyList<string>?` | `null` | Extensions shown, such as ".txt"; null or empty shows all files. Ignored when [`FilePickerOptions.FileTypeChoices`](#filetypechoices) is non-empty. |
| `SuggestedStartLocation` | `PickerLocationId` | `default` | The initial location suggested to Windows. |
| `CommitButtonText` | `string` | `""` | The commit button label; empty uses the platform default. |

### Properties

#### `CommitButtonText`

The commit button label; empty uses the platform default.

```csharp
public string CommitButtonText  // get, init
```

#### `FileTypeChoices`

Ordered, labelled file-type groups. When non-empty they replace [`FilePickerOptions.FileTypeFilter`](#filetypefilter).

```csharp
public IReadOnlyList<FileTypeChoice>? FileTypeChoices  // get, init
```

#### `FileTypeFilter`

Extensions shown, such as ".txt"; null or empty shows all files. Ignored when [`FilePickerOptions.FileTypeChoices`](#filetypechoices) is non-empty.

```csharp
public IReadOnlyList<string>? FileTypeFilter  // get, init
```

#### `InitialFileTypeIndex`

The zero-based file-type entry selected initially; null preserves the platform default.

```csharp
public int? InitialFileTypeIndex  // get, init
```

#### `SettingsIdentifier`

Identifies the picker state remembered by Windows.

```csharp
public string SettingsIdentifier  // get, init
```

#### `SuggestedFolder`

An absolute folder path that the dialog always opens in; empty lets Windows decide.

```csharp
public string SuggestedFolder  // get, init
```

#### `SuggestedStartFolder`

An absolute folder path used only until Windows remembers a location for this picker; empty lets Windows decide.

```csharp
public string SuggestedStartFolder  // get, init
```

#### `SuggestedStartLocation`

The initial location suggested to Windows.

```csharp
public PickerLocationId SuggestedStartLocation  // get, init
```

#### `Title`

The dialog title; empty uses the platform default.

```csharp
public string Title  // get, init
```

#### `ViewMode`

The native view mode; null preserves the platform default.

```csharp
public PickerViewMode? ViewMode  // get, init
```

## `FileTypeChoice`

A labelled group of file extensions offered by a file picker, such as `("Text", [".txt", ".md"])`.

```csharp
public sealed record FileTypeChoice(string Label, IReadOnlyList<string> Extensions)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `FileTypeChoice`

A labelled group of file extensions offered by a file picker, such as `("Text", [".txt", ".md"])`.

```csharp
public FileTypeChoice(string Label, IReadOnlyList<string> Extensions)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Label` | `string` | The display label shown in the picker's file-type list. |
| `Extensions` | `IReadOnlyList<string>` | The extensions in the group, each including the leading dot. |

### Properties

#### `Extensions`

The extensions in the group, each including the leading dot.

```csharp
public IReadOnlyList<string> Extensions  // get, init
```

#### `Label`

The display label shown in the picker's file-type list.

```csharp
public string Label  // get, init
```

## `FolderPickerOptions`

Options for [`RenderContext.PickFolderAsync`](../components/RenderContext.md#pickfolderasync) and [`RenderContext.PickMultipleFoldersAsync`](../components/RenderContext.md#pickmultiplefoldersasync).

```csharp
public sealed record FolderPickerOptions(PickerLocationId SuggestedStartLocation = default, string CommitButtonText = "")
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `FolderPickerOptions`

Options for [`RenderContext.PickFolderAsync`](../components/RenderContext.md#pickfolderasync) and [`RenderContext.PickMultipleFoldersAsync`](../components/RenderContext.md#pickmultiplefoldersasync).

```csharp
public FolderPickerOptions(PickerLocationId SuggestedStartLocation = default, string CommitButtonText = "")
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `SuggestedStartLocation` | `PickerLocationId` | `default` | The initial location suggested to Windows. |
| `CommitButtonText` | `string` | `""` | The commit button label; empty uses the platform default. |

### Properties

#### `CommitButtonText`

The commit button label; empty uses the platform default.

```csharp
public string CommitButtonText  // get, init
```

#### `SettingsIdentifier`

Identifies the picker state remembered by Windows.

```csharp
public string SettingsIdentifier  // get, init
```

#### `SuggestedFolder`

An absolute folder path that the dialog always opens in; empty lets Windows decide.

```csharp
public string SuggestedFolder  // get, init
```

#### `SuggestedStartFolder`

An absolute folder path used only until Windows remembers a location for this picker; empty lets Windows decide.

```csharp
public string SuggestedStartFolder  // get, init
```

#### `SuggestedStartLocation`

The initial location suggested to Windows.

```csharp
public PickerLocationId SuggestedStartLocation  // get, init
```

#### `Title`

The dialog title; empty uses the platform default.

```csharp
public string Title  // get, init
```

#### `ViewMode`

The native view mode; null preserves the platform default.

```csharp
public PickerViewMode? ViewMode  // get, init
```

## `SaveFilePickerOptions`

Options for [`RenderContext.PickSaveFileAsync`](../components/RenderContext.md#picksavefileasync).

```csharp
public sealed record SaveFilePickerOptions(IReadOnlyList<FileTypeChoice> FileTypeChoices, string SuggestedFileName = "", PickerLocationId SuggestedStartLocation = default, string CommitButtonText = "")
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `SaveFilePickerOptions`

Options for [`RenderContext.PickSaveFileAsync`](../components/RenderContext.md#picksavefileasync).

```csharp
public SaveFilePickerOptions(IReadOnlyList<FileTypeChoice> FileTypeChoices, string SuggestedFileName = "", PickerLocationId SuggestedStartLocation = default, string CommitButtonText = "")
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `FileTypeChoices` | `IReadOnlyList<FileTypeChoice>` |  | Ordered, labelled file-type groups. At least one choice is required. |
| `SuggestedFileName` | `string` | `""` | The name suggested to the user, without requiring an extension. |
| `SuggestedStartLocation` | `PickerLocationId` | `default` | The initial location suggested to Windows. |
| `CommitButtonText` | `string` | `""` | The commit button label; empty uses the platform default. |

### Properties

#### `CommitButtonText`

The commit button label; empty uses the platform default.

```csharp
public string CommitButtonText  // get, init
```

#### `DefaultFileExtension`

The extension, such as ".txt", appended when the user types a name without one; empty uses the platform default.

```csharp
public string DefaultFileExtension  // get, init
```

#### `FileTypeChoices`

Ordered, labelled file-type groups. At least one choice is required.

```csharp
public IReadOnlyList<FileTypeChoice> FileTypeChoices  // get, init
```

#### `InitialFileTypeIndex`

The zero-based file-type choice selected initially; null preserves the platform default.

```csharp
public int? InitialFileTypeIndex  // get, init
```

#### `SettingsIdentifier`

Identifies the picker state remembered by Windows.

```csharp
public string SettingsIdentifier  // get, init
```

#### `ShowOverwritePrompt`

Whether Windows asks before overwriting an existing file. Defaults to true.

```csharp
public bool ShowOverwritePrompt  // get, init
```

#### `SuggestedFileName`

The name suggested to the user, without requiring an extension.

```csharp
public string SuggestedFileName  // get, init
```

#### `SuggestedFolder`

An absolute folder path that the dialog always opens in; empty lets Windows decide.

```csharp
public string SuggestedFolder  // get, init
```

#### `SuggestedStartFolder`

An absolute folder path used only until Windows remembers a location for this picker; empty lets Windows decide.

```csharp
public string SuggestedStartFolder  // get, init
```

#### `SuggestedStartLocation`

The initial location suggested to Windows.

```csharp
public PickerLocationId SuggestedStartLocation  // get, init
```

#### `Title`

The dialog title; empty uses the platform default.

```csharp
public string Title  // get, init
```
