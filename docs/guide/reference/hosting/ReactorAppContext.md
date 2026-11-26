# ReactorAppContext

The argument to [`ReactorApp.Run`](ReactorApp.md#runactionreactorappcontext)'s startup callback. Carries the launch activation; all surface-management operations (open / find a window or tray icon, register events) go directly through the static [`ReactorApp`](ReactorApp.md) API — call sites mix freely. (spec 036 §4.3)

```csharp
public sealed class ReactorAppContext
```

Namespace: `Microsoft.UI.Reactor`.

**Learn more:** [Windows](../../windows.md)

## Remarks

Keep the context small on purpose: the static [`ReactorApp`](ReactorApp.md) surface is the single source of truth for windows / tray icons / shutdown policy, and remains valid before `Run` enters this callback and after it returns. Doubling the API on the context just doubled the discovery cost without adding capability.

## Properties

### `LaunchActivation`

How the process was launched. See [`LaunchActivation`](launch.md#launchactivation).

```csharp
public LaunchActivation LaunchActivation  // get
```
