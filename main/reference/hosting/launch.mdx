---
title: "Launch and activation"
description: "How the app was started, and requests to embed it."
---

**Learn more:** [Advanced Windowing](/microsoft-ui-reactor/main/guides/windowing-advanced), [Desktop integration](/microsoft-ui-reactor/main/guides/desktop-integration)

## `EmbedRequest` [#embedrequest]

```csharp
public sealed record EmbedRequest(WindowEmbedStyle Style, int HostPid, bool InitialVisibility)
```

Namespace: `Microsoft.UI.Reactor`.

### Constructors [#constructors]

#### `EmbedRequest` [#embedrequest_1]

```csharp
public EmbedRequest(WindowEmbedStyle Style, int HostPid, bool InitialVisibility)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Style` | [`WindowEmbedStyle`](/microsoft-ui-reactor/main/reference/hosting/window-options#windowembedstyle) |  |
| `HostPid` | `int` |  |
| `InitialVisibility` | `bool` |  |

### Properties [#properties]

#### `HostPid` [#hostpid]

```csharp
public int HostPid  // get, init
```

#### `InitialVisibility` [#initialvisibility]

```csharp
public bool InitialVisibility  // get, init
```

#### `Style` [#style]

```csharp
public WindowEmbedStyle Style  // get, init
```

## `LaunchActivation` [#launchactivation]

Parsed launch-activation payload. The argument string and file list are best-effort — both can be empty when the OS surface didn't provide them.

```csharp
public sealed record LaunchActivation(LaunchKind Kind, string? Arguments, IReadOnlyList<string> Files)
```

Namespace: `Microsoft.UI.Reactor`.

### Constructors [#constructors_1]

#### `LaunchActivation` [#launchactivation_1]

Parsed launch-activation payload. The argument string and file list are best-effort — both can be empty when the OS surface didn't provide them.

```csharp
public LaunchActivation(LaunchKind Kind, string? Arguments, IReadOnlyList<string> Files)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Kind` | [`LaunchKind`](#launchkind) |  |
| `Arguments` | `string?` |  |
| `Files` | `IReadOnlyList<string>` |  |

### Properties [#properties_1]

#### `Arguments` [#arguments]

```csharp
public string? Arguments  // get, init
```

#### `Files` [#files]

```csharp
public IReadOnlyList<string> Files  // get, init
```

#### `Kind` [#kind]

```csharp
public LaunchKind Kind  // get, init
```

#### `Normal` [#normal]

Sentinel for Phase 1, when no real activation parsing exists yet.

```csharp
public static LaunchActivation Normal  // get
```

### Methods [#methods]

#### `TryResolve` [#tryresolve]

Resolve [`LaunchActivation.Arguments`](#arguments) as a deep-link URI through the supplied [`DeepLinkMap<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/DeepLinkMap). Returns `true` only when [`LaunchActivation.Arguments`](#arguments) is non-empty **and** the map matched a registered pattern. The shell-launch convention for Reactor is that jump-list / tray / thumbnail-toolbar entries carry deep-link URIs in their argument strings (see [`JumpListItem.ForUri`](/microsoft-ui-reactor/main/reference/hosting/taskbar#foruri)); this helper plumbs that convention into the navigation system. (spec 036 §11.6)

```csharp
public bool TryResolve<TRoute>(DeepLinkMap<TRoute> map, out DeepLinkResult<TRoute> result) where TRoute : notnull
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `map` | [`DeepLinkMap<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/DeepLinkMap) |  |
| `result` | [`DeepLinkResult<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/DeepLinkResult) |  |

## `LaunchKind` [#launchkind]

How the process was launched.

```csharp
public enum LaunchKind
```

Namespace: `Microsoft.UI.Reactor`.

### Remarks [#remarks]

The OS shell presents jump-list entries, tray "Open" / double-click commands, and thumbnail-toolbar buttons as plain process re-launches — all three arrive at `OnLaunched` with the same shape (a non-empty argument string and no extended activation kind). Reactor cannot distinguish them from the WinUI surface, so all three roll up under [`LaunchKind.JumpList`](#values). Apps that need finer granularity should encode it in the argument URI itself (e.g. a path prefix or query parameter) and inspect [`LaunchActivation.Arguments`](#arguments).

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `Normal` | `0` | Launched via the standard executable / shortcut entry point. |
| `JumpList` | `1` | Launched with a non-empty argument string from a shell re-launch — jump list, tray "Open", or thumbnail-toolbar button. The three sources are indistinguishable at the WinUI activation surface. |
| `Toast` | `2` | Launched in response to a toast click. |
| `Protocol` | `3` | Launched via a custom URI protocol handler. |
| `File` | `4` | Launched via file association. |
