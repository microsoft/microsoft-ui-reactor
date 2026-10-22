# UseWindowSize

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseWindowSize`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseWindowSize()`](#usewindowsize)
- [`UseWindowSize(Window)`](#usewindowsizewindow)

## `UseWindowSize()`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseWindowSize`

### Summary

Parameterless overload — resolves the window whose host renders this
component and re-renders on resize. Returns
`(0, 0)` when the component has no owning window (tray-flyout content,
`ReactorHostControl` embeds); no implicit fallback to `PrimaryWindow`
or to another open window. (spec 036 §5.2 / §7.1)

## `UseWindowSize(Window)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseWindowSize(Microsoft.UI.Xaml.Window)`

### Summary

Returns (width, height) of the given window and re-renders when the window resizes.


