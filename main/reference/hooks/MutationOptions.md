---
title: "MutationOptions"
seo:
  description: "Callbacks and side-effects for the UseMutation hook. All callbacks are optional; all run on the dispatcher thread except OnOptimistic which runs synchronously on the caller of RunAsync (guide) — the typical case is a render-thread click handler, so the optimistic update lands in the very next frame without a dispatcher hop."
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)

## Summary [#summary]

Callbacks and side-effects for the `UseMutation` hook.
All callbacks are optional; all run on the dispatcher thread except [OnOptimistic](/microsoft-ui-reactor/main/reference/hooks/MutationOptions)
which runs synchronously on the caller of [RunAsync](/microsoft-ui-reactor/main/reference/hooks/RunAsync) ([guide](/microsoft-ui-reactor/main/guides/hooks))
— the typical case is a render-thread click handler, so the optimistic update lands in the
very next frame without a dispatcher hop.

## Discussion [#discussion]



**InvalidateKeys.** On success, each key is passed to
`QueryCache.Invalidate`. Sibling `UseResource` hooks subscribed to
those keys will observe the invalidation and refetch on their next render.



Error path: [OnError](/microsoft-ui-reactor/main/reference/hooks/MutationOptions) fires but [InvalidateKeys](/microsoft-ui-reactor/main/reference/hooks/MutationOptions) does
**not** — the assumption is the server state didn't change, so the cache is still valid.
