# UseWindow

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseWindow`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Resolves the `ReactorWindow` whose host renders this
component — including nested components and virtualized rows realized outside the
host's render pass — or `null` when it has no owning window (tray-flyout content,
`ReactorHostControl` embeds). O(1) field read; no subscription, no re-render
trigger. (spec 036 §7 / §7.1)


