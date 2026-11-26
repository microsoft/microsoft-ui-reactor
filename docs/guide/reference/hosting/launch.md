# Launch and activation

How the app was started, and requests to embed it.

**Learn more:** [Advanced Windowing](../../windowing-advanced.md)

## `EmbedRequest`

```csharp
public sealed record EmbedRequest(WindowEmbedStyle Style, int HostPid, bool InitialVisibility)
```

Namespace: `Microsoft.UI.Reactor`.

### Constructors

#### `EmbedRequest`

```csharp
public EmbedRequest(WindowEmbedStyle Style, int HostPid, bool InitialVisibility)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Style` | [`WindowEmbedStyle`](window-options.md#windowembedstyle) |  |
| `HostPid` | `int` |  |
| `InitialVisibility` | `bool` |  |

### Properties

#### `HostPid`

```csharp
public int HostPid  // get, init
```

#### `InitialVisibility`

```csharp
public bool InitialVisibility  // get, init
```

#### `Style`

```csharp
public WindowEmbedStyle Style  // get, init
```

## `LaunchActivation`

Parsed launch-activation payload. The argument string and file list are best-effort — both can be empty when the OS surface didn't provide them.

```csharp
public sealed record LaunchActivation(LaunchKind Kind, string? Arguments, IReadOnlyList<string> Files)
```

Namespace: `Microsoft.UI.Reactor`.

### Constructors

#### `LaunchActivation`

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

### Properties

#### `Arguments`

```csharp
public string? Arguments  // get, init
```

#### `Files`

```csharp
public IReadOnlyList<string> Files  // get, init
```

#### `Kind`

```csharp
public LaunchKind Kind  // get, init
```

#### `Normal`

Sentinel for Phase 1, when no real activation parsing exists yet.

```csharp
public static LaunchActivation Normal  // get
```

### Methods

#### `TryResolve`

Resolve [`LaunchActivation.Arguments`](#arguments) as a deep-link URI through the supplied [`DeepLinkMap<TRoute>`](../navigation/DeepLinkMap.md). Returns `true` only when [`LaunchActivation.Arguments`](#arguments) is non-empty **and** the map matched a registered pattern. The shell-launch convention for Reactor is that jump-list / tray / thumbnail-toolbar entries carry deep-link URIs in their argument strings (see [`JumpListItem.ForUri`](taskbar.md#foruri)); this helper plumbs that convention into the navigation system. (spec 036 §11.6)

```csharp
public bool TryResolve<TRoute>(DeepLinkMap<TRoute> map, out DeepLinkResult<TRoute> result) where TRoute : notnull
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `map` | [`DeepLinkMap<TRoute>`](../navigation/DeepLinkMap.md) |  |
| `result` | [`DeepLinkResult<TRoute>`](../navigation/DeepLinkResult.md) |  |

## `LaunchKind`

How the process was launched.

```csharp
public enum LaunchKind
```

Namespace: `Microsoft.UI.Reactor`.

### Remarks

The OS shell presents jump-list entries, tray "Open" / double-click commands, and thumbnail-toolbar buttons as plain process re-launches — all three arrive at `OnLaunched` with the same shape (a non-empty argument string and no extended activation kind). Reactor cannot distinguish them from the WinUI surface, so all three roll up under [`LaunchKind.JumpList`](#values). Apps that need finer granularity should encode it in the argument URI itself (e.g. a path prefix or query parameter) and inspect [`LaunchActivation.Arguments`](#arguments).

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Normal` | `0` | Launched via the standard executable / shortcut entry point. |
| `JumpList` | `1` | Launched with a non-empty argument string from a shell re-launch — jump list, tray "Open", or thumbnail-toolbar button. The three sources are indistinguishable at the WinUI activation surface. |
| `Toast` | `2` | Launched in response to a toast click. |
| `Protocol` | `3` | Launched via a custom URI protocol handler. |
| `File` | `4` | Launched via file association. |
