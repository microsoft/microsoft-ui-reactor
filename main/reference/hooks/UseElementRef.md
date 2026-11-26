---
title: "UseElementRef"
---

> **Learn more:** [Hooks](/microsoft-ui-reactor/main/guides/hooks), [Effects](/microsoft-ui-reactor/main/guides/effects)

## Overloads [#overloads]

- [`UseElementRef<T>(Component)`](#useelementreftcomponent)
- [`UseElementRef<T>(RenderContext)`](#useelementreftrendercontext)

## `UseElementRef<T>(Component)` [#useelementreftcomponent]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseElementRefExtensions.UseElementRef``1(Microsoft.UI.Reactor.Core.Component)`

### Summary [#summary]

Component-extension overload of [UseElementRef](/microsoft-ui-reactor/main/reference/hooks/UseElementRef#useelementreftrendercontext) ([guide](/microsoft-ui-reactor/main/guides/hooks)).
Equivalent to calling the `RenderContext`-extension form against
`component.Context`.

### Parameters [#parameters]

- **component** — The component whose render context owns the hook slot.

## `UseElementRef<T>(RenderContext)` [#useelementreftrendercontext]

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseElementRefExtensions.UseElementRef``1(Microsoft.UI.Reactor.Core.RenderContext)`

### Summary [#summary_1]

Returns a stable `ElementRef` for the current component scope.
