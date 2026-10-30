# CacheKey

`property`  
_cref_: `P:Microsoft.UI.Reactor.Hooks.ResourceOptions.CacheKey`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Overrides the generated cache key; null uses a key derived from deps.
When set, the key identifies the query; deps changes alone do not cancel
an in-flight request or start a new fetch. Include values that select
different data in the key, for example `$"user:{userId}"`.


