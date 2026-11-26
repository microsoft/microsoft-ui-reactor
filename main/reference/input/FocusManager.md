---
title: "FocusManager"
seo:
  description: "Imperative focus helpers (spec 027 Tier 5). These operate on ElementRef refs obtained via the UseElementFocus hook or an explicit .Ref(...) modifier; use them when declarative focus (.Focus(...) form helpers, UIElement.IsTabStop) is not enough."
---

Imperative focus helpers (spec 027 Tier 5). These operate on [`ElementRef`](/microsoft-ui-reactor/main/reference/input/element-refs#elementref) refs obtained via the `UseElementFocus` hook or an explicit `.Ref(...)` modifier; use them when declarative focus (`.Focus(...)` form helpers, [`UIElement.IsTabStop`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.istabstop)) is not enough.

```csharp
public static class FocusManager
```

Namespace: `Microsoft.UI.Reactor.Input`.

**Learn more:** [Input and gestures](/microsoft-ui-reactor/main/guides/input-and-gestures)

## Methods [#methods]

### `Focus` [#focus]

Synchronously focus the referenced element. Returns `false` when the ref has no mounted target, the target cannot receive focus, or WinUI rejects the focus request.

```csharp
public static bool Focus(ElementRef target, FocusState state = (FocusState)3)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `target` | [`ElementRef`](/microsoft-ui-reactor/main/reference/input/element-refs#elementref) |  |  |
| `state` | `FocusState` | `(FocusState)3` |  |

### `FocusAsync` [#focusasync]

Asynchronously focus the referenced element using WinUI's FocusManager.TryFocusAsync. Use it when the caller needs confirmation that the focus change succeeded. Call it on the target control's UI thread: it does not dispatch, so a background caller must marshal to that thread first (for example through the control's DispatcherQueue).

```csharp
public static Task<bool> FocusAsync(ElementRef target, FocusState state = (FocusState)3)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `target` | [`ElementRef`](/microsoft-ui-reactor/main/reference/input/element-refs#elementref) |  |  |
| `state` | `FocusState` | `(FocusState)3` |  |
