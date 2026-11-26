# Descriptor callbacks

Delegates and attributes used by control descriptors.

**Learn more:** [Extending Reactor with Native Controls](../../extending-reactor-controls.md), [The Control Reconciler Protocol](../../control-reconciler-protocol.md)

## `AfterChildrenMountCallback<TElement, TControl>`

Spec 047 §14 Phase 3 prelude (Engine A1) — post-children mount callback signature for [`ControlDescriptor.AfterChildrenMount`](ControlDescriptor.md#afterchildrenmount). Invoked by `V1HandlerAdapter` after the descriptor's children strategy has mounted/bound every child, so a descriptor can wire events that must subscribe strictly after children-add (e.g. `TabView.SelectionChanged`). Takes [`MountContext`](lifecycle-contexts.md#mountcontext) by `in` to preserve the allocation-free ref-struct contract.

```csharp
public delegate void AfterChildrenMountCallback<TElement, TControl>(in MountContext ctx, TElement element, TControl control) where TElement : Element where TControl : FrameworkElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol.Descriptor`.

## `DescriptorUnmountCallback<TControl>`

Teardown callback signature for [`ControlDescriptor.OnUnmount`](ControlDescriptor.md#onunmount). Invoked by [`DescriptorHandler<TElement, TControl>`](DescriptorHandler.md) from the engine's [`IElementHandler.Unmount`](IElementHandler.md#unmount) dispatch, so a descriptor can invalidate control-scoped state it set up during mount — pending deferred writes, one-shot lifecycle subscriptions, disposables. Takes [`UnmountContext`](lifecycle-contexts.md#unmountcontext) by `in` to preserve the allocation-free ref-struct contract.

```csharp
public delegate void DescriptorUnmountCallback<TControl>(in UnmountContext ctx, TControl control) where TControl : FrameworkElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol.Descriptor`.

### Remarks

The engine still runs the pool reset contract afterwards; this hook is for state the reset contract cannot see, such as a `Loaded` handler the descriptor attached itself.

## `NoClearValueAttribute`

Suppresses the REACTOR0050 analyzer for a descriptor field or property whose `OneWay` entries intentionally use [`Optional<T>`](../components/Optional.md) without a dependency-property backing value to clear.

```csharp
public sealed class NoClearValueAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol.Descriptor`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Remarks

Prefer providing the `dp:` argument to `ControlDescriptor.OneWay` whenever the target WinUI property is backed by a dependency property. Use this attribute only for descriptor entries whose unset value should skip the write because there is no dependency property, or because clearing the local value would be semantically wrong for that control.

### Constructors

#### `NoClearValueAttribute`

```csharp
public NoClearValueAttribute()
```

## `OneWayBridgedSetter<TControl, TValue>`

Spec 047 §14 Phase 3-final — set-lambda signature for `OneWayBridgedPropEntry`. Named delegate type (not `Action<...>`) so the [`MountContext`](lifecycle-contexts.md#mountcontext) / [`UpdateContext`](lifecycle-contexts.md#updatecontext) doesn't need to be passed — the entry projects the two pieces a bridge typically needs (the [`Reconciler`](Reconciler.md) and the rerender callback).

```csharp
public delegate void OneWayBridgedSetter<TControl, TValue>(TControl ctrl, TValue value, Reconciler reconciler, Action requestRerender) where TControl : UIElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol.Descriptor`.
