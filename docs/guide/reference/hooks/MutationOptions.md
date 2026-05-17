# MutationOptions

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.MutationOptions`2`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Callbacks and side-effects for the <c>UseMutation</c> hook.
All callbacks are optional; all run on the dispatcher thread except `OnOptimistic`
which runs synchronously on the caller of [RunAsync](RunAsync.md) ([guide](../../hooks.md))
— the typical case is a render-thread click handler, so the optimistic update lands in the
very next frame without a dispatcher hop.

## Discussion

<para><b>InvalidateKeys.</b> On success, each key is passed to
`Invalidate`. Sibling <c>UseResource</c> hooks subscribed to
those keys will observe the invalidation and refetch on their next render.</para><para>Error path: `OnError` fires but `InvalidateKeys` does
<b>not</b> — the assumption is the server state didn't change, so the cache is still valid.</para>


