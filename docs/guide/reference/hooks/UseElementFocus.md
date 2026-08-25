# UseElementFocus

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseElementFocusExtensions.UseElementFocus(Microsoft.UI.Reactor.Core.Component,Microsoft.UI.Xaml.FocusState)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseElementFocus(Component, FocusState)`](#useelementfocuscomponent-focusstate)
- [`UseElementFocus(RenderContext, FocusState)`](#useelementfocusrendercontext-focusstate)

## `UseElementFocus(Component, FocusState)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseElementFocusExtensions.UseElementFocus(Microsoft.UI.Reactor.Core.Component,Microsoft.UI.Xaml.FocusState)`

### Summary

Component-extension overload of [UseElementFocus](UseElementFocus.md#useelementfocusrendercontext-focusstate).
Equivalent to calling the `RenderContext`-extension form against
<c>component.Context</c>; see that overload for the full contract.

### Parameters

- **component** — The component whose render context owns the hook slot.
- **state** — The focus state used by the imperative request.

## `UseElementFocus(RenderContext, FocusState)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseElementFocusExtensions.UseElementFocus(Microsoft.UI.Reactor.Core.RenderContext,Microsoft.UI.Xaml.FocusState)`

### Summary

Creates (or retrieves) a component-scoped `ElementRef` and pairs it
with a <c>RequestFocus</c> action. Bind the ref to an element via <c>.Ref(ref)</c>.
Calling <c>RequestFocus</c> schedules `Focus` on the UI
dispatcher; if the ref's target has not mounted yet the call is a no-op.

### Examples

var (inputRef, requestFocus) = ctx.UseElementFocus();
ctx.UseEffect(() => requestFocus(), Array.Empty<object>()); // focus on first render
return TextBox(value, setValue).Ref(inputRef);


