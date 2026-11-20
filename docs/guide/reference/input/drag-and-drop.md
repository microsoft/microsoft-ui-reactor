# Drag and drop

Types for dragging data out of and dropping it onto elements.

**Learn more:** [Input and gestures](../../input-and-gestures.md)

## `DragAttached`

Attached element data for the window drag-from-background opt-out modifier.

```csharp
public sealed record DragAttached(bool IsEnabled)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Constructors

#### `DragAttached`

Attached element data for the window drag-from-background opt-out modifier.

```csharp
public DragAttached(bool IsEnabled)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `IsEnabled` | `bool` |  |

### Properties

#### `IsEnabled`

```csharp
public bool IsEnabled  // get, init
```

## `DragData`

The drag payload. Supports typed-payload round-trip (Phase 6a), standard WinUI formats (text / URI / HTML / RTF / files / bitmap — Phase 6b eager + lazy), and custom format identifiers. Same-process transfers stash the in-memory [`DragData`](#dragdata) via a per-drag GUID in `DataPackage.Properties`; eager standard formats are also written directly to the [`DataPackage`](https://learn.microsoft.com/uwp/api/windows.applicationmodel.datatransfer.datapackage) so cross-process drops (Notepad, Word, File Explorer, …) can consume them natively. Lazy providers are adapted via [`DataPackage.SetDataProvider`](https://learn.microsoft.com/uwp/api/windows.applicationmodel.datatransfer.datapackage.setdataprovider) and invoked on demand.

```csharp
public sealed class DragData
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `DragData`

Create an empty [`DragData`](#dragdata). Use `With…` methods to populate formats.

```csharp
public DragData()
```

### Properties

#### `AvailableFormats`

Format identifiers this [`DragData`](#dragdata) advertises.

```csharp
public IReadOnlyCollection<string> AvailableFormats  // get
```

#### `OriginProcessId`

Origin process id — stored so cross-process drops can be detected/rejected.

```csharp
public int OriginProcessId  // get
```

### Methods

#### `GetBitmapAsync`

```csharp
public Task<RandomAccessStreamReference?> GetBitmapAsync(CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` |  |

#### `GetCustomFormatAsync`

```csharp
public Task<T?> GetCustomFormatAsync<T>(string formatId, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `formatId` | `string` |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

#### `GetFilesAsync`

```csharp
public Task<IReadOnlyList<IStorageItem>> GetFilesAsync(CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` |  |

#### `GetHtmlAsync`

```csharp
public Task<string?> GetHtmlAsync(CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` |  |

#### `GetRtfAsync`

```csharp
public Task<string?> GetRtfAsync(CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` |  |

#### `GetTextAsync`

```csharp
public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` |  |

#### `GetUriAsync`

```csharp
public Task<Uri?> GetUriAsync(CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` |  |

#### `HasFormat`

Returns true when the specified format id is present.

```csharp
public bool HasFormat(string formatId)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `formatId` | `string` |  |

#### `Text`

Convenience factory — creates a [`DragData`](#dragdata) pre-populated with plain text.

```csharp
public static DragData Text(string text)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `text` | `string` |  |

#### `TryGetBitmap`

```csharp
public bool TryGetBitmap(out RandomAccessStreamReference value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `RandomAccessStreamReference` |  |

#### `TryGetCustomFormat`

```csharp
public bool TryGetCustomFormat<T>(string formatId, out T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `formatId` | `string` |  |
| `value` | `T` |  |

#### `TryGetFiles`

```csharp
public bool TryGetFiles(out IReadOnlyList<IStorageItem> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `IReadOnlyList<IStorageItem>` |  |

#### `TryGetHtml`

```csharp
public bool TryGetHtml(out string value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `string` |  |

#### `TryGetRtf`

```csharp
public bool TryGetRtf(out string value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `string` |  |

#### `TryGetSafeLocalFiles`

Filters dropped files to local-only paths, removing UNC, DOS-device, reparse, and shell-virtual entries that the source process picked. Apps that open/parse/render dropped files should prefer this over [`DragData.TryGetFiles`](#trygetfiles); UNC paths trigger NTLM authentication on stat/open, reparse points can escape the directory the user thought they shared, and Internet-zone files lose their MOTW warning if the app reads bytes without consulting `Zone.Identifier`. TASK-069.

```csharp
public bool TryGetSafeLocalFiles(out IReadOnlyList<IStorageItem> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `IReadOnlyList<IStorageItem>` |  |

#### `TryGetText`

```csharp
public bool TryGetText(out string value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `string` |  |

#### `TryGetTypedPayload`

Attempts to retrieve a typed payload. Returns false if the format isn't present or the stored payload can't be cast to `T`.

```csharp
public bool TryGetTypedPayload<T>(out T payload)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `payload` | `T` |  |

#### `TryGetUri`

```csharp
public bool TryGetUri(out Uri value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `Uri` |  |

#### `Typed`

Factory for a typed-payload-only [`DragData`](#dragdata).

```csharp
public static DragData Typed<T>(T payload)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `payload` | `T` |  |

#### `TypedFormatId`

Canonical format id used for a typed payload of `T`.

```csharp
public static string TypedFormatId<T>()
```

#### `WithBitmap`

```csharp
public DragData WithBitmap(Func<CancellationToken, Task<RandomAccessStreamReference>> provider)
public DragData WithBitmap(Func<RandomAccessStreamReference> provider)
public DragData WithBitmap(RandomAccessStreamReference bitmap)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `provider` | `Func<CancellationToken, Task<RandomAccessStreamReference>>` or `Func<RandomAccessStreamReference>` |  |
| `bitmap` | `RandomAccessStreamReference` |  |

#### `WithCustomFormat`

```csharp
public DragData WithCustomFormat(string formatId, Func<object> provider)
public DragData WithCustomFormat(string formatId, Func<CancellationToken, Task<object>> provider)
public DragData WithCustomFormat(string formatId, object payload)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `formatId` | `string` |  |
| `provider` | `Func<object>` or `Func<CancellationToken, Task<object>>` |  |
| `payload` | `object` |  |

#### `WithFiles`

```csharp
public DragData WithFiles(IEnumerable<IStorageItem> files)
public DragData WithFiles(Func<IEnumerable<IStorageItem>> provider)
public DragData WithFiles(Func<CancellationToken, Task<IEnumerable<IStorageItem>>> provider)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `files` | `IEnumerable<IStorageItem>` |  |
| `provider` | `Func<IEnumerable<IStorageItem>>` or `Func<CancellationToken, Task<IEnumerable<IStorageItem>>>` |  |

#### `WithHtml`

```csharp
public DragData WithHtml(Func<string> provider)
public DragData WithHtml(Func<CancellationToken, Task<string>> provider)
public DragData WithHtml(string html)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `provider` | `Func<string>` or `Func<CancellationToken, Task<string>>` |  |
| `html` | `string` |  |

#### `WithRtf`

```csharp
public DragData WithRtf(Func<string> provider)
public DragData WithRtf(Func<CancellationToken, Task<string>> provider)
public DragData WithRtf(string rtf)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `provider` | `Func<string>` or `Func<CancellationToken, Task<string>>` |  |
| `rtf` | `string` |  |

#### `WithText`

```csharp
public DragData WithText(Func<string> provider)
public DragData WithText(Func<CancellationToken, Task<string>> provider)
public DragData WithText(string text)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `provider` | `Func<string>` or `Func<CancellationToken, Task<string>>` |  |
| `text` | `string` |  |

#### `WithTypedPayload`

Attaches a typed payload under the canonical typed-format key for `T`.

```csharp
public DragData WithTypedPayload<T>(T payload)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `payload` | `T` |  |

#### `WithUri`

```csharp
public DragData WithUri(Func<CancellationToken, Task<Uri>> provider)
public DragData WithUri(Func<Uri> provider)
public DragData WithUri(Uri uri)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `provider` | `Func<CancellationToken, Task<Uri>>` or `Func<Uri>` |  |
| `uri` | `Uri` |  |

## `DragEndContext`

Delivered to the source's `onEnd` callback when the drag concludes. Distinguishes successful completion from cancellation, and identifies the final operation the target accepted so the source can apply the matching side-effect (e.g. remove on Move).

```csharp
public readonly record struct DragEndContext(DragOperations CompletedOperation, bool WasCancelled)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `DragEndContext`

Delivered to the source's `onEnd` callback when the drag concludes. Distinguishes successful completion from cancellation, and identifies the final operation the target accepted so the source can apply the matching side-effect (e.g. remove on Move).

```csharp
public DragEndContext(DragOperations CompletedOperation, bool WasCancelled)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `CompletedOperation` | [`DragOperations`](#dragoperations) | Final operation negotiated with the target, or [`DragOperations.None`](#values) on cancellation. |
| `WasCancelled` | `bool` | True when the drag was cancelled (ESC key, dropped outside any valid target, system abort). |

### Properties

#### `CompletedOperation`

Final operation negotiated with the target, or [`DragOperations.None`](#values) on cancellation.

```csharp
public DragOperations CompletedOperation  // get, init
```

#### `WasCancelled`

True when the drag was cancelled (ESC key, dropped outside any valid target, system abort).

```csharp
public bool WasCancelled  // get, init
```

## `DragOperationNegotiation`

Negotiation helpers shared by the reconciler and tests.

```csharp
public static class DragOperationNegotiation
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Methods

#### `Negotiate`

Returns the first of [`DragOperations.Move`](#values) / [`DragOperations.Copy`](#values) / [`DragOperations.Link`](#values) that both source and target agree on. The preference order (Move before Copy before Link) matches the Windows shell's default.

```csharp
public static DragOperations Negotiate(DragOperations source, DragOperations target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `source` | [`DragOperations`](#dragoperations) |  |
| `target` | [`DragOperations`](#dragoperations) |  |

## `DragOperations`

Drag-and-drop operation flags. Source declares what it allows via `ElementExtensions.OnDragStart`; target negotiates the final operation by setting [`DragTargetArgs.AcceptedOperation`](#acceptedoperation).

```csharp
public enum DragOperations
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `None` | `0` | No operation — used to reject a drop. |
| `Copy` | `1` | Copy the payload, leaving the source intact. |
| `Move` | `2` | Move the payload — source should remove the item on successful drop. |
| `Link` | `4` | Create a link/reference to the payload. |
| `All` | `7` | Any of Copy, Move, or Link. |

## `DragSourceConfig`

Immutable source-side drag configuration. Stored on `ElementModifiers.DragSource`; the reconciler attaches [`UIElement.DragStarting`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.dragstarting) and [`UIElement.DropCompleted`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.dropcompleted) trampolines and auto-sets [`UIElement.CanDrag`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.candrag).

```csharp
public sealed record DragSourceConfig(Func<DragData> GetData)
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `DragSourceConfig`

Immutable source-side drag configuration. Stored on `ElementModifiers.DragSource`; the reconciler attaches [`UIElement.DragStarting`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.dragstarting) and [`UIElement.DropCompleted`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.dropcompleted) trampolines and auto-sets [`UIElement.CanDrag`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.candrag).

```csharp
public DragSourceConfig(Func<DragData> GetData)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `GetData` | `Func<DragData>` |  |

### Properties

#### `AllowedOperations`

Operations the source will allow. Null means "all".

```csharp
public DragOperations? AllowedOperations  // get, init
```

#### `CanDrag`

Optional guard — when returning false, the drag is cancelled in `DragStarting`.

```csharp
public Func<bool>? CanDrag  // get, init
```

#### `GetData`

```csharp
public Func<DragData> GetData  // get, init
```

#### `OnEnd`

Final callback when the drag ends (success or cancellation).

```csharp
public Action<DragEndContext>? OnEnd  // get, init
```

## `DragTargetArgs`

Drop-target callback argument. Delivered to `.OnDragEnter`/`.OnDragOver`/ `.OnDragLeave`/raw `.OnDrop` handlers.

```csharp
public sealed class DragTargetArgs
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `DragTargetArgs`

```csharp
public DragTargetArgs(DragData data, Point position, DragOperations allowedOperations, DragDropModifiers modifiers, DragUIOverrideHandle uiOverride)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | [`DragData`](#dragdata) |  |
| `position` | `Point` |  |
| `allowedOperations` | [`DragOperations`](#dragoperations) |  |
| `modifiers` | `DragDropModifiers` |  |
| `uiOverride` | [`DragUIOverrideHandle`](#draguioverridehandle) |  |

### Properties

#### `AcceptedOperation`

Operation the target wants to accept. Default [`DragOperations.None`](#values) rejects the drop; set to [`DragOperations.Copy`](#values)/`Move`/`Link` to accept.

```csharp
public DragOperations AcceptedOperation  // get, set
```

#### `AllowedOperations`

Union of operations the source declared it allows.

```csharp
public DragOperations AllowedOperations  // get
```

#### `Data`

The drag payload as advertised by the source.

```csharp
public DragData Data  // get
```

#### `Modifiers`

Active Ctrl/Shift/Alt/mouse-button modifiers at the time of the event.

```csharp
public DragDropModifiers Modifiers  // get
```

#### `Position`

Pointer position in target-element-local space.

```csharp
public Point Position  // get
```

#### `UIOverride`

Mutable UI override — caption/glyph visibility tweaks.

```csharp
public DragUIOverrideHandle UIOverride  // get
```

## `DragUIOverrideHandle`

Mutable hooks on [`DragTargetArgs.UIOverride`](#uioverride) that let drag-over handlers tweak the drop indicator (caption, glyph, preview). Values written here are applied by the reconciler onto the underlying WinUI `Windows.ApplicationModel.DataTransfer.DragDrop.DragUIOverride` after the user callback returns.

```csharp
public sealed class DragUIOverrideHandle
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `DragUIOverrideHandle`

```csharp
public DragUIOverrideHandle()
```

### Properties

#### `Caption`

Override caption text (e.g. "Move to Inbox"). Null leaves WinUI's default.

```csharp
public string? Caption  // get, set
```

#### `IsCaptionVisible`

When false, hides the caption entirely.

```csharp
public bool IsCaptionVisible  // get, set
```

#### `IsContentVisible`

When false, hides the drag-source content preview.

```csharp
public bool IsContentVisible  // get, set
```

#### `IsGlyphVisible`

When false, hides the operation glyph (Copy/Move/Link icon).

```csharp
public bool IsGlyphVisible  // get, set
```

## `DropTargetConfig`

Immutable target-side drag configuration. Stored on `ElementModifiers.DropTarget`; the reconciler attaches [`UIElement.DragEnter`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.dragenter), [`UIElement.DragOver`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.dragover), [`UIElement.DragLeave`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.dragleave) and [`UIElement.Drop`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.drop) trampolines and auto-sets [`UIElement.AllowDrop`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.allowdrop).

```csharp
public sealed record DropTargetConfig
```

Namespace: `Microsoft.UI.Reactor.Input`.

### Constructors

#### `DropTargetConfig`

```csharp
public DropTargetConfig()
```

### Properties

#### `AcceptedOperations`

Default accepted operation applied when [`DragTargetArgs.AcceptedOperation`](#acceptedoperation) is left at [`DragOperations.None`](#values) by the user callback. Matches source-declared [`DragSourceConfig.AllowedOperations`](#allowedoperations) via [`DragOperationNegotiation`](#dragoperationnegotiation).

```csharp
public DragOperations AcceptedOperations  // get, init
```

#### `MaxPayloadBytes`

Hard cap on the byte size of any single payload (Text, Html, Rtf, Bitmap) the target accepts from a cross-process drag. Default 4 MiB. Apps that legitimately accept larger payloads can override per drop target. TASK-068.

```csharp
public int MaxPayloadBytes  // get, init
```

#### `OnDragEnter`

Invoked on [`UIElement.DragEnter`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.dragenter).

```csharp
public Action<DragTargetArgs>? OnDragEnter  // get, init
```

#### `OnDragLeave`

Invoked on [`UIElement.DragLeave`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.dragleave).

```csharp
public Action<DragTargetArgs>? OnDragLeave  // get, init
```

#### `OnDragOver`

Invoked on [`UIElement.DragOver`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.dragover).

```csharp
public Action<DragTargetArgs>? OnDragOver  // get, init
```

#### `OnDrop`

Raw drop handler invoked with full [`DragTargetArgs`](#dragtargetargs). Either this or [`DropTargetConfig.TypedDrop`](#typeddrop) is non-null.

```csharp
public Action<DragTargetArgs>? OnDrop  // get, init
```

#### `TypedDrop`

Typed-drop convenience. The reconciler unwraps the typed payload and calls this when it's present, falling back to [`DropTargetConfig.OnDrop`](#ondrop) otherwise.

```csharp
public Action<DragTargetArgs>? TypedDrop  // get, init
```
