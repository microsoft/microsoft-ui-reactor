---
title: "UseClosingGuard"
description: "Registers a synchronous \"can the window close right now?\" predicate. Multiple guards stack — any returning false cancels the close. Runs on the UI thread; for async confirmation, return false and re-trigger ReactorWindow.Close from the dialog callback. No-op outside a window. (spec 036 §7 / §13.4)"
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)
