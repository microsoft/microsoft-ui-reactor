---
title: "Render errors"
description: "Types that describe an error thrown while a component renders."
---

**Learn more:** [Rules of Reactor](/microsoft-ui-reactor/main/get-started/rules-of-reactor), [Advanced Patterns](/microsoft-ui-reactor/main/guides/advanced)

## `HookOrderException` [#hookorderexception]

Thrown when a component's hook call sequence diverges from the previous render's sequence — either a hook is called at a different index, or the hook type at a given index doesn't match what was previously declared.

In normal operation this is a programming bug (hooks must be called in the same order every render). During Hot Reload, however, an edit that reorders or changes hook types is the \*expected\* outcome of the user editing their code — so the host catches this exception, runs cleanups on the surviving RenderContext, resets its hook state, and re-renders. State is lost (we cannot reliably re-key surviving hook values to a shape that may have changed), but the app keeps running rather than hard-failing the dev loop.

Derives from [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) so existing \`catch (InvalidOperationException)\` handlers around component render continue to work; the host's render catch sniffs the more specific type to gate the hot-reload recovery path.

```csharp
public sealed class HookOrderException : InvalidOperationException
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception).

### Constructors [#constructors]

#### `HookOrderException` [#hookorderexception_1]

```csharp
public HookOrderException(string message)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `message` | `string` |  |

## `RenderError` [#rendererror]

A render-time exception Reactor caught, handed to a [`RenderErrorHandler`](#rendererrorhandler).

```csharp
public sealed class RenderError
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Properties [#properties]

#### `ComponentName` [#componentname]

The failing component's type name, when known.

```csharp
public string? ComponentName  // get
```

#### `Exception` [#exception]

The exception Reactor caught.

```csharp
public Exception Exception  // get
```

#### `IsHostLevel` [#ishostlevel]

Where the failure sits. For a displayable error: `true` when the returned element replaces the host's whole content (root render, reconcile, root effects); `false` when it replaces only the failing component's slot in the tree. For [`RenderErrorSource.Cleanup`](#values), where no element is shown: `true` for a cleanup registered by the host's root component, `false` for one registered by a child component.

```csharp
public bool IsHostLevel  // get
```

#### `IsPropagationRequested` [#ispropagationrequested]

Whether [`RenderError.Propagate`](#propagate) has been called.

```csharp
public bool IsPropagationRequested  // get
```

#### `Source` [#source]

Which part of the render pass threw.

```csharp
public RenderErrorSource Source  // get
```

### Methods [#methods]

#### `Propagate` [#propagate]

Show no fallback and route the exception to the app's unhandled-exception path instead: [`ReactorApp.AppLogger`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#applogger) and [`ReactorApplication.OnUnhandledException`](/microsoft-ui-reactor/main/reference/hosting/ReactorApplication#onunhandledexception). The handler's return value is ignored. Has no effect if the handler throws afterwards.

```csharp
public void Propagate()
```

**Remarks**

When [`ReactorApplication.OnUnhandledException`](/microsoft-ui-reactor/main/reference/hosting/ReactorApplication#onunhandledexception) returns `true`, nothing is shown where the failure happened and the app keeps running. Otherwise the exception is rethrown (stack preserved) out of whatever ran the render:

- Re-renders, and every render of a [`ReactorHostControl`](/microsoft-ui-reactor/main/reference/hosting/ReactorHostControl), run from a `DispatcherQueue` callback. WinUI ends the process for an exception that escapes one, without raising `Application.UnhandledException` or `AppDomain.UnhandledException` (microsoft/microsoft-ui-xaml#8940). The [`ReactorApplication.OnUnhandledException`](/microsoft-ui-reactor/main/reference/hosting/ReactorApplication#onunhandledexception) call Reactor makes first is therefore the only managed hook the app gets, and returning `false` (or not setting it) ends the process.
- The first render of a [`ReactorHost`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost) runs synchronously inside `Mount` / `ReactorApp.OpenWindow`, so there the exception is thrown to that caller.

[`ReactorApplication.OnUnhandledException`](/microsoft-ui-reactor/main/reference/hosting/ReactorApplication#onunhandledexception) is static and is called even when the app's `Application` is not a [`ReactorApplication`](/microsoft-ui-reactor/main/reference/hosting/ReactorApplication) — for example a XAML app that hosts Reactor content in [`ReactorHostControl`](/microsoft-ui-reactor/main/reference/hosting/ReactorHostControl). For such an app it is the only way to observe a propagated render error.

## `RenderErrorHandler` [#rendererrorhandler]

Replaces Reactor's built-in render-error fallback. Called on the UI thread with the exception Reactor caught.

```csharp
public delegate Element? RenderErrorHandler(RenderError error)
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Remarks [#remarks]

Set it app-wide with [`ReactorApp.DefaultRenderErrorHandler`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#defaultrendererrorhandler), per window with [`WindowSpec.RenderErrorHandler`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#rendererrorhandler), or per host with [`ReactorHost.RenderErrorHandler`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost#rendererrorhandler) / [`ReactorHostControl.RenderErrorHandler`](/microsoft-ui-reactor/main/reference/hosting/ReactorHostControl#rendererrorhandler). A host's own handler wins; otherwise the app-wide default is used. Exceptions caught by an `ErrorBoundary` are handled by that boundary and never reach this handler.

Failure is closed, not open: if the handler throws, or the element it returns throws while rendering, Reactor shows a neutral "Something went wrong." message without exception details, and does not call the handler again for that failure.

The handler can run many times for the same fault. Reactor does not latch a failing component: every later render retries it (that retry is how recovery works), and if it throws again the handler is called again — once per re-render of a failing child, and once per host render for a host-level failure. De-duplicate side effects such as telemetry or logging, for example by remembering the exceptions already recorded.

## `RenderErrorSource` [#rendererrorsource]

Where a render error handled by a [`RenderErrorHandler`](#rendererrorhandler) came from.

```csharp
public enum RenderErrorSource
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `RootRender` | `0` | The host's root component `Render()` or root render function threw. |
| `ComponentRender` | `1` | A child component's `Render()` threw (in-tree placeholder). |
| `Reconcile` | `2` | The commit phase threw: reconciling the element tree into native controls (mount/update/unmount), installing the result in the host, or a host callback run after the render pass such as `OnRenderComplete`. |
| `Effects` | `3` | An effect body, or an effect cleanup run while flushing effects, threw. |
| `Cleanup` | `4` | An effect cleanup threw while the host was being disposed (window closed, host control disposed). Nothing can be displayed at that point, so the handler's return value is ignored; it is a report-only notification. Remaining cleanups still run. |
