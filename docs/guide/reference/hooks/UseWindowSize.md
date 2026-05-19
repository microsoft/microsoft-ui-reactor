# UseWindowSize

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseWindowSize`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Summary

Parameterless overload — resolves the current host's window via the
active host's back-pointer and re-renders on resize. Returns
<c>(0, 0)</c> when called outside a window (e.g. tray-flyout content);
no implicit fallback to <c>PrimaryWindow</c>. (spec 036 §5.2 / §7.1)


