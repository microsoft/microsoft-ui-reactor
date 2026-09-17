# UseElementFocusExtensions

`type`  
_cref_: `T:Microsoft.UI.Reactor.Hooks.UseElementFocusExtensions`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Hook for imperative element focus (spec 027 Tier 5). Returns a stable
`ElementRef` (survives re-renders) plus a `RequestFocus` action
that schedules `Input.FocusManager.Focus` on the UI dispatcher. Scheduling
defers focus past the current reconcile pass so callers can safely request focus
from effects or event handlers without racing against layout.


