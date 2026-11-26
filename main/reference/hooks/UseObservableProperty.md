---
title: "UseObservableProperty"
description: "Subscribes to a specific property on an INotifyPropertyChanged source. Re-renders only when that property changes."
---

```csharp
public TProp UseObservableProperty<T, TProp>(T source, Func<T, TProp> selector, string propertyName) where T : INotifyPropertyChanged
```

Declared on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext).

**Learn more:** [Reactivity Model](/microsoft-ui-reactor/main/how-it-works/reactivity-model), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Parameters [#parameters]

| Name | Type | Description |
| --- | --- | --- |
| `source` | `T` |  |
| `selector` | `Func<T, TProp>` |  |
| `propertyName` | `string` |  |

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected TProp UseObservableProperty<T, TProp>(T source, Func<T, TProp> selector, string propertyName) where T : INotifyPropertyChanged
```
