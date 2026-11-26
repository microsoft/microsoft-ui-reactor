---
title: "PendingScope"
description: "Shared loading-state ref-count consumed by the Pending element and populated by UseResource / UseInfiniteResource hooks inside the scope. When the scope observes any registered resource in the Loading state (not Reloading), the owning Pending element renders its fallback instead of the child subtree."
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)


## Discussion [#discussion]



**Semantics.** Only `Loading` triggers the fallback — spec §10.1. A
`Reloading(previous)` is "we already have something to show" and the subtree
continues to render normally.



**Threading.** All members are thread-safe. [Changed](/microsoft-ui-reactor/main/reference/hooks/Changed) ([guide](/microsoft-ui-reactor/main/guides/hooks)) fires on the
thread that caused the mutation — consumers (typically `Pending`'s re-render
trigger) marshal it to the dispatcher themselves.



**Scope nesting.** Each `Pending` provides a fresh scope to its subtree,
so nested `Pending`s are independent. A hook registers only with its nearest
ancestor scope.
