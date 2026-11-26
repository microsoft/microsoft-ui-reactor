---
title: "ReactorBinding"
---

Spec 047 §14 Phase 1 (1.4) — public author-facing surface for the echo suppression primitive (§13 Q19). Today this delegates to `ChangeEchoSuppressor.BeginSuppress`; Phase 4 swaps the body for per-control tolerance / coercion metadata as part of the §8 cleanup — the signature does not change. The per-binding wrapper `ReactorBinding<TElement>` (per spec §4) lands in 1.6; this static class is just the primitive entry point.

```csharp
public static class ReactorBinding
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Extending Reactor with Native Controls](/microsoft-ui-reactor/main/how-it-works/extending-reactor-controls)

## Methods [#methods]

### `ShouldSuppressEcho` [#shouldsuppressecho]

Read-side counterpart of [`ReactorBinding.WriteSuppressed`](#writesuppresseduielement-action). Call as the first line of a change-event handler: returns `true` when the current fire is the engine-synthesized echo of a prior `WriteSuppressed` write against `target` — **consuming one suppression token** on that counter-based path — so the handler should return early before invoking the user's callback. It also returns `true` *without* consuming a token while inside the non-consuming `ApplySetters` suppression scope. Returns `false` for a genuine user interaction (no outstanding token/scope), for a target that is not a [`FrameworkElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement), or when the target carries no Reactor state.

This is the public primitive the wrapper generator emits for a `[WrapControlled(Deferred = true)]` prop, so a deferred-controlled generated wrapper compiles against Reactor's public surface alone (no `InternalsVisibleTo`) — the read-side gap called out in spec 062 §14. It maps to the counter/scope channel (paired with `WriteSuppressed`), not the value-diff channel.

```csharp
public static bool ShouldSuppressEcho(UIElement target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | `UIElement` |  |

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) | `target` is `null`. |

### `WriteSuppressed` [#writesuppressed]

#### `WriteSuppressed(UIElement, Action)` [#writesuppresseduielement-action]

Runs `mutate` after registering a one-shot suppression token for `target`. The next change-event handler invocation against `target` consumes the token via `ChangeEchoSuppressor.ShouldSuppress` and short-circuits before the user's OnChanged callback fires.

No try/finally — the consumption is event-driven and the counter is harmless if a single token is left at +1 after an exceptional path (it gets consumed by the next change event for that control, which by contract should be one the engine itself just provoked).

```csharp
public static void WriteSuppressed(UIElement target, Action mutate)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | `UIElement` |  |
| `mutate` | `Action` |  |

#### `WriteSuppressed<T>(T, Action<T>)` [#writesuppressedtt-actiont]

Typed overload — saves a cast at the call site for the common case where the handler already has a strongly-typed control reference.

```csharp
public static void WriteSuppressed<T>(T target, Action<T> mutate) where T : UIElement
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | `T` |  |
| `mutate` | `Action<T>` |  |

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) | `target` or `mutate` is `null`. |
