---
title: "ResourceOptions"
seo:
  description: "Tuning for the UseResource hook. Defaults mirror TanStack Query — zero StaleTime (guide) (always refetch-on-mount but dedup concurrent requests), five-minute CacheTime (guide)."
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)

## Summary [#summary]

Tuning for the `UseResource` hook. Defaults mirror
TanStack Query — zero [StaleTime](/microsoft-ui-reactor/main/reference/hooks/StaleTime) ([guide](/microsoft-ui-reactor/main/guides/hooks)) (always refetch-on-mount but dedup
concurrent requests), five-minute [CacheTime](/microsoft-ui-reactor/main/reference/hooks/CacheTime) ([guide](/microsoft-ui-reactor/main/guides/hooks)).

## Parameters [#parameters]

- **StaleTime** — Freshness interval; null uses zero.
- **CacheTime** — Retention after the last subscriber leaves; null uses five minutes.
- **RetryCount** — Number of retries after a failed fetch.
- **RefetchOnMount** — Whether to fetch on a cache miss.
- **CacheKey** — Overrides the generated cache key; null uses a key derived from deps.
When set, the key identifies the query; deps changes alone do not cancel
an in-flight request or start a new fetch. Include values that select
different data in the key, for example `$"user:{userId}"`.
- **RefetchOnWindowFocus** — Whether to opt into window-focus revalidation.
