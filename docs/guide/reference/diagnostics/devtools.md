# Developer tools hosting

Types the developer tools use to start inside an app.

**Learn more:** [Devtools](../../devtools.md)

## `DevtoolsCliOptions`

Parsed result of a `--devtools` / `--preview` invocation. [`DevtoolsCliOptions.Subverb`](#subverb) is `null` when neither flag is present.

```csharp
public sealed record DevtoolsCliOptions(DevtoolsSubverb? Subverb, string? ComponentName, bool VsCodeMode, int Fps, string? ListOutputPath, string? ScreenshotOutputPath, int? McpPort, string? LogLevel, McpTransport Transport, bool UsedDeprecatedPreview, bool PreviewAndDevtoolsConflict, string? ProjectIdentifier = null, bool LogsDisabled = false, int? LogsCapacityMb = null, bool EmbedRequested = false, WindowEmbedStyle EmbedStyle = WindowEmbedStyle.Child, int? EmbedHostPid = null, string? EmbedValidationError = null, bool EmbedAutoEnabledVsCode = false, double? WindowWidth = null, double? WindowHeight = null, double? WindowX = null, double? WindowY = null)
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Constructors

#### `DevtoolsCliOptions`

Parsed result of a `--devtools` / `--preview` invocation. [`DevtoolsCliOptions.Subverb`](#subverb) is `null` when neither flag is present.

```csharp
public DevtoolsCliOptions(DevtoolsSubverb? Subverb, string? ComponentName, bool VsCodeMode, int Fps, string? ListOutputPath, string? ScreenshotOutputPath, int? McpPort, string? LogLevel, McpTransport Transport, bool UsedDeprecatedPreview, bool PreviewAndDevtoolsConflict, string? ProjectIdentifier = null, bool LogsDisabled = false, int? LogsCapacityMb = null, bool EmbedRequested = false, WindowEmbedStyle EmbedStyle = WindowEmbedStyle.Child, int? EmbedHostPid = null, string? EmbedValidationError = null, bool EmbedAutoEnabledVsCode = false, double? WindowWidth = null, double? WindowHeight = null, double? WindowX = null, double? WindowY = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Subverb` | `DevtoolsSubverb?` |  |  |
| `ComponentName` | `string?` |  |  |
| `VsCodeMode` | `bool` |  |  |
| `Fps` | `int` |  |  |
| `ListOutputPath` | `string?` |  |  |
| `ScreenshotOutputPath` | `string?` |  |  |
| `McpPort` | `int?` |  |  |
| `LogLevel` | `string?` |  |  |
| `Transport` | [`McpTransport`](#mcptransport) |  |  |
| `UsedDeprecatedPreview` | `bool` |  |  |
| `PreviewAndDevtoolsConflict` | `bool` |  |  |
| `ProjectIdentifier` | `string?` | `null` |  |
| `LogsDisabled` | `bool` | `false` |  |
| `LogsCapacityMb` | `int?` | `null` |  |
| `EmbedRequested` | `bool` | `false` |  |
| `EmbedStyle` | [`WindowEmbedStyle`](../hosting/window-options.md#windowembedstyle) | `WindowEmbedStyle.Child` |  |
| `EmbedHostPid` | `int?` | `null` |  |
| `EmbedValidationError` | `string?` | `null` |  |
| `EmbedAutoEnabledVsCode` | `bool` | `false` |  |
| `WindowWidth` | `double?` | `null` |  |
| `WindowHeight` | `double?` | `null` |  |
| `WindowX` | `double?` | `null` |  |
| `WindowY` | `double?` | `null` |  |

### Properties

#### `ComponentName`

```csharp
public string? ComponentName  // get, init
```

#### `EmbedAutoEnabledVsCode`

```csharp
public bool EmbedAutoEnabledVsCode  // get, init
```

#### `EmbedHostPid`

```csharp
public int? EmbedHostPid  // get, init
```

#### `EmbedRequested`

```csharp
public bool EmbedRequested  // get, init
```

#### `EmbedStyle`

```csharp
public WindowEmbedStyle EmbedStyle  // get, init
```

#### `EmbedValidationError`

```csharp
public string? EmbedValidationError  // get, init
```

#### `Fps`

```csharp
public int Fps  // get, init
```

#### `ListOutputPath`

```csharp
public string? ListOutputPath  // get, init
```

#### `LogLevel`

```csharp
public string? LogLevel  // get, init
```

#### `LogsCapacityMb`

```csharp
public int? LogsCapacityMb  // get, init
```

#### `LogsDisabled`

```csharp
public bool LogsDisabled  // get, init
```

#### `McpPort`

```csharp
public int? McpPort  // get, init
```

#### `PreviewAndDevtoolsConflict`

```csharp
public bool PreviewAndDevtoolsConflict  // get, init
```

#### `ProjectIdentifier`

```csharp
public string? ProjectIdentifier  // get, init
```

#### `ScreenshotOutputPath`

```csharp
public string? ScreenshotOutputPath  // get, init
```

#### `Subverb`

```csharp
public DevtoolsSubverb? Subverb  // get, init
```

#### `Transport`

```csharp
public McpTransport Transport  // get, init
```

#### `UsedDeprecatedPreview`

```csharp
public bool UsedDeprecatedPreview  // get, init
```

#### `VsCodeMode`

```csharp
public bool VsCodeMode  // get, init
```

#### `WindowHeight`

```csharp
public double? WindowHeight  // get, init
```

#### `WindowWidth`

```csharp
public double? WindowWidth  // get, init
```

#### `WindowX`

```csharp
public double? WindowX  // get, init
```

#### `WindowY`

```csharp
public double? WindowY  // get, init
```

## `DevtoolsSubverb`

```csharp
public enum DevtoolsSubverb
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Run` | `0` |  |
| `List` | `1` |  |
| `Screenshot` | `2` |  |
| `Tree` | `3` |  |
| `App` | `4` |  |

## `IReactorDevtoolsHost`

```csharp
public interface IReactorDevtoolsHost
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Methods

#### `BuildDevtoolsMenu`

```csharp
public Element? BuildDevtoolsMenu(Func<IEnumerable<MenuFlyoutItemBase>>? items, string glyph, string toolTip, string? automationId)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `items` | `Func<IEnumerable<MenuFlyoutItemBase>>?` |  |
| `glyph` | `string` |  |
| `toolTip` | `string` |  |
| `automationId` | `string?` |  |

#### `TryHandleCommandLine`

```csharp
public bool TryHandleCommandLine(ReactorDevtoolsBootRequest request)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `request` | [`ReactorDevtoolsBootRequest`](#reactordevtoolsbootrequest) |  |

## `McpTransport`

Which MCP transport the devtools surface uses.

```csharp
public enum McpTransport
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Http` | `0` |  |
| `Stdio` | `1` |  |

## `ReactorDevtoolsBootRequest`

```csharp
public sealed record ReactorDevtoolsBootRequest(DevtoolsCliOptions Options, string Title, double? Width, double? Height, bool FullScreen, Type? HostRoot, Func<Component>? HostRootFactory, Func<RenderContext, Element>? RootRenderFunc, Action<ReactorHost>? Configure)
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Constructors

#### `ReactorDevtoolsBootRequest`

```csharp
public ReactorDevtoolsBootRequest(DevtoolsCliOptions Options, string Title, double? Width, double? Height, bool FullScreen, Type? HostRoot, Func<Component>? HostRootFactory, Func<RenderContext, Element>? RootRenderFunc, Action<ReactorHost>? Configure)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Options` | [`DevtoolsCliOptions`](#devtoolsclioptions) |  |
| `Title` | `string` |  |
| `Width` | `double?` |  |
| `Height` | `double?` |  |
| `FullScreen` | `bool` |  |
| `HostRoot` | `Type?` |  |
| `HostRootFactory` | `Func<Component>?` |  |
| `RootRenderFunc` | `Func<RenderContext, Element>?` |  |
| `Configure` | `Action<ReactorHost>?` |  |

### Properties

#### `Configure`

```csharp
public Action<ReactorHost>? Configure  // get, init
```

#### `FullScreen`

```csharp
public bool FullScreen  // get, init
```

#### `Height`

```csharp
public double? Height  // get, init
```

#### `HostRoot`

```csharp
public Type? HostRoot  // get, init
```

#### `HostRootFactory`

```csharp
public Func<Component>? HostRootFactory  // get, init
```

#### `Options`

```csharp
public DevtoolsCliOptions Options  // get, init
```

#### `RootRenderFunc`

```csharp
public Func<RenderContext, Element>? RootRenderFunc  // get, init
```

#### `Title`

```csharp
public string Title  // get, init
```

#### `Width`

```csharp
public double? Width  // get, init
```

## `ReactorDevtoolsBootstrap`

```csharp
public static class ReactorDevtoolsBootstrap
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Methods

#### `Register`

```csharp
public static void Register(IReactorDevtoolsHost host)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `host` | [`IReactorDevtoolsHost`](#ireactordevtoolshost) |  |
