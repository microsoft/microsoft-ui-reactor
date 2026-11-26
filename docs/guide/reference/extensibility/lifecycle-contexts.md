# Handler contexts

Contexts passed to an element handler when it mounts, updates or unmounts a control.

**Learn more:** [Extending Reactor with Native Controls](../../extending-reactor-controls.md), [The Control Reconciler Protocol](../../control-reconciler-protocol.md)

## `MountContext`

Spec 047 §4 / §14 Phase 1 (1.6) — mount-time context passed to [`IElementHandler.Mount`](IElementHandler.md#mount).

**UI-thread guarantee (Q14):** handlers run on the reconciler's UI dispatcher. Mount bodies may freely access control state without synchronization. Calls from other threads throw in Debug builds today; Phase 1 plan is to measure first before tightening to unconditional throw. See `docs/specs/047/phase1-results/q14-dispatcher-affinity.md`.

**Allocation contract:** [`MountContext`](#mountcontext) is a `readonly ref struct`, so it cannot escape the call stack and never allocates on its own. Hot path (Mount body) pays only the dictionary lookup + interface call already incurred by the V1HandlerRegistry dispatch.

**Sample skeleton** (per spec §4 ToggleSwitchHandler example):

```csharp
public sealed class ToggleSwitchHandler : IElementHandler<ToggleSwitchElement, ToggleSwitch>
{
    public ToggleSwitch Mount(MountContext ctx, ToggleSwitchElement el)
    {
        var ctrl = ctx.RentControl<ToggleSwitch>();
        ctrl.IsOn = el.IsOn;
        ctrl.OnContent = el.OnContent;
        var bind = ctx.BindFor(ctrl, el);
        bind.OnCustomEvent<RoutedEventArgs>(
            (c, h) => ((ToggleSwitch)c).Toggled += (s, e) => h(s, e),
            (c, h) => { /* trampoline lives for the control's lifetime */ },
            (e, args) => e.OnIsOnChanged?.Invoke(((ToggleSwitch)args.OriginalSource).IsOn));
        return ctrl;
    }
    public void Update(UpdateContext ctx, ToggleSwitchElement oldEl, ToggleSwitchElement newEl, ToggleSwitch ctrl)
    {
        if (oldEl.IsOn != newEl.IsOn)
            ctx.BindFor(ctrl, newEl).WriteSuppressed(() => ctrl.IsOn = newEl.IsOn);
    }
}
```

```csharp
public readonly struct MountContext
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

> **Obsolete.** Types with embedded references are not supported in this version of your compiler.

### Properties

#### `Reconciler`

The owning reconciler. Provided as an escape hatch for handlers that need to forward a child mount through some non-strategy path.

```csharp
public Reconciler Reconciler  // get
```

#### `RequestRerender`

The rerender callback for the owning component subtree.

```csharp
public Action RequestRerender  // get
```

### Methods

#### `AddRawRoutedHandler`

Escape hatch (Q11) — attach a raw routed handler directly to the control. Handlers should prefer the typed `On*` family on [`ReactorBinding<TElement>`](ReactorBinding-1.md); use this only for events the binding doesn't cover (e.g. `UIElement.PreviewKeyDown` on a non-FrameworkElement, or app-side custom routed events).

```csharp
public void AddRawRoutedHandler(UIElement target, RoutedEvent re, Delegate h, bool handledEventsToo)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | `UIElement` |  |
| `re` | `RoutedEvent` |  |
| `h` | `Delegate` |  |
| `handledEventsToo` | `bool` |  |

#### `ApplySetters`

The [`Reconciler.ApplySetters`](Reconciler.md#applysetters) helper; provided on the context for symmetry with handler-authored mount bodies.

```csharp
public void ApplySetters<T>(Action<T>[] setters, T control) where T : class
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `setters` | `Action<T>[]` |  |
| `control` | `T` |  |

#### `BindFor`

Construct a per-binding event helper for the given control + element. The returned binding closes over the control identity, not the element — handlers re-fetch the live element via `ReactorState.Element` on each event fire, so the same binding survives element re-renders.

```csharp
public ReactorBinding<TElement> BindFor<TElement>(FrameworkElement control, TElement element) where TElement : Element
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `control` | `FrameworkElement` |  |
| `element` | `TElement` |  |

#### `MountChild`

Mount a child element through the reconciler. The returned [`UIElement`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement) is whatever the reconciler decided to mount (possibly null for `EmptyElement`).

```csharp
public UIElement? MountChild(Element child)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element`](../elements/Element.md) |  |

#### `PushContext`

Push a typed context value for the duration of the returned [`IDisposable`](https://learn.microsoft.com/dotnet/api/system.idisposable). Used by handlers that mount children which must see an author-supplied context.

```csharp
public IDisposable PushContext<T>(Context<T> context, T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `context` | [`Context<T>`](../components/Context.md) |  |
| `value` | `T` |  |

#### `PushStaggerScope`

Push a stagger scope; children mounted inside the scope consume stagger indices for their enter transitions.

```csharp
public IDisposable PushStaggerScope(TimeSpan delay)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `delay` | `TimeSpan` |  |

#### `ReconcileChild`

Reconcile a secondary element slot (mount / update-in-place / unmount) against an already-mounted control, preserving descendant component state across re-renders. `existing` is the control currently in the slot (or `null`). Returns the control to assign back into the slot. Public counterpart to the engine-internal child reconcile, so external wrappers (not just built-in descriptors) can host stateful secondary element slots — see `[WrapElementSlot]`.

```csharp
public UIElement? ReconcileChild(Element? oldChild, Element? newChild, UIElement? existing)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `oldChild` | [`Element?`](../elements/Element.md) |  |
| `newChild` | [`Element?`](../elements/Element.md) |  |
| `existing` | `UIElement?` |  |

#### `RentControl`

Rent a control instance from the per-type pool, or allocate a fresh one if the pool is empty / the policy opts out.

```csharp
public T RentControl<T>(PoolPolicy<T>? policy = null, Func<T>? factory = null) where T : class, new()
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `policy` | [`PoolPolicy<T>?`](PoolPolicy.md) | `null` |  |
| `factory` | `Func<T>?` | `null` |  |

## `UnmountContext`

Spec 047 §4 / §14 Phase 1 (1.6) — unmount-time context. Handlers use [`UnmountContext.ReturnControl`](#returncontrol) to participate in the pool reset contract; the engine takes care of detaching from the parent tree.

```csharp
public readonly struct UnmountContext
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

> **Obsolete.** Types with embedded references are not supported in this version of your compiler.

### Properties

#### `Reconciler`

```csharp
public Reconciler Reconciler  // get
```

#### `RequestRerender`

```csharp
public Action RequestRerender  // get
```

### Methods

#### `ReturnControl`

Return a control to its per-type pool via the engine's reset contract. Safe to call twice — the pool dedupes by stack cap.

```csharp
public void ReturnControl<T>(T control, PoolPolicy<T>? policy = null) where T : class
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `control` | `T` |  |  |
| `policy` | [`PoolPolicy<T>?`](PoolPolicy.md) | `null` |  |

## `UpdateContext`

Spec 047 §4 / §14 Phase 1 (1.6) — update-time context passed to [`IElementHandler.Update`](IElementHandler.md#update).

Same shape as [`MountContext`](#mountcontext) minus `RentControl` — updates run against an existing control, so allocation is forbidden on this path. UI-thread guarantee applies (see [`MountContext`](#mountcontext)).

```csharp
public readonly struct UpdateContext
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

> **Obsolete.** Types with embedded references are not supported in this version of your compiler.

### Properties

#### `Reconciler`

```csharp
public Reconciler Reconciler  // get
```

#### `RequestRerender`

```csharp
public Action RequestRerender  // get
```

### Methods

#### `AddRawRoutedHandler`

```csharp
public void AddRawRoutedHandler(UIElement target, RoutedEvent re, Delegate h, bool handledEventsToo)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | `UIElement` |  |
| `re` | `RoutedEvent` |  |
| `h` | `Delegate` |  |
| `handledEventsToo` | `bool` |  |

#### `ApplySetters`

```csharp
public void ApplySetters<T>(Action<T>[] setters, T control) where T : class
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `setters` | `Action<T>[]` |  |
| `control` | `T` |  |

#### `BindFor`

```csharp
public ReactorBinding<TElement> BindFor<TElement>(FrameworkElement control, TElement element) where TElement : Element
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `control` | `FrameworkElement` |  |
| `element` | `TElement` |  |

#### `MountChild`

```csharp
public UIElement? MountChild(Element child)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element`](../elements/Element.md) |  |

#### `PushContext`

```csharp
public IDisposable PushContext<T>(Context<T> context, T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `context` | [`Context<T>`](../components/Context.md) |  |
| `value` | `T` |  |

#### `PushStaggerScope`

```csharp
public IDisposable PushStaggerScope(TimeSpan delay)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `delay` | `TimeSpan` |  |

#### `ReconcileChild`

Reconcile a secondary element slot (mount / update-in-place / unmount), preserving descendant component state across re-renders. See [`MountContext.ReconcileChild`](#reconcilechild).

```csharp
public UIElement? ReconcileChild(Element? oldChild, Element? newChild, UIElement? existing)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `oldChild` | [`Element?`](../elements/Element.md) |  |
| `newChild` | [`Element?`](../elements/Element.md) |  |
| `existing` | `UIElement?` |  |

## `V1UnmountDisposition`

Spec 047 §14 Phase 3 completion — controls the engine's pool-return / child-traversal decision after a V1 handler's `Unmount` body runs. Returned from `IV1HandlerEntry.Unmount`; the standard hand-coded + descriptor handlers always return [`V1UnmountDisposition.CollectSelf`](#values), matching the pre-Phase-3-completion behavior. Decorator-style handlers ([`IDecoratorElementHandler<TElement>`](IDecoratorElementHandler.md)) return the other dispositions to opt out of pool return / let the engine continue the default unmount traversal into the wrapped child.

```csharp
public enum V1UnmountDisposition
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `CollectSelf` | `0` | Default for standard handlers. The handler created the control via `RentControl`; the engine pools the control (if poolable) and stops the traversal — children were already torn down by the handler. |
| `ContinueDefaultTraversal` | `1` | The handler's returned control is owned by another element (e.g., a decorator's wrapped `Target` child element). The engine must NOT pool the control and MUST continue the default unmount traversal as if no V1 handler had claimed it, so the inner element's own teardown runs. |
| `SkipPool` | `2` | The handler manages teardown itself; the control is a placeholder/wrapper that should NOT be pooled and whose children were already torn down by the handler. Engine returns immediately without pool collection or traversal. |
