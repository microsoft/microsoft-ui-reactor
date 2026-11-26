---
title: "UsePersisted"
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)

## Overloads [#overloads]

- [`UsePersisted<T1>(string, T1)`](#usepersistedt1string-t1)
- [`UsePersisted<T1>(string, T1, PersistedScope)`](#usepersistedt1string-t1-persistedscope)

## `UsePersisted<T1>(string, T1)` [#usepersistedt1string-t1]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UsePersisted``1(System.String,``0)`

### Summary [#summary]

Like UseState, but the value survives unmount/remount via an in-memory cache.
On first mount, uses cached value if available, otherwise uses initialValue.
Value is saved to cache on unmount.

### Discussion [#discussion]

Spec 033 §2. The cache is currently process-wide
(`ApplicationPersistedScope.Default`) and bounded by an LRU
policy. The two-arg form is flagged by `REACTOR_PERSIST_001`
(`UsePersistedScopeAnalyzer`); new code should use the three-arg
overload to make the intended scope explicit.

## `UsePersisted<T1>(string, T1, PersistedScope)` [#usepersistedt1string-t1-persistedscope]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UsePersisted``1(System.String,``0,Microsoft.UI.Reactor.Core.PersistedScope)`

### Summary [#summary_1]

Persisted-state hook with explicit scope (spec 033 §2). Use
`PersistedScope.Window` for state that should be bounded by
the host's lifetime; `PersistedScope.Application` for
process-wide state.

### Discussion [#discussion_1]

`PersistedScope.Window` resolves to the
`ReactorWindow.PersistedScope` of the window whose host
renders this component; content without an owning window (tray flyouts,
`ReactorHostControl`, unit-test contexts) falls back to the process-wide
scope. Two windows of the same component class therefore
hold independent state under `PersistedScope.Window`.
(spec 036 §3.4 / §4.4 — closes spec 033 §7.5.)

## Featured in [#featured-in]

- [Hooks](/microsoft-ui-reactor/main/guides/hooks)
