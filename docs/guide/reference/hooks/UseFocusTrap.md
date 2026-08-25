# UseFocusTrap

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseFocusTrapExtensions.UseFocusTrap(Microsoft.UI.Reactor.Core.Component,System.Boolean)`

> **Learn more:** [Hooks](../../hooks.md), [Effects](../../effects.md)

## Overloads

- [`UseFocusTrap(Component, bool)`](#usefocustrapcomponent-bool)
- [`UseFocusTrap(RenderContext, bool)`](#usefocustraprendercontext-bool)

## `UseFocusTrap(Component, bool)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseFocusTrapExtensions.UseFocusTrap(Microsoft.UI.Reactor.Core.Component,System.Boolean)`

### Summary

Creates a focus trap handle for this component.

## `UseFocusTrap(RenderContext, bool)`

`method`  
_cref_: `M:Microsoft.UI.Reactor.Hooks.UseFocusTrapExtensions.UseFocusTrap(Microsoft.UI.Reactor.Core.RenderContext,System.Boolean)`

### Summary

Creates a focus trap handle that traps keyboard focus within a container
when active. Use with the .FocusTrap() element modifier.

<code>
var trap = UseFocusTrap(isDialogOpen);
return Border(
VStack(
TextBlock("Confirm delete?"),
Button("Cancel", () =&gt; setOpen(false)),
Button("Delete", onDelete)
)
).FocusTrap(trap);
</code>


