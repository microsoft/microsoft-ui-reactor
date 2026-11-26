---
title: "UseElementFocus"
seo:
  description: "Creates (or retrieves) a component-scoped ElementRef and pairs it with a RequestFocus action. Bind the ref to an element via .Ref(ref). Calling RequestFocus schedules FocusManager.Focus on the UI dispatcher; if the ref's target has not mounted yet the call is a no-op."
---

Creates (or retrieves) a component-scoped [`ElementRef`](/microsoft-ui-reactor/main/reference/input/element-refs#elementref) and pairs it with a `RequestFocus` action. Bind the ref to an element via `.Ref(ref)`. Calling `RequestFocus` schedules [`FocusManager.Focus`](/microsoft-ui-reactor/main/reference/input/FocusManager#focus) on the UI dispatcher; if the ref's target has not mounted yet the call is a no-op.

```csharp
public static (ElementRef Ref, Action RequestFocus) UseElementFocus(this RenderContext ctx, FocusState state = (FocusState)3)
```

An extension method on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext), declared in `UseElementFocusExtensions`.

**Learn more:** [Input and gestures](/microsoft-ui-reactor/main/guides/input-and-gestures), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Parameters [#parameters]

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `state` | `FocusState` | `(FocusState)3` |  |

## Example [#example]

```csharp
var (inputRef, requestFocus) = ctx.UseElementFocus();
ctx.UseEffect(() => requestFocus(), Array.Empty<object>()); // focus on first render
return TextBox(value, setValue).Ref(inputRef);
```

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
public static (ElementRef Ref, Action RequestFocus) UseElementFocus(this Component component, FocusState state = (FocusState)3)
```
