---
title: "ReactorBinding<TElement>"
description: "Spec 047 §9 / §14 Phase 1 (1.6) — per-binding event helper."
---

One `On<Event>` method per WinUI true-routed input family (pointer, key, tap, focus). Plain CLR events (Toggled, Click, ValueChanged, TextChanged) go through [`ReactorBinding.OnCustomEvent`](#oncustomevent); that path stores its delegate in the per-control event payload box (`ControlEventStateBox`) so pool/recycle correctly clears it.

**Trampoline-refresh pattern** (§3): the handler closure captures [`ReactorBinding._control`](/microsoft-ui-reactor/main/reference/extensibility/ReactorBinding-1) but NOT the element. On each event fire, the live element is re-read via [`Reconciler.GetElementTag`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler#getelementtagframeworkelement); the binding survives re-renders without resubscribing.

**WriteSuppressed** wraps the 1.4 primitive so authors can suppress the next change-event echo on the bound control.

```csharp
public readonly struct ReactorBinding<TElement> where TElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

**Learn more:** [Extending Reactor with Native Controls](/microsoft-ui-reactor/main/how-it-works/extending-reactor-controls), [The Control Reconciler Protocol](/microsoft-ui-reactor/main/how-it-works/control-reconciler-protocol)

## Properties [#properties]

### `Control` [#control]

The control the binding is anchored to.

```csharp
public FrameworkElement Control  // get
```

## Methods [#methods]

### `OnCustomEvent` [#oncustomevent]

Spec 047 §9.2 — subscribe to a plain CLR event (Toggled, Click, ValueChanged, TextChanged …). The handler closure re-fetches the live element via [`Reconciler.GetElementTag`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler#getelementtagframeworkelement) on each fire so it survives re-renders.

The subscription is anchored to the control's lifetime. The engine's pool reset contract clears the per-control event payload on return, so ported handlers are safe to call this once per Mount. Handlers should NOT call it inside [`IElementHandler.Update`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler#update) (subscriptions would multiply across renders).

```csharp
public void OnCustomEvent<TArgs>(Action<FrameworkElement, EventHandler<TArgs>> subscribe, Action<FrameworkElement, EventHandler<TArgs>> unsubscribe, Action<TElement, TArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `subscribe` | `Action<FrameworkElement, EventHandler<TArgs>>` |  |
| `unsubscribe` | `Action<FrameworkElement, EventHandler<TArgs>>` |  |
| `handler` | `Action<TElement, TArgs>` |  |

### `OnDoubleTapped` [#ondoubletapped]

```csharp
public void OnDoubleTapped(Action<TElement, DoubleTappedRoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, DoubleTappedRoutedEventArgs>` |  |

### `OnGotFocus` [#ongotfocus]

```csharp
public void OnGotFocus(Action<TElement, RoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, RoutedEventArgs>` |  |

### `OnHolding` [#onholding]

```csharp
public void OnHolding(Action<TElement, HoldingRoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, HoldingRoutedEventArgs>` |  |

### `OnKeyDown` [#onkeydown]

```csharp
public void OnKeyDown(Action<TElement, KeyRoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, KeyRoutedEventArgs>` |  |

### `OnKeyUp` [#onkeyup]

```csharp
public void OnKeyUp(Action<TElement, KeyRoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, KeyRoutedEventArgs>` |  |

### `OnLostFocus` [#onlostfocus]

```csharp
public void OnLostFocus(Action<TElement, RoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, RoutedEventArgs>` |  |

### `OnPointerCaptureLost` [#onpointercapturelost]

```csharp
public void OnPointerCaptureLost(Action<TElement, PointerRoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, PointerRoutedEventArgs>` |  |

### `OnPointerEntered` [#onpointerentered]

```csharp
public void OnPointerEntered(Action<TElement, PointerRoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, PointerRoutedEventArgs>` |  |

### `OnPointerExited` [#onpointerexited]

```csharp
public void OnPointerExited(Action<TElement, PointerRoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, PointerRoutedEventArgs>` |  |

### `OnPointerMoved` [#onpointermoved]

```csharp
public void OnPointerMoved(Action<TElement, PointerRoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, PointerRoutedEventArgs>` |  |

### `OnPointerPressed` [#onpointerpressed]

```csharp
public void OnPointerPressed(Action<TElement, PointerRoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, PointerRoutedEventArgs>` |  |

### `OnPointerReleased` [#onpointerreleased]

```csharp
public void OnPointerReleased(Action<TElement, PointerRoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, PointerRoutedEventArgs>` |  |

### `OnPointerWheelChanged` [#onpointerwheelchanged]

```csharp
public void OnPointerWheelChanged(Action<TElement, PointerRoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, PointerRoutedEventArgs>` |  |

### `OnRightTapped` [#onrighttapped]

```csharp
public void OnRightTapped(Action<TElement, RightTappedRoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, RightTappedRoutedEventArgs>` |  |

### `OnTapped` [#ontapped]

```csharp
public void OnTapped(Action<TElement, TappedRoutedEventArgs> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<TElement, TappedRoutedEventArgs>` |  |

### `Reference` [#reference]

Registers a reference-property edge for hand-coded handlers. Slot indices are assigned by call order for each `BindFor` instance and offset away from descriptor slots; repeated reconciles of the same handler call order therefore hit the same per-control reference slot.

```csharp
public void Reference<TTarget>(Func<TElement, ElementRef<TTarget>?> get, Action<FrameworkElement, TTarget?> set) where TTarget : FrameworkElement
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `get` | `Func<TElement, ElementRef<TTarget>?>` |  |
| `set` | `Action<FrameworkElement, TTarget?>` |  |

### `ReferenceList` [#referencelist]

Registers a list-valued reference-property edge for hand-coded handlers. The target list is rebuilt from resolved cells in author declaration order.

```csharp
public void ReferenceList<TTarget>(Func<TElement, IReadOnlyList<ElementRef<TTarget>>?> get, Action<FrameworkElement, IReadOnlyList<TTarget>> apply) where TTarget : FrameworkElement
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `get` | `Func<TElement, IReadOnlyList<ElementRef<TTarget>>?>` |  |
| `apply` | `Action<FrameworkElement, IReadOnlyList<TTarget>>` |  |

### `WriteSuppressed` [#writesuppressed]

Per-binding wrapper around the 1.4 primitive ([`ReactorBinding.WriteSuppressed`](/microsoft-ui-reactor/main/reference/extensibility/ReactorBinding#writesuppresseduielement-action)).

```csharp
public void WriteSuppressed(Action mutate)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mutate` | `Action` |  |
