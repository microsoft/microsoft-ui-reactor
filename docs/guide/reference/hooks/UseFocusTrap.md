# UseFocusTrap

Creates a focus trap handle that traps keyboard focus within a container when active. Use with the .FocusTrap() element modifier.

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

```csharp
public static FocusTrapHandle UseFocusTrap(this RenderContext ctx, bool isActive)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `UseFocusTrapExtensions`.

**Learn more:** [Input and gestures](../../input-and-gestures.md), [Accessibility](../../accessibility.md), [Hooks](../../hooks.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `isActive` | `bool` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
public static FocusTrapHandle UseFocusTrap(this Component component, bool isActive)
```
