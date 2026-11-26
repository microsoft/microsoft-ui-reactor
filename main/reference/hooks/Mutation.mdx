---
title: "Mutation"
seo:
  description: "Handle returned by the UseMutation hook. Carries the pending/error/last-result state and the RunAsync (guide) entry point."
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)

## Summary [#summary]

Handle returned by the `UseMutation` hook. Carries
the pending/error/last-result state and the [RunAsync](/microsoft-ui-reactor/main/reference/hooks/RunAsync) ([guide](/microsoft-ui-reactor/main/guides/hooks)) entry point.

## Discussion [#discussion]



**Concurrency.** Overlapping [RunAsync](/microsoft-ui-reactor/main/reference/hooks/RunAsync) ([guide](/microsoft-ui-reactor/main/guides/hooks)) calls each get their own
cancellation token; both complete and fire their callbacks in completion order.
[LastResult](/microsoft-ui-reactor/main/reference/hooks/LastResult) ([guide](/microsoft-ui-reactor/main/guides/hooks)) is whichever finishes last. If you want strictly-serialized
mutations, wrap [RunAsync](/microsoft-ui-reactor/main/reference/hooks/RunAsync) ([guide](/microsoft-ui-reactor/main/guides/hooks)) behind your own gate (or disable the trigger
control while [IsPending](/microsoft-ui-reactor/main/reference/hooks/IsPending) ([guide](/microsoft-ui-reactor/main/guides/hooks)) is true).



**Reset.**[Reset](/microsoft-ui-reactor/main/reference/hooks/Reset) ([guide](/microsoft-ui-reactor/main/guides/hooks)) clears [Error](/microsoft-ui-reactor/main/reference/hooks/Error) ([guide](/microsoft-ui-reactor/main/guides/hooks)) and
[LastResult](/microsoft-ui-reactor/main/reference/hooks/LastResult) ([guide](/microsoft-ui-reactor/main/guides/hooks)) but does **not** cancel in-flight work — this is an explicit
choice so a "dismiss the error banner" action doesn't abort the user's retry.
