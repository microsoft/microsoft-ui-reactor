---
title: "Developer tools hosting"
description: "Types the developer tools use to start inside an app."
---

## `DevtoolsCliOptions` [#devtoolsclioptions]

Parsed result of a `--devtools` / `--preview` invocation. [`DevtoolsCliOptions.Subverb`](#subverb) is `null` when neither flag is present.

```csharp
public sealed record DevtoolsCliOptions(DevtoolsSubverb? Subverb, string? ComponentName, bool VsCodeMode, int Fps, string? ListOutputPath, string? ScreenshotOutputPath, int? McpPort, string? LogLevel, McpTransport Transport, bool UsedDeprecatedPreview, bool PreviewAndDevtoolsConflict, string? ProjectIdentifier = null, bool LogsDisabled = false, int? LogsCapacityMb = null, bool EmbedRequested = false, WindowEmbedStyle EmbedStyle = WindowEmbedStyle.Child, int? EmbedHostPid = null, string? EmbedValidationError = null, bool EmbedAutoEnabledVsCode = false, double? WindowWidth = null, double? WindowHeight = null, double? WindowX = null, double? WindowY = null)
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Constructors [#constructors]

#### `DevtoolsCliOptions` [#devtoolsclioptions_1]

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
| `EmbedStyle` | [`WindowEmbedStyle`](/microsoft-ui-reactor/main/reference/hosting/window-options#windowembedstyle) | `WindowEmbedStyle.Child` |  |
| `EmbedHostPid` | `int?` | `null` |  |
| `EmbedValidationError` | `string?` | `null` |  |
| `EmbedAutoEnabledVsCode` | `bool` | `false` |  |
| `WindowWidth` | `double?` | `null` |  |
| `WindowHeight` | `double?` | `null` |  |
| `WindowX` | `double?` | `null` |  |
| `WindowY` | `double?` | `null` |  |

### Properties [#properties]

#### `ComponentName` [#componentname]

```csharp
public string? ComponentName  // get, init
```

#### `EmbedAutoEnabledVsCode` [#embedautoenabledvscode]

```csharp
public bool EmbedAutoEnabledVsCode  // get, init
```

#### `EmbedHostPid` [#embedhostpid]

```csharp
public int? EmbedHostPid  // get, init
```

#### `EmbedRequested` [#embedrequested]

```csharp
public bool EmbedRequested  // get, init
```

#### `EmbedStyle` [#embedstyle]

```csharp
public WindowEmbedStyle EmbedStyle  // get, init
```

#### `EmbedValidationError` [#embedvalidationerror]

```csharp
public string? EmbedValidationError  // get, init
```

#### `Fps` [#fps]

```csharp
public int Fps  // get, init
```

#### `ListOutputPath` [#listoutputpath]

```csharp
public string? ListOutputPath  // get, init
```

#### `LogLevel` [#loglevel]

```csharp
public string? LogLevel  // get, init
```

#### `LogsCapacityMb` [#logscapacitymb]

```csharp
public int? LogsCapacityMb  // get, init
```

#### `LogsDisabled` [#logsdisabled]

```csharp
public bool LogsDisabled  // get, init
```

#### `McpPort` [#mcpport]

```csharp
public int? McpPort  // get, init
```

#### `PreviewAndDevtoolsConflict` [#previewanddevtoolsconflict]

```csharp
public bool PreviewAndDevtoolsConflict  // get, init
```

#### `ProjectIdentifier` [#projectidentifier]

```csharp
public string? ProjectIdentifier  // get, init
```

#### `ScreenshotOutputPath` [#screenshotoutputpath]

```csharp
public string? ScreenshotOutputPath  // get, init
```

#### `Subverb` [#subverb]

```csharp
public DevtoolsSubverb? Subverb  // get, init
```

#### `Transport` [#transport]

```csharp
public McpTransport Transport  // get, init
```

#### `UsedDeprecatedPreview` [#useddeprecatedpreview]

```csharp
public bool UsedDeprecatedPreview  // get, init
```

#### `VsCodeMode` [#vscodemode]

```csharp
public bool VsCodeMode  // get, init
```

#### `WindowHeight` [#windowheight]

```csharp
public double? WindowHeight  // get, init
```

#### `WindowWidth` [#windowwidth]

```csharp
public double? WindowWidth  // get, init
```

#### `WindowX` [#windowx]

```csharp
public double? WindowX  // get, init
```

#### `WindowY` [#windowy]

```csharp
public double? WindowY  // get, init
```

## `DevtoolsSubverb` [#devtoolssubverb]

```csharp
public enum DevtoolsSubverb
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `Run` | `0` |  |
| `List` | `1` |  |
| `Screenshot` | `2` |  |
| `Tree` | `3` |  |
| `App` | `4` |  |

## `IReactorDevtoolsHost` [#ireactordevtoolshost]

```csharp
public interface IReactorDevtoolsHost
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Methods [#methods]

#### `BuildDevtoolsMenu` [#builddevtoolsmenu]

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

#### `TryHandleCommandLine` [#tryhandlecommandline]

```csharp
public bool TryHandleCommandLine(ReactorDevtoolsBootRequest request)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `request` | [`ReactorDevtoolsBootRequest`](#reactordevtoolsbootrequest) |  |

## `McpTransport` [#mcptransport]

Which MCP transport the devtools surface uses.

```csharp
public enum McpTransport
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Values [#values_1]

| Name | Value | Description |
| --- | --- | --- |
| `Http` | `0` |  |
| `Stdio` | `1` |  |

## `ReactorDevtoolsBootRequest` [#reactordevtoolsbootrequest]

```csharp
public sealed record ReactorDevtoolsBootRequest(DevtoolsCliOptions Options, string Title, double? Width, double? Height, bool FullScreen, Type? HostRoot, Func<Component>? HostRootFactory, Func<RenderContext, Element>? RootRenderFunc, Action<ReactorHost>? Configure)
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Constructors [#constructors_1]

#### `ReactorDevtoolsBootRequest` [#reactordevtoolsbootrequest_1]

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

### Properties [#properties_1]

#### `Configure` [#configure]

```csharp
public Action<ReactorHost>? Configure  // get, init
```

#### `FullScreen` [#fullscreen]

```csharp
public bool FullScreen  // get, init
```

#### `Height` [#height]

```csharp
public double? Height  // get, init
```

#### `HostRoot` [#hostroot]

```csharp
public Type? HostRoot  // get, init
```

#### `HostRootFactory` [#hostrootfactory]

```csharp
public Func<Component>? HostRootFactory  // get, init
```

#### `Options` [#options]

```csharp
public DevtoolsCliOptions Options  // get, init
```

#### `RootRenderFunc` [#rootrenderfunc]

```csharp
public Func<RenderContext, Element>? RootRenderFunc  // get, init
```

#### `Title` [#title]

```csharp
public string Title  // get, init
```

#### `Width` [#width]

```csharp
public double? Width  // get, init
```

## `ReactorDevtoolsBootstrap` [#reactordevtoolsbootstrap]

```csharp
public static class ReactorDevtoolsBootstrap
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Methods [#methods_1]

#### `Register` [#register]

```csharp
public static void Register(IReactorDevtoolsHost host)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `host` | [`IReactorDevtoolsHost`](#ireactordevtoolshost) |  |
