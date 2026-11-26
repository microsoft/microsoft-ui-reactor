---
title: "UseCommandState"
seo:
  description: "Allocates the stable hook shape shared by both UseCommand (guide) overloads: the IsExecuting / IsDebouncing state, the re-entrance guard and debounce-slot refs, and an unmount effect that disposes any live re-enable timer so it cannot fire setIsDebouncing / request a re-render against a torn-down context. These hooks are allocated unconditionally so the hook order is identical regardless of the command's shape."
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)

## Summary [#summary]

Allocates the stable hook shape shared by both [UseCommand](/microsoft-ui-reactor/main/reference/hooks/UseCommand#usecommandcommand) ([guide](/microsoft-ui-reactor/main/guides/hooks)) overloads:
the IsExecuting / IsDebouncing state, the re-entrance guard and debounce-slot refs, and an
unmount effect that disposes any live re-enable timer so it cannot fire
`setIsDebouncing` / request a re-render against a torn-down context. These hooks are
allocated unconditionally so the hook order is identical regardless of the command's shape.
