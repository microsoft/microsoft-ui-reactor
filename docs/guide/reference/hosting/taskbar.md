# Taskbar and jump lists

Types for the taskbar button, its progress, overlay, thumbnail buttons and jump list.

## `JumpList`

Process-scoped jump list — the right-click flyout that appears on the app's taskbar button or Start tile. Driven through `Windows.UI.StartScreen.JumpList` for packaged apps and Win32 `ICustomDestinationList` for unpackaged. (spec 036 §11.3)

```csharp
public static class JumpList
```

Namespace: `Microsoft.UI.Reactor`.

### Remarks

**Security note:** [`JumpListItem.Arguments`](#arguments) are command-line strings handed to a freshly launched process by the shell. Apps must parse them through `Reactor.Cli`'s arg parser before acting on them — a malformed shortcut, a forged jump-list entry left by another process, or a stale entry from an earlier installation will otherwise reach the new process unchecked. Reactor itself never auto-acts on these strings; [`ReactorAppContext.LaunchActivation`](ReactorAppContext.md#launchactivation) surfaces them so the app can validate before dispatching.

Unpackaged apps must set [`JumpList.AppUserModelId`](#appusermodelid) before the first [`JumpList.UpdateAsync`](#updateasync) call — without it the shell has no stable identity to attach the jump list to.

### Properties

#### `AppUserModelId`

Application User Model ID used by the shell to associate the jump list with this process. Required for unpackaged apps; ignored when the process runs under an MSIX package (the package manifest supplies it).

```csharp
public static string? AppUserModelId  // get, set
```

#### `ShowFrequent`

Whether the OS-managed "Frequent" category is shown. The list contents are owned by the shell — apps can only toggle visibility.

```csharp
public static bool ShowFrequent  // get, set
```

#### `ShowRecent`

Whether the OS-managed "Recent" category is shown. The list contents are owned by the shell — apps can only toggle visibility.

```csharp
public static bool ShowRecent  // get, set
```

### Methods

#### `ClearAsync`

Clear all user-defined jump-list entries. UI-thread only.

```csharp
public static Task<IReadOnlyList<string>> ClearAsync()
```

**Returns:** The removed arguments observed by the clear transaction.

**Remarks**

Like [`JumpList.UpdateAsync`](#updateasync), a successful clear consumes Windows' removal records. Retain the returned arguments before publishing destinations again.

#### `GetRemovedArgumentsAsync`

Read the argument strings of entries removed by the user. UI-thread only.

```csharp
public static Task<IReadOnlyList<string>> GetRemovedArgumentsAsync()
```

**Remarks**

This does not update the list or acknowledge removals. Call before [`JumpList.UpdateAsync`](#updateasync) and remove matching entries from the app's destination tracking data. A successful update clears its removal records; also retain the arguments returned by that update to capture removals that occurred after this query. Only reintroduce a destination after new user activity. On unpackaged apps, only shell links targeting the current executable are returned; shell-item destinations are not created by this wrapper. Argument strings are compared ordinally, not as parsed command lines.

#### `UpdateAsync`

Replace the jump list's user-defined entries. UI-thread only.

```csharp
public static Task<IReadOnlyList<string>> UpdateAsync(IEnumerable<JumpListItem> items)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `items` | `IEnumerable<JumpListItem>` |  |

**Returns:** All distinct removed arguments observed in this update transaction, including removals not present in `items`. The result reports observations, not commit success; logged shell HRESULT failures retain the existing non-throwing behavior.

**Remarks**

Clears any previously-set entries first; the OS-managed Recent and Frequent categories are unaffected (toggle them via [`JumpList.ShowRecent`](#showrecent) / [`JumpList.ShowFrequent`](#showfrequent)). COM activation and marshaling failures propagate as exceptions — callers may catch if fire-and-forget is desired. Individual HRESULT failures from shell APIs (e.g., `BeginList`, `CommitList`) are logged via `DiagnosticLog` but do not throw. Entries currently recorded as removed by Windows are omitted. A successful update clears the platform's removal records. Retain the returned arguments in the app's destination tracking data to avoid reintroducing them on the next update without new user activity.

## `JumpListItem`

Shape of a single jump-list entry. (spec 036 §11.3 / §11.6)

```csharp
public sealed record JumpListItem(string Title, string Arguments, JumpListItemKind Kind = JumpListItemKind.Task, string? Description = null, WindowIcon? Icon = null, string? GroupCategory = null)
```

Namespace: `Microsoft.UI.Reactor`.

### Constructors

#### `JumpListItem`

Shape of a single jump-list entry. (spec 036 §11.3 / §11.6)

```csharp
public JumpListItem(string Title, string Arguments, JumpListItemKind Kind = JumpListItemKind.Task, string? Description = null, WindowIcon? Icon = null, string? GroupCategory = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Title` | `string` |  | User-visible label for the entry. |
| `Arguments` | `string` |  | Command-line arguments handed to a fresh process invocation when the user activates the entry. **Recommended convention:** use a deep-link URI string (the same shape [`DeepLinkMap.Resolve`](../navigation/DeepLinkMap.md#resolve) accepts) and resolve via [`LaunchActivation.TryResolve`](launch.md#tryresolve) in the startup callback. The [`JumpListItem.ForUri`](#foruri) factory makes that convention discoverable. **Security:** these strings round-trip through the shell back into the next process launch. Apps must validate them via `Reactor.Cli`'s arg parser or [`DeepLinkMap<TRoute>`](../navigation/DeepLinkMap.md) before acting on them — Reactor itself never auto-executes the arguments. |
| `Kind` | [`JumpListItemKind`](#jumplistitemkind) | `JumpListItemKind.Task` | Whether this is a regular task, a separator, or part of a custom group. |
| `Description` | `string?` | `null` | Optional tooltip / accessible description. |
| `Icon` | [`WindowIcon?`](WindowIcon.md) | `null` | Optional icon shown next to the title. The two jump-list paths want different things, and each silently skips what it cannot use: **Packaged** — `WindowIcon.FromResource` only. The WinRT `JumpListItem.Logo` takes a packaged `Uri`, so a filesystem path is skipped. **Unpackaged** — `WindowIcon.FromPath` only. The Win32 path hands the source to `IShellLink.SetIconLocation`, which wants a filesystem path, so an `ms-appx:` URI is skipped instead. See `JumpListComInterop.BuildShellLinkArray`. Binary sources (`WindowIcon.FromBytes` / `FromRgba`) work on neither: they have no URI and no path. Both arms skip them via the empty-`Source` guard. Ship a sidecar `.ico` or a packaged asset for jump-list entries. |
| `GroupCategory` | `string?` | `null` | Group label for [`JumpListItemKind.Custom`](#values) items. |

### Properties

#### `Arguments`

Command-line arguments handed to a fresh process invocation when the user activates the entry. **Recommended convention:** use a deep-link URI string (the same shape [`DeepLinkMap.Resolve`](../navigation/DeepLinkMap.md#resolve) accepts) and resolve via [`LaunchActivation.TryResolve`](launch.md#tryresolve) in the startup callback. The [`JumpListItem.ForUri`](#foruri) factory makes that convention discoverable.

**Security:** these strings round-trip through the shell back into the next process launch. Apps must validate them via `Reactor.Cli`'s arg parser or [`DeepLinkMap<TRoute>`](../navigation/DeepLinkMap.md) before acting on them — Reactor itself never auto-executes the arguments.

```csharp
public string Arguments  // get, init
```

#### `Description`

Optional tooltip / accessible description.

```csharp
public string? Description  // get, init
```

#### `GroupCategory`

Group label for [`JumpListItemKind.Custom`](#values) items.

```csharp
public string? GroupCategory  // get, init
```

#### `Icon`

Optional icon shown next to the title. The two jump-list paths want different things, and each silently skips what it cannot use:

- **Packaged** — `WindowIcon.FromResource` only. The WinRT `JumpListItem.Logo` takes a packaged `Uri`, so a filesystem path is skipped.
- **Unpackaged** — `WindowIcon.FromPath` only. The Win32 path hands the source to `IShellLink.SetIconLocation`, which wants a filesystem path, so an `ms-appx:` URI is skipped instead. See `JumpListComInterop.BuildShellLinkArray`.

Binary sources (`WindowIcon.FromBytes` / `FromRgba`) work on neither: they have no URI and no path. Both arms skip them via the empty-`Source` guard. Ship a sidecar `.ico` or a packaged asset for jump-list entries.

```csharp
public WindowIcon? Icon  // get, init
```

#### `Kind`

Whether this is a regular task, a separator, or part of a custom group.

```csharp
public JumpListItemKind Kind  // get, init
```

#### `Title`

User-visible label for the entry.

```csharp
public string Title  // get, init
```

### Methods

#### `EncodeArgument`

Encode a single argv value per `CommandLineToArgvW` rules. Public so callers building command lines piecewise can reuse the same escaper Reactor uses internally for jump-list entries.

```csharp
public static string EncodeArgument(string argument)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `argument` | `string` |  |

#### `EncodeArguments`

Encode a sequence of argv-style argument values as a single command-line string compatible with `CommandLineToArgvW`. Whitespace, quotes, and trailing-backslash sequences are quoted/escaped per the documented MSVC parsing rules so that round-tripping the result through the OS command-line parser yields back the original sequence.

```csharp
public static string EncodeArguments(IEnumerable<string> arguments)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `arguments` | `IEnumerable<string>` |  |

**Remarks**

Useful directly for jump-list entries (see [`JumpListItem.ForCommandLine`](#forcommandline)), thumbnail-toolbar buttons, and any other surface where an argument string is handed to the shell for re-launch. Reactor's own CLI parser (`Reactor.Cli`) consumes `CommandLineToArgvW`-encoded input, so encoded strings round-trip without further escaping.

#### `ForCommandLine`

Build a jump-list entry whose [`JumpListItem.Arguments`](#arguments) string is tokenized: each element of `arguments` becomes one argv slot when the launched process parses its command line via `CommandLineToArgvW`. Values containing whitespace, quotes, or backslashes are escaped so a malicious value can't break out into a neighbouring argument.

Use this factory whenever the arguments include data that did not originate from a string literal in your source code (deep-link payloads, file paths, user-typed identifiers). The raw-string [`JumpListItem`](#jumplistitem_1) constructor and [`JumpListItem.ForUri`](#foruri) are still appropriate when the caller has already produced a well-formed deep-link URI. (spec 036 §11.3 security checklist)

```csharp
public static JumpListItem ForCommandLine(string title, IEnumerable<string> arguments, string? description = null, WindowIcon? icon = null, string? groupCategory = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `title` | `string` |  |  |
| `arguments` | `IEnumerable<string>` |  |  |
| `description` | `string?` | `null` |  |
| `icon` | [`WindowIcon?`](WindowIcon.md) | `null` |  |
| `groupCategory` | `string?` | `null` |  |

#### `ForUri`

Convenience factory for the recommended "Arguments == deep-link URI" convention. Equivalent to `new JumpListItem(title, uri, Task, ...)`; makes the navigation bridge discoverable so apps don't need to read the Arguments doc to know they can hand the activation to a [`DeepLinkMap<TRoute>`](../navigation/DeepLinkMap.md). (spec 036 §11.3 / §11.6)

```csharp
public static JumpListItem ForUri(string title, string uri, string? description = null, WindowIcon? icon = null, string? groupCategory = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `title` | `string` |  |  |
| `uri` | `string` |  |  |
| `description` | `string?` | `null` |  |
| `icon` | [`WindowIcon?`](WindowIcon.md) | `null` |  |
| `groupCategory` | `string?` | `null` |  |

## `JumpListItemKind`

Kind of jump-list entry. (spec 036 §11.3)

```csharp
public enum JumpListItemKind
```

Namespace: `Microsoft.UI.Reactor`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Task` | `0` | A regular task in the standard "Tasks" group. |
| `Custom` | `1` | A task in a custom [`JumpListItem.GroupCategory`](#groupcategory). |
| `Separator` | `2` | A horizontal separator in the same group as the previous item. |

## `TaskbarItem`

Grouping facade for the taskbar features associated with a [`ReactorWindow`](ReactorWindow.md). Existing [`ReactorWindow.Progress`](ReactorWindow.md#progress), [`ReactorWindow.Overlay`](ReactorWindow.md#overlay), [`ReactorWindow.SetThumbnailToolbar`](ReactorWindow.md#setthumbnailtoolbar), and [`ReactorWindow.ClearThumbnailToolbar`](ReactorWindow.md#clearthumbnailtoolbar) shortcuts remain available.

```csharp
public sealed class TaskbarItem
```

Namespace: `Microsoft.UI.Reactor`.

### Properties

#### `Description`

Shell thumbnail tooltip / description for the taskbar button. The property keeps the last value assigned; native application is best-effort.

```csharp
public string? Description  // get, set
```

#### `Overlay`

Taskbar overlay icon for the owning window.

```csharp
public TaskbarOverlay Overlay  // get
```

#### `Progress`

Taskbar progress indicator for the owning window.

```csharp
public TaskbarProgress Progress  // get
```

### Methods

#### `ClearThumbnailToolbar`

Hide all thumbnail toolbar buttons for the owning window.

```csharp
public void ClearThumbnailToolbar()
```

#### `SetThumbnailToolbar`

Replace the thumbnail toolbar buttons for the owning window.

```csharp
public void SetThumbnailToolbar(IReadOnlyList<ThumbnailToolbarButton> buttons)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `buttons` | `IReadOnlyList<ThumbnailToolbarButton>` |  |

## `TaskbarOverlay`

Taskbar overlay icon ("badge") for a top-level window. Backs [`ReactorWindow.Overlay`](ReactorWindow.md#overlay). (spec 036 §11.2)

```csharp
public sealed class TaskbarOverlay
```

Namespace: `Microsoft.UI.Reactor`.

### Remarks

Setting [`TaskbarOverlay.Icon`](#icon_1) to null clears the overlay. The icon should be 16×16 logical pixels — larger images are downscaled by the shell with quality loss; we emit a one-shot `Debug.WriteLine` when a [`WindowIcon.FromPath`](WindowIcon.md#frompath) source can't be loaded as an HICON.

The overlay needs a raw `HICON`, so it accepts a filesystem path or in-memory data ([`WindowIcon.FromBytes`](WindowIcon.md#frombytes) / [`WindowIcon.FromRgba`](WindowIcon.md#fromrgba)) and skips [`WindowIcon.FromResource`](WindowIcon.md#fromresource). A procedurally-drawn badge is a natural fit for [`WindowIcon.FromRgba`](WindowIcon.md#fromrgba), which needs no file on disk.

Accessibility: [`TaskbarOverlay.AccessibleDescription`](#accessibledescription) flows through the `pszDescription` parameter of `ITaskbarList3.SetOverlayIcon` — without it the overlay is invisible to assistive tech (spec §0.6).

### Properties

#### `AccessibleDescription`

Accessible description handed to the shell's `SetOverlayIcon(pszDescription)` — required for assistive tech to announce the overlay. (spec 036 §0.6)

```csharp
public string? AccessibleDescription  // get, set
```

#### `Icon`

The current overlay icon. Null clears.

```csharp
public WindowIcon? Icon  // get, set
```

## `TaskbarProgress`

Visual taskbar progress for a top-level window. Backs [`ReactorWindow.Progress`](ReactorWindow.md#progress). (spec 036 §11.1)

```csharp
public sealed class TaskbarProgress
```

Namespace: `Microsoft.UI.Reactor`.

### Remarks

All writes are best-effort. The shell is the source of truth — Reactor keeps the last-applied state for round-trip queries and idempotent re-apply only. A [`TaskbarProgressState.None`](#values_1) assignment clears both state and value; an [`TaskbarProgressState.Indeterminate`](#values_1) state ignores [`TaskbarProgress.Value`](#value).

### Properties

#### `State`

Last-applied progress state. (spec 036 §11.1)

```csharp
public TaskbarProgressState State  // get, set
```

#### `Value`

Progress fraction in `[0.0, 1.0]`. Ignored when [`TaskbarProgress.State`](#state) is [`TaskbarProgressState.Indeterminate`](#values_1) or [`TaskbarProgressState.None`](#values_1). Out-of-range values throw [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception).

```csharp
public double Value  // get, set
```

### Methods

#### `Clear`

Shorthand for `State = None` + `Value = 0`.

```csharp
public void Clear()
```

## `TaskbarProgressState`

State of the taskbar progress indicator for a window. Wire-compatible with the shell's `TBPF_*` values. (spec 036 §11.1)

```csharp
public enum TaskbarProgressState
```

Namespace: `Microsoft.UI.Reactor`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `None` | `0` | No progress shown (default). |
| `Indeterminate` | `1` | Marquee animation; [`TaskbarProgress.Value`](#value) is ignored. |
| `Normal` | `2` | Standard determinate progress, green. |
| `Paused` | `3` | Determinate progress, yellow (paused). |
| `Error` | `4` | Determinate progress, red (error). |

## `ThumbnailToolbarButton`

One button in a window's thumbnail toolbar (taskbar live-preview UI). Constructed declaratively and handed to [`ReactorWindow.SetThumbnailToolbar`](ReactorWindow.md#setthumbnailtoolbar). (spec 036 §11.5)

```csharp
public sealed record ThumbnailToolbarButton(string Id, WindowIcon Icon, string Tooltip, Action OnClick, bool IsEnabled = true, bool IsVisible = true, bool DismissOnClick = false)
```

Namespace: `Microsoft.UI.Reactor`.

### Constructors

#### `ThumbnailToolbarButton`

One button in a window's thumbnail toolbar (taskbar live-preview UI). Constructed declaratively and handed to [`ReactorWindow.SetThumbnailToolbar`](ReactorWindow.md#setthumbnailtoolbar). (spec 036 §11.5)

```csharp
public ThumbnailToolbarButton(string Id, WindowIcon Icon, string Tooltip, Action OnClick, bool IsEnabled = true, bool IsVisible = true, bool DismissOnClick = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Id` | `string` |  | Stable per-window identifier; used to diff against the previous call so per-render `SetThumbnailToolbar` updates only the changed buttons. Must be unique within a single set. |
| `Icon` | [`WindowIcon`](WindowIcon.md) |  | Filesystem-backed icon ([`WindowIcon.FromPath`](WindowIcon.md#frompath)) or in-memory icon data ([`WindowIcon.FromBytes`](WindowIcon.md#frombytes) / [`WindowIcon.FromRgba`](WindowIcon.md#fromrgba)). [`WindowIcon.FromResource`](WindowIcon.md#fromresource) sources are not supported on the thumbnail surface (HICON only) and are silently skipped — the button still renders without an icon. |
| `Tooltip` | `string` |  | Hover tooltip; truncated to 259 characters. |
| `OnClick` | `Action` |  | Click delegate. Invoked on the UI thread when the button is pressed. |
| `IsEnabled` | `bool` | `true` | Whether the button accepts input. |
| `IsVisible` | `bool` | `true` | Whether the button is rendered at all. |
| `DismissOnClick` | `bool` | `false` | When true, clicking the button hides the live-preview popup. |

### Properties

#### `DismissOnClick`

When true, clicking the button hides the live-preview popup.

```csharp
public bool DismissOnClick  // get, init
```

#### `Icon`

Filesystem-backed icon ([`WindowIcon.FromPath`](WindowIcon.md#frompath)) or in-memory icon data ([`WindowIcon.FromBytes`](WindowIcon.md#frombytes) / [`WindowIcon.FromRgba`](WindowIcon.md#fromrgba)). [`WindowIcon.FromResource`](WindowIcon.md#fromresource) sources are not supported on the thumbnail surface (HICON only) and are silently skipped — the button still renders without an icon.

```csharp
public WindowIcon Icon  // get, init
```

#### `Id`

Stable per-window identifier; used to diff against the previous call so per-render `SetThumbnailToolbar` updates only the changed buttons. Must be unique within a single set.

```csharp
public string Id  // get, init
```

#### `IsEnabled`

Whether the button accepts input.

```csharp
public bool IsEnabled  // get, init
```

#### `IsVisible`

Whether the button is rendered at all.

```csharp
public bool IsVisible  // get, init
```

#### `OnClick`

Click delegate. Invoked on the UI thread when the button is pressed.

```csharp
public Action OnClick  // get, init
```

#### `Tooltip`

Hover tooltip; truncated to 259 characters.

```csharp
public string Tooltip  // get, init
```
