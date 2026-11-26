# Developer tools hosting

Types the developer tools use to start inside an app.

**Learn more:** [Devtools](../../devtools.md)

## `DevtoolsCliOptions`

Parsed result of a `--devtools` / `--preview` invocation. [`DevtoolsCliOptions.Subverb`](#subverb) is `null` when neither flag is present.

```csharp
public sealed record DevtoolsCliOptions(DevtoolsSubverb? Subverb, string? ComponentName, bool VsCodeMode, int Fps, string? ListOutputPath, bool PreviewAndDevtoolsConflict, bool EmbedRequested = false, WindowEmbedStyle EmbedStyle = WindowEmbedStyle.Child, int? EmbedHostPid = null, string? EmbedValidationError = null, bool EmbedAutoEnabledVsCode = false, double? WindowWidth = null, double? WindowHeight = null, double? WindowX = null, double? WindowY = null)
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Constructors

#### `DevtoolsCliOptions`

Parsed result of a `--devtools` / `--preview` invocation. [`DevtoolsCliOptions.Subverb`](#subverb) is `null` when neither flag is present.

```csharp
public DevtoolsCliOptions(DevtoolsSubverb? Subverb, string? ComponentName, bool VsCodeMode, int Fps, string? ListOutputPath, bool PreviewAndDevtoolsConflict, bool EmbedRequested = false, WindowEmbedStyle EmbedStyle = WindowEmbedStyle.Child, int? EmbedHostPid = null, string? EmbedValidationError = null, bool EmbedAutoEnabledVsCode = false, double? WindowWidth = null, double? WindowHeight = null, double? WindowX = null, double? WindowY = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Subverb` | `DevtoolsSubverb?` |  |  |
| `ComponentName` | `string?` |  |  |
| `VsCodeMode` | `bool` |  |  |
| `Fps` | `int` |  |  |
| `ListOutputPath` | `string?` |  |  |
| `PreviewAndDevtoolsConflict` | `bool` |  |  |
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

#### `PreviewAndDevtoolsConflict`

```csharp
public bool PreviewAndDevtoolsConflict  // get, init
```

#### `Subverb`

```csharp
public DevtoolsSubverb? Subverb  // get, init
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

What the in-app preview host was asked to do: [`DevtoolsSubverb.Run`](#values) the preview (the VS Code live preview and the Visual Studio embedded preview launch this), or [`DevtoolsSubverb.List`](#values) the app's components.

```csharp
public enum DevtoolsSubverb
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Run` | `0` |  |
| `List` | `1` |  |

## `IReactorDevtoolsHost`

Implemented by the `Microsoft.UI.Reactor.Devtools` package: the in-app preview host that the VS Code live preview and the Visual Studio embedded preview launch with `--devtools run`.

```csharp
public interface IReactorDevtoolsHost
```

Namespace: `Microsoft.UI.Reactor.Hosting.Devtools`.

### Methods

#### `TryHandleCommandLine`

```csharp
public bool TryHandleCommandLine(ReactorDevtoolsBootRequest request)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `request` | [`ReactorDevtoolsBootRequest`](#reactordevtoolsbootrequest) |  |

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
