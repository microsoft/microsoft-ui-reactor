# UseChildValidationContext

Creates a child validation context independent from any parent. Returns both the child context and the parent (if one exists). The child collects its own messages; use the parent reference to bubble manually.

```csharp
public static (ValidationContext Child, ValidationContext? Parent) UseChildValidationContext(this RenderContext ctx)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `ValidationContextHookExtensions`.

**Learn more:** [Forms and Input](../../forms.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
public static (ValidationContext Child, ValidationContext? Parent) UseChildValidationContext(this Component component)
```
