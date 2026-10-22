# UseBreakpoint

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseBreakpoint(System.Double)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseBreakpoint(double)`](#usebreakpointdouble)
- [`UseBreakpoint(Window, double)`](#usebreakpointwindow-double)

## `UseBreakpoint(double)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseBreakpoint(System.Double)`

### Summary

Window-inferring overload — takes only `minWidth` and
resolves the window whose host renders this component (see the parameterless
[UseWindowSize](UseWindowSize.md#usewindowsize) ([guide](../../hooks.md))). Returns false when the component has no owning
window. (spec 036 §5.2)

## `UseBreakpoint(Window, double)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseBreakpoint(Microsoft.UI.Xaml.Window,System.Double)`

### Summary

Returns true when the given window's width is >= minWidth.
Re-renders when the window resizes across the breakpoint.


