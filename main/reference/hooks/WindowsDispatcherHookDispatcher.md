---
title: "WindowsDispatcherHookDispatcher"
seo:
  description: "Default IHookDispatcher (guide) backed by DispatcherQueue.GetForCurrentThread(). Falls back to inline invocation when called outside a WinUI dispatcher (unit tests)."
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)

## Summary [#summary]

Default [IHookDispatcher](/microsoft-ui-reactor/main/reference/hooks/IHookDispatcher) ([guide](/microsoft-ui-reactor/main/guides/hooks)) backed by `DispatcherQueue.GetForCurrentThread()`.
Falls back to inline invocation when called outside a WinUI dispatcher (unit tests).
