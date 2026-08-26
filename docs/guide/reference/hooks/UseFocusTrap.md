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

```csharp
// UseFocusTrap is an extension on Component / RenderContext, so it needs
// an explicit receiver — there is no unqualified Component wrapper.
var trap = this.UseFocusTrap(isDialogOpen);
return Border(
VStack(
TextBlock("Confirm delete?"),
Button("Cancel", () => setOpen(false)),
Button("Delete", onDelete)
)
).FocusTrap(trap);
```


