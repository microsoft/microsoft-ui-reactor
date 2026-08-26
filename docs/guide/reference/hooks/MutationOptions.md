# MutationOptions

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.MutationOptions`2`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Callbacks and side-effects for the `UseMutation` hook.
All callbacks are optional; all run on the dispatcher thread except [OnOptimistic](MutationOptions.md)
which runs synchronously on the caller of [RunAsync](RunAsync.md) ([guide](../../hooks.md))
— the typical case is a render-thread click handler, so the optimistic update lands in the
very next frame without a dispatcher hop.

## Discussion

<para>**InvalidateKeys.** On success, each key is passed to
`QueryCache.Invalidate`. Sibling `UseResource` hooks subscribed to
those keys will observe the invalidation and refetch on their next render.</para><para>Error path: [OnError](MutationOptions.md) fires but [InvalidateKeys](MutationOptions.md) does
**not** — the assumption is the server state didn't change, so the cache is still valid.</para>


