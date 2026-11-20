# UseElementRef

Returns a stable [`ElementRef<T>`](../input/element-refs.md#elementreft) for the current component scope.

```csharp
public static ElementRef<T> UseElementRef<T>(this RenderContext ctx) where T : FrameworkElement
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `UseElementRefExtensions`.

**Learn more:** [Input and gestures](../../input-and-gestures.md), [Hooks](../../hooks.md)

## Type parameters

| Name | Description |
| --- | --- |
| `T` | The concrete control type the ref will attach to. |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
public static ElementRef<T> UseElementRef<T>(this Component component) where T : FrameworkElement
```
