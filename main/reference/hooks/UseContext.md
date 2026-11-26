---
title: "UseContext"
description: "Reads the nearest ancestor's provided value for the given context. Returns the context's DefaultValue if no provider exists in the ancestor chain. Follows hook rules — must be called in the same order every render."
---

```csharp
public T UseContext<T>(Context<T> context)
```

Declared on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext).

**Learn more:** [Context](/microsoft-ui-reactor/main/guides/context), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Parameters [#parameters]

| Name | Type | Description |
| --- | --- | --- |
| `context` | [`Context<T>`](/microsoft-ui-reactor/main/reference/components/Context) |  |

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected T UseContext<T>(Context<T> context)
```
