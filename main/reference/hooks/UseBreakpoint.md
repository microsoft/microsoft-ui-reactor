---
title: "UseBreakpoint"
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)

## Overloads [#overloads]

- [`UseBreakpoint(double)`](#usebreakpointdouble)
- [`UseBreakpoint(Window, double)`](#usebreakpointwindow-double)

## `UseBreakpoint(double)` [#usebreakpointdouble]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseBreakpoint(System.Double)`

### Summary [#summary]

Window-inferring overload — takes only `minWidth` and
resolves the window whose host renders this component (see the parameterless
[UseWindowSize](/microsoft-ui-reactor/main/reference/hooks/UseWindowSize) ([guide](/microsoft-ui-reactor/main/guides/hooks))). Returns false when the component has no owning
window. (spec 036 §5.2)

## `UseBreakpoint(Window, double)` [#usebreakpointwindow-double]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Core.RenderContext.UseBreakpoint(Microsoft.UI.Xaml.Window,System.Double)`

### Summary [#summary_1]

Returns true when the given window's width is >= minWidth.
Re-renders when the window resizes across the breakpoint.
