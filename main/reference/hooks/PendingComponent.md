---
title: "PendingComponent"
seo:
  description: "Component backing the Pending element. Hosts a PendingScope (guide), subscribes to its Changed (guide) event, and chooses between Fallback and Child."
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)

## Summary [#summary]

Component backing the `Pending` element. Hosts a [PendingScope](/microsoft-ui-reactor/main/reference/hooks/PendingScope) ([guide](/microsoft-ui-reactor/main/guides/hooks)),
subscribes to its [Changed](/microsoft-ui-reactor/main/reference/hooks/Changed) ([guide](/microsoft-ui-reactor/main/guides/hooks)) event, and chooses between
[Fallback](/microsoft-ui-reactor/main/reference/hooks/PendingProps) and [Child](/microsoft-ui-reactor/main/reference/hooks/PendingProps).

## Discussion [#discussion]

The child subtree is always rendered. When the scope reports any loading resource,
the child's `Visibility` is flipped to `Collapsed` and the fallback is
shown instead. Because the subtree stays mounted, its hooks keep running, their
fetches complete in the background, and the UI swaps to the real content on the
next render frame after the scope reports clean.
