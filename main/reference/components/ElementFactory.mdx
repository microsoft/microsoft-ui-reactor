---
title: "ElementFactory<T>"
description: "Bridges WinUI's ItemsRepeater/IElementFactory to Reactor's Reconciler. GetElement calls the view builder then mounts; RecycleElement unmounts."
sidebar:
  label: "ElementFactory"
---

```csharp
public sealed class ElementFactory<T> : IElementFactory
```

Namespace: `Microsoft.UI.Reactor.Core`.

## Remarks [#remarks]

Spec 042 Phase 1: [`ElementFactory._mountedElements`](/microsoft-ui-reactor/main/reference/components/ElementFactory) is keyed by the stable identity string from `ReactorRow`, not by realized index. Insert-at-0 used to shift every entry's effective index by one — that broke [`ElementFactory.RefreshRealizedItems`](/microsoft-ui-reactor/main/reference/components/ElementFactory)'s lookup contract because the dictionary's int keys no longer matched the repeater's new positions. Keying by string makes the mapping reorder-stable.

## Constructors [#constructors]

### `ElementFactory` [#elementfactory]

```csharp
public ElementFactory(IReadOnlyList<T> items, Func<T, int, Element> viewBuilder, Reconciler reconciler, Action requestRerender, ElementPool? pool = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | `IReadOnlyList<T>` |  |  |
| `viewBuilder` | `Func<T, int, Element>` |  |  |
| `reconciler` | [`Reconciler`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler) |  |  |
| `requestRerender` | `Action` |  |  |
| `pool` | [`ElementPool?`](/microsoft-ui-reactor/main/reference/extensibility/ElementPool) | `null` |  |

## Methods [#methods]

### `GetElement` [#getelement]

```csharp
public UIElement GetElement(ElementFactoryGetArgs args)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `args` | `ElementFactoryGetArgs` |  |

### `RecycleElement` [#recycleelement]

```csharp
public void RecycleElement(ElementFactoryRecycleArgs args)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `args` | `ElementFactoryRecycleArgs` |  |
