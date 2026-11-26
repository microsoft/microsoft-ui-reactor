---
title: "UseValidationContext"
description: "Returns the nearest ancestor's ValidationContext, or creates a new one scoped to this component. The created context persists across re-renders."
---

When the context is component-local (no ancestor provided one) it is published to the rendered subtree automatically, so `FormField`, the visualizers, and nested components find it without an explicit `.Provide(ValidationContexts.Current, ctx)`. Writing that call yourself still works and takes precedence.

The component also re-renders when the context changes, which is what makes `ctx.MarkAllTouched()` in a submit handler visible: it mutates the context without touching component state, so nothing else would schedule a repaint.

```csharp
public static ValidationContext UseValidationContext(this RenderContext ctx)
```

An extension method on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext), declared in `ValidationContextHookExtensions`.

**Learn more:** [Forms and Input](/microsoft-ui-reactor/main/controls/forms), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
public static ValidationContext UseValidationContext(this Component component)
```
