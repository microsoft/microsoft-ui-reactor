# PoolPolicy\<TControl\>

Spec 047 §13 Q18 / §14 Phase 1 (1.5) — author-facing pool policy.

External and built-in V1 handlers attach a `PoolPolicy<TControl>` to their `RentControl` / `ReturnControl` calls to opt out of pooling (e.g. for controls with persistent native resources like MediaPlayerElement, MapControl, SwapChainPanel) or to layer an author-defined reset action on top of the engine's default contract.

Pool key is `typeof(TControl)` only (Q18). Finer keys (e.g. by style hash) are a Phase 3+ addition.

Provisional surface — see [`ReactorBinding`](ReactorBinding.md) for the `REACTOR_V1_PREVIEW` diagnostic id.

```csharp
public sealed class PoolPolicy<TControl> where TControl : class
```

Namespace: `Microsoft.UI.Reactor.Core`.

**Learn more:** [Element Pool](../../element-pool.md)

## Constructors

### `PoolPolicy`

```csharp
public PoolPolicy()
```

## Properties

### `IsPoolable`

When false, `RentControl` always allocates a fresh instance and `ReturnControl` is a no-op (the control is dropped on the floor for the GC to collect). Default true.

```csharp
public bool IsPoolable  // get, init
```

### `Reset`

Optional extra reset action invoked AFTER the engine's default contract clears (ControlEventState, ModifierEventHandlerState, ReactorAttached.StateProperty Tag, Reactor-set DataContext). Use for control-specific transient state the engine can't know about (e.g. ScrollViewer.ChangeView animation queue).

```csharp
public Action<TControl>? Reset  // get, init
```
