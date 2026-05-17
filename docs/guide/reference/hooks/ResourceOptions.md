# ResourceOptions

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.ResourceOptions`

## Summary

Tuning for the <c>UseResource</c> hook. Defaults mirror
TanStack Query — zero `StaleTime` (always refetch-on-mount but dedup
concurrent requests), five-minute `CacheTime`.

