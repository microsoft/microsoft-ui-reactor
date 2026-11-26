---
title: "UseElementFocusExtensions"
description: "Hook for imperative element focus (spec 027 Tier 5). Returns a stable ElementRef (survives re-renders) plus a RequestFocus action that schedules Input.FocusManager.Focus on the UI dispatcher. Scheduling defers focus past the current reconcile pass so callers can safely request focus from effects or event handlers without racing against layout."
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)
