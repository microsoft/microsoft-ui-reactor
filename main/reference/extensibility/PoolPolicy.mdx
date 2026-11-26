---
title: "PoolPolicy<TControl>"
description: "Spec 047 §13 Q18 / §14 Phase 1 (1.5) — author-facing pool policy."
sidebar:
  label: "PoolPolicy"
---

External and built-in V1 handlers attach a `PoolPolicy<TControl>` to their `RentControl` / `ReturnControl` calls to opt out of pooling (e.g. for controls with persistent native resources like MediaPlayerElement, MapControl, SwapChainPanel) or to layer an author-defined reset action on top of the engine's default contract.

Pool key is `typeof(TControl)` only (Q18). Finer keys (e.g. by style hash) are a Phase 3+ addition.

Provisional surface — see [`ReactorBinding`](/microsoft-ui-reactor/main/reference/extensibility/ReactorBinding) for the `REACTOR_V1_PREVIEW` diagnostic id.

```csharp
public sealed class PoolPolicy<TControl> where TControl : class
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Element Pool](/microsoft-ui-reactor/main/how-it-works/element-pool)

## Constructors [#constructors]

### `PoolPolicy` [#poolpolicy]

```csharp
public PoolPolicy()
```

## Properties [#properties]

### `IsPoolable` [#ispoolable]

When false, `RentControl` always allocates a fresh instance and `ReturnControl` is a no-op (the control is dropped on the floor for the GC to collect). Default true.

```csharp
public bool IsPoolable  // get, init
```

### `Reset` [#reset]

Optional extra reset action, invoked last when the control is returned to the pool, after the engine's default reset. That reset clears the element pointer, the current modifier callbacks, echo-suppression and unmount state, and the Reactor-set DataContext. It keeps the attached ReactorState and its per-control ControlEventState, so event trampolines subscribed once for the control's lifetime stay subscribed and dispatch to the next element. Don't resubscribe native events here. Use it for control-specific transient state the engine can't know about (e.g. ScrollViewer.ChangeView animation queue).

```csharp
public Action<TControl>? Reset  // get, init
```
