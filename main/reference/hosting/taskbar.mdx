---
title: "Taskbar and jump lists"
description: "Types for the taskbar button, its progress, overlay, thumbnail buttons and jump list."
---

## `JumpList` [#jumplist]

Process-scoped jump list — the right-click flyout that appears on the app's taskbar button or Start tile. Driven through `Windows.UI.StartScreen.JumpList` for packaged apps and Win32 `ICustomDestinationList` for unpackaged. (spec 036 §11.3)

```csharp
public static class JumpList
```

Namespace: `Microsoft.UI.Reactor`.

### Remarks [#remarks]

**Security note:** [`JumpListItem.Arguments`](#arguments) are command-line strings handed to a freshly launched process by the shell. Apps must parse them through `Reactor.Cli`'s arg parser before acting on them — a malformed shortcut, a forged jump-list entry left by another process, or a stale entry from an earlier installation will otherwise reach the new process unchecked. Reactor itself never auto-acts on these strings; [`ReactorAppContext.LaunchActivation`](/microsoft-ui-reactor/main/reference/hosting/ReactorAppContext#launchactivation) surfaces them so the app can validate before dispatching.

Unpackaged apps must set [`JumpList.AppUserModelId`](#appusermodelid) before the first [`JumpList.UpdateAsync`](#updateasync) call — without it the shell has no stable identity to attach the jump list to.

### Properties [#properties]

#### `AppUserModelId` [#appusermodelid]

Application User Model ID used by the shell to associate the jump list with this process. Required for unpackaged apps; ignored when the process runs under an MSIX package (the package manifest supplies it).

```csharp
public static string? AppUserModelId  // get, set
```

#### `ShowFrequent` [#showfrequent]

Whether the OS-managed "Frequent" category is shown. The list contents are owned by the shell — apps can only toggle visibility.

```csharp
public static bool ShowFrequent  // get, set
```

#### `ShowRecent` [#showrecent]

Whether the OS-managed "Recent" category is shown. The list contents are owned by the shell — apps can only toggle visibility.

```csharp
public static bool ShowRecent  // get, set
```

### Methods [#methods]

#### `ClearAsync` [#clearasync]

Clear all user-defined jump-list entries. UI-thread only.

```csharp
public static Task<IReadOnlyList<string>> ClearAsync()
```

**Returns:** The removed arguments observed by the clear transaction.

**Remarks**

Like [`JumpList.UpdateAsync`](#updateasync), a successful clear consumes Windows' removal records. Retain the returned arguments before publishing destinations again.

#### `GetRemovedArgumentsAsync` [#getremovedargumentsasync]

Read the argument strings of entries removed by the user. UI-thread only.

```csharp
public static Task<IReadOnlyList<string>> GetRemovedArgumentsAsync()
```

**Remarks**

This does not update the list or acknowledge removals. Call before [`JumpList.UpdateAsync`](#updateasync) and remove matching entries from the app's destination tracking data. A successful update clears its removal records; also retain the arguments returned by that update to capture removals that occurred after this query. Only reintroduce a destination after new user activity. On unpackaged apps, only shell links targeting the current executable are returned; shell-item destinations are not created by this wrapper. Argument strings are compared ordinally, not as parsed command lines.

#### `UpdateAsync` [#updateasync]

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

## `JumpListItem` [#jumplistitem]

Shape of a single jump-list entry. (spec 036 §11.3 / §11.6)

```csharp
public sealed record JumpListItem(string Title, string Arguments, JumpListItemKind Kind = JumpListItemKind.Task, string? Description = null, WindowIcon? Icon = null, string? GroupCategory = null)
```

Namespace: `Microsoft.UI.Reactor`.

### Constructors [#constructors]

#### `JumpListItem` [#jumplistitem_1]

Shape of a single jump-list entry. (spec 036 §11.3 / §11.6)

```csharp
public JumpListItem(string Title, string Arguments, JumpListItemKind Kind = JumpListItemKind.Task, string? Description = null, WindowIcon? Icon = null, string? GroupCategory = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Title` | `string` |  | User-visible label for the entry. |
| `Arguments` | `string` |  | Command-line arguments handed to a fresh process invocation when the user activates the entry. **Recommended convention:** use a deep-link URI string (the same shape [`DeepLinkMap.Resolve`](/microsoft-ui-reactor/main/reference/navigation/DeepLinkMap#resolve) accepts) and resolve via [`LaunchActivation.TryResolve`](/microsoft-ui-reactor/main/reference/hosting/launch#tryresolve) in the startup callback. The [`JumpListItem.ForUri`](#foruri) factory makes that convention discoverable. **Security:** these strings round-trip through the shell back into the next process launch. Apps must validate them via `Reactor.Cli`'s arg parser or [`DeepLinkMap<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/DeepLinkMap) before acting on them — Reactor itself never auto-executes the arguments. |
| `Kind` | [`JumpListItemKind`](#jumplistitemkind) | `JumpListItemKind.Task` | Whether this is a regular task, a separator, or part of a custom group. |
| `Description` | `string?` | `null` | Optional tooltip / accessible description. |
| `Icon` | [`WindowIcon?`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon) | `null` | Optional icon shown next to the title. The two jump-list paths want different things, and each silently skips what it cannot use: **Packaged** — `WindowIcon.FromResource` only. The WinRT `JumpListItem.Logo` takes a packaged `Uri`, so a filesystem path is skipped. **Unpackaged** — `WindowIcon.FromPath` only. The Win32 path hands the source to `IShellLink.SetIconLocation`, which wants a filesystem path, so an `ms-appx:` URI is skipped instead. See `JumpListComInterop.BuildShellLinkArray`. Binary sources (`WindowIcon.FromBytes` / `FromRgba`) work on neither: they have no URI and no path. Both arms skip them via the empty-`Source` guard. Ship a sidecar `.ico` or a packaged asset for jump-list entries. |
| `GroupCategory` | `string?` | `null` | Group label for [`JumpListItemKind.Custom`](#values) items. |

### Properties [#properties_1]

#### `Arguments` [#arguments]

Command-line arguments handed to a fresh process invocation when the user activates the entry. **Recommended convention:** use a deep-link URI string (the same shape [`DeepLinkMap.Resolve`](/microsoft-ui-reactor/main/reference/navigation/DeepLinkMap#resolve) accepts) and resolve via [`LaunchActivation.TryResolve`](/microsoft-ui-reactor/main/reference/hosting/launch#tryresolve) in the startup callback. The [`JumpListItem.ForUri`](#foruri) factory makes that convention discoverable.

**Security:** these strings round-trip through the shell back into the next process launch. Apps must validate them via `Reactor.Cli`'s arg parser or [`DeepLinkMap<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/DeepLinkMap) before acting on them — Reactor itself never auto-executes the arguments.

```csharp
public string Arguments  // get, init
```

#### `Description` [#description]

Optional tooltip / accessible description.

```csharp
public string? Description  // get, init
```

#### `GroupCategory` [#groupcategory]

Group label for [`JumpListItemKind.Custom`](#values) items.

```csharp
public string? GroupCategory  // get, init
```

#### `Icon` [#icon]

Optional icon shown next to the title. The two jump-list paths want different things, and each silently skips what it cannot use:

- **Packaged** — `WindowIcon.FromResource` only. The WinRT `JumpListItem.Logo` takes a packaged `Uri`, so a filesystem path is skipped.
- **Unpackaged** — `WindowIcon.FromPath` only. The Win32 path hands the source to `IShellLink.SetIconLocation`, which wants a filesystem path, so an `ms-appx:` URI is skipped instead. See `JumpListComInterop.BuildShellLinkArray`.

Binary sources (`WindowIcon.FromBytes` / `FromRgba`) work on neither: they have no URI and no path. Both arms skip them via the empty-`Source` guard. Ship a sidecar `.ico` or a packaged asset for jump-list entries.

```csharp
public WindowIcon? Icon  // get, init
```

#### `Kind` [#kind]

Whether this is a regular task, a separator, or part of a custom group.

```csharp
public JumpListItemKind Kind  // get, init
```

#### `Title` [#title]

User-visible label for the entry.

```csharp
public string Title  // get, init
```

### Methods [#methods_1]

#### `EncodeArgument` [#encodeargument]

Encode a single argv value per `CommandLineToArgvW` rules. Public so callers building command lines piecewise can reuse the same escaper Reactor uses internally for jump-list entries.

```csharp
public static string EncodeArgument(string argument)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `argument` | `string` |  |

#### `EncodeArguments` [#encodearguments]

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

#### `ForCommandLine` [#forcommandline]

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
| `icon` | [`WindowIcon?`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon) | `null` |  |
| `groupCategory` | `string?` | `null` |  |

#### `ForUri` [#foruri]

Convenience factory for the recommended "Arguments == deep-link URI" convention. Equivalent to `new JumpListItem(title, uri, Task, ...)`; makes the navigation bridge discoverable so apps don't need to read the Arguments doc to know they can hand the activation to a [`DeepLinkMap<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/DeepLinkMap). (spec 036 §11.3 / §11.6)

```csharp
public static JumpListItem ForUri(string title, string uri, string? description = null, WindowIcon? icon = null, string? groupCategory = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `title` | `string` |  |  |
| `uri` | `string` |  |  |
| `description` | `string?` | `null` |  |
| `icon` | [`WindowIcon?`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon) | `null` |  |
| `groupCategory` | `string?` | `null` |  |

## `JumpListItemKind` [#jumplistitemkind]

Kind of jump-list entry. (spec 036 §11.3)

```csharp
public enum JumpListItemKind
```

Namespace: `Microsoft.UI.Reactor`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `Task` | `0` | A regular task in the standard "Tasks" group. |
| `Custom` | `1` | A task in a custom [`JumpListItem.GroupCategory`](#groupcategory). |
| `Separator` | `2` | A horizontal separator in the same group as the previous item. |

## `TaskbarItem` [#taskbaritem]

Grouping facade for the taskbar features associated with a [`ReactorWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow). Existing [`ReactorWindow.Progress`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#progress), [`ReactorWindow.Overlay`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#overlay), [`ReactorWindow.SetThumbnailToolbar`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#setthumbnailtoolbar), and [`ReactorWindow.ClearThumbnailToolbar`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#clearthumbnailtoolbar) shortcuts remain available.

```csharp
public sealed class TaskbarItem
```

Namespace: `Microsoft.UI.Reactor`.

### Properties [#properties_2]

#### `Description` [#description_1]

Shell thumbnail tooltip / description for the taskbar button. The property keeps the last value assigned; native application is best-effort.

```csharp
public string? Description  // get, set
```

#### `Overlay` [#overlay]

Taskbar overlay icon for the owning window.

```csharp
public TaskbarOverlay Overlay  // get
```

#### `Progress` [#progress]

Taskbar progress indicator for the owning window.

```csharp
public TaskbarProgress Progress  // get
```

### Methods [#methods_2]

#### `ClearThumbnailToolbar` [#clearthumbnailtoolbar]

Hide all thumbnail toolbar buttons for the owning window.

```csharp
public void ClearThumbnailToolbar()
```

#### `SetThumbnailToolbar` [#setthumbnailtoolbar]

Replace the thumbnail toolbar buttons for the owning window.

```csharp
public void SetThumbnailToolbar(IReadOnlyList<ThumbnailToolbarButton> buttons)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `buttons` | `IReadOnlyList<ThumbnailToolbarButton>` |  |

## `TaskbarOverlay` [#taskbaroverlay]

Taskbar overlay icon ("badge") for a top-level window. Backs [`ReactorWindow.Overlay`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#overlay). (spec 036 §11.2)

```csharp
public sealed class TaskbarOverlay
```

Namespace: `Microsoft.UI.Reactor`.

### Remarks [#remarks_1]

Setting [`TaskbarOverlay.Icon`](#icon_1) to null clears the overlay. The icon should be 16×16 logical pixels — larger images are downscaled by the shell with quality loss; we emit a one-shot `Debug.WriteLine` when a [`WindowIcon.FromPath`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frompath) source can't be loaded as an HICON.

The overlay needs a raw `HICON`, so it accepts a filesystem path or in-memory data ([`WindowIcon.FromBytes`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frombytes) / [`WindowIcon.FromRgba`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromrgba)) and skips [`WindowIcon.FromResource`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromresource). A procedurally-drawn badge is a natural fit for [`WindowIcon.FromRgba`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromrgba), which needs no file on disk.

Accessibility: [`TaskbarOverlay.AccessibleDescription`](#accessibledescription) flows through the `pszDescription` parameter of `ITaskbarList3.SetOverlayIcon` — without it the overlay is invisible to assistive tech (spec §0.6).

### Properties [#properties_3]

#### `AccessibleDescription` [#accessibledescription]

Accessible description handed to the shell's `SetOverlayIcon(pszDescription)` — required for assistive tech to announce the overlay. (spec 036 §0.6)

```csharp
public string? AccessibleDescription  // get, set
```

#### `Icon` [#icon_1]

The current overlay icon. Null clears.

```csharp
public WindowIcon? Icon  // get, set
```

## `TaskbarProgress` [#taskbarprogress]

Visual taskbar progress for a top-level window. Backs [`ReactorWindow.Progress`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#progress). (spec 036 §11.1)

```csharp
public sealed class TaskbarProgress
```

Namespace: `Microsoft.UI.Reactor`.

### Remarks [#remarks_2]

All writes are best-effort. The shell is the source of truth — Reactor keeps the last-applied state for round-trip queries and idempotent re-apply only. A [`TaskbarProgressState.None`](#values_1) assignment clears both state and value; an [`TaskbarProgressState.Indeterminate`](#values_1) state ignores [`TaskbarProgress.Value`](#value).

### Properties [#properties_4]

#### `State` [#state]

Last-applied progress state. (spec 036 §11.1)

```csharp
public TaskbarProgressState State  // get, set
```

#### `Value` [#value]

Progress fraction in `[0.0, 1.0]`. Ignored when [`TaskbarProgress.State`](#state) is [`TaskbarProgressState.Indeterminate`](#values_1) or [`TaskbarProgressState.None`](#values_1). Out-of-range values throw [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception).

```csharp
public double Value  // get, set
```

### Methods [#methods_3]

#### `Clear` [#clear]

Shorthand for `State = None` + `Value = 0`.

```csharp
public void Clear()
```

## `TaskbarProgressState` [#taskbarprogressstate]

State of the taskbar progress indicator for a window. Wire-compatible with the shell's `TBPF_*` values. (spec 036 §11.1)

```csharp
public enum TaskbarProgressState
```

Namespace: `Microsoft.UI.Reactor`.

### Values [#values_1]

| Name | Value | Description |
| --- | --- | --- |
| `None` | `0` | No progress shown (default). |
| `Indeterminate` | `1` | Marquee animation; [`TaskbarProgress.Value`](#value) is ignored. |
| `Normal` | `2` | Standard determinate progress, green. |
| `Paused` | `3` | Determinate progress, yellow (paused). |
| `Error` | `4` | Determinate progress, red (error). |

## `ThumbnailToolbarButton` [#thumbnailtoolbarbutton]

One button in a window's thumbnail toolbar (taskbar live-preview UI). Constructed declaratively and handed to [`ReactorWindow.SetThumbnailToolbar`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#setthumbnailtoolbar). (spec 036 §11.5)

```csharp
public sealed record ThumbnailToolbarButton(string Id, WindowIcon Icon, string Tooltip, Action OnClick, bool IsEnabled = true, bool IsVisible = true, bool DismissOnClick = false)
```

Namespace: `Microsoft.UI.Reactor`.

### Constructors [#constructors_1]

#### `ThumbnailToolbarButton` [#thumbnailtoolbarbutton_1]

One button in a window's thumbnail toolbar (taskbar live-preview UI). Constructed declaratively and handed to [`ReactorWindow.SetThumbnailToolbar`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow#setthumbnailtoolbar). (spec 036 §11.5)

```csharp
public ThumbnailToolbarButton(string Id, WindowIcon Icon, string Tooltip, Action OnClick, bool IsEnabled = true, bool IsVisible = true, bool DismissOnClick = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Id` | `string` |  | Stable per-window identifier; used to diff against the previous call so per-render `SetThumbnailToolbar` updates only the changed buttons. Must be unique within a single set. |
| `Icon` | [`WindowIcon`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon) |  | Filesystem-backed icon ([`WindowIcon.FromPath`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frompath)) or in-memory icon data ([`WindowIcon.FromBytes`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frombytes) / [`WindowIcon.FromRgba`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromrgba)). [`WindowIcon.FromResource`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromresource) sources are not supported on the thumbnail surface (HICON only) and are silently skipped — the button still renders without an icon. |
| `Tooltip` | `string` |  | Hover tooltip; truncated to 259 characters. |
| `OnClick` | `Action` |  | Click delegate. Invoked on the UI thread when the button is pressed. |
| `IsEnabled` | `bool` | `true` | Whether the button accepts input. |
| `IsVisible` | `bool` | `true` | Whether the button is rendered at all. |
| `DismissOnClick` | `bool` | `false` | When true, clicking the button hides the live-preview popup. |

### Properties [#properties_5]

#### `DismissOnClick` [#dismissonclick]

When true, clicking the button hides the live-preview popup.

```csharp
public bool DismissOnClick  // get, init
```

#### `Icon` [#icon_2]

Filesystem-backed icon ([`WindowIcon.FromPath`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frompath)) or in-memory icon data ([`WindowIcon.FromBytes`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frombytes) / [`WindowIcon.FromRgba`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromrgba)). [`WindowIcon.FromResource`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromresource) sources are not supported on the thumbnail surface (HICON only) and are silently skipped — the button still renders without an icon.

```csharp
public WindowIcon Icon  // get, init
```

#### `Id` [#id]

Stable per-window identifier; used to diff against the previous call so per-render `SetThumbnailToolbar` updates only the changed buttons. Must be unique within a single set.

```csharp
public string Id  // get, init
```

#### `IsEnabled` [#isenabled]

Whether the button accepts input.

```csharp
public bool IsEnabled  // get, init
```

#### `IsVisible` [#isvisible]

Whether the button is rendered at all.

```csharp
public bool IsVisible  // get, init
```

#### `OnClick` [#onclick]

Click delegate. Invoked on the UI thread when the button is pressed.

```csharp
public Action OnClick  // get, init
```

#### `Tooltip` [#tooltip]

Hover tooltip; truncated to 259 characters.

```csharp
public string Tooltip  // get, init
```
