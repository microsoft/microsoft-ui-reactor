# ResourceOptions

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.ResourceOptions`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Tuning for the `UseResource` hook. Defaults mirror
TanStack Query — zero [StaleTime](StaleTime.md) ([guide](../../hooks.md)) (always refetch-on-mount but dedup
concurrent requests), five-minute [CacheTime](CacheTime.md) ([guide](../../hooks.md)).

## Parameters

- **StaleTime** — Freshness interval; null uses zero.
- **CacheTime** — Retention after the last subscriber leaves; null uses five minutes.
- **RetryCount** — Number of retries after a failed fetch.
- **RefetchOnMount** — Whether to fetch on a cache miss.
- **CacheKey** — Overrides the generated cache key; null uses a key derived from deps.
When set, the key identifies the query; deps changes alone do not cancel
an in-flight request or start a new fetch. Include values that select
different data in the key, for example `$"user:{userId}"`.
- **RefetchOnWindowFocus** — Whether to opt into window-focus revalidation.


