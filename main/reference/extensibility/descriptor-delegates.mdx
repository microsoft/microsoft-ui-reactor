---
title: "Descriptor callbacks"
description: "Delegates and attributes used by control descriptors."
---

**Learn more:** [Extending Reactor with Native Controls](/microsoft-ui-reactor/main/how-it-works/extending-reactor-controls), [The Control Reconciler Protocol](/microsoft-ui-reactor/main/how-it-works/control-reconciler-protocol)

## `AfterChildrenMountCallback<TElement, TControl>` [#afterchildrenmountcallbacktelement-tcontrol]

Spec 047 §14 Phase 3 prelude (Engine A1) — post-children mount callback signature for [`ControlDescriptor.AfterChildrenMount`](/microsoft-ui-reactor/main/reference/extensibility/ControlDescriptor#afterchildrenmount). Invoked by `V1HandlerAdapter` after the descriptor's children strategy has mounted/bound every child, so a descriptor can wire events that must subscribe strictly after children-add (e.g. `TabView.SelectionChanged`). Takes [`MountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#mountcontext) by `in` to preserve the allocation-free ref-struct contract.

```csharp
public delegate void AfterChildrenMountCallback<TElement, TControl>(in MountContext ctx, TElement element, TControl control) where TElement : Element where TControl : FrameworkElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol.Descriptor`.

## `DescriptorUnmountCallback<TControl>` [#descriptorunmountcallbacktcontrol]

Teardown callback signature for [`ControlDescriptor.OnUnmount`](/microsoft-ui-reactor/main/reference/extensibility/ControlDescriptor#onunmount). Invoked by [`DescriptorHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/DescriptorHandler) from the engine's [`IElementHandler.Unmount`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler#unmount) dispatch, so a descriptor can invalidate control-scoped state it set up during mount — pending deferred writes, one-shot lifecycle subscriptions, disposables. Takes [`UnmountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#unmountcontext) by `in` to preserve the allocation-free ref-struct contract.

```csharp
public delegate void DescriptorUnmountCallback<TControl>(in UnmountContext ctx, TControl control) where TControl : FrameworkElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol.Descriptor`.

### Remarks [#remarks]

The engine still runs the pool reset contract afterwards; this hook is for state the reset contract cannot see, such as a `Loaded` handler the descriptor attached itself.

## `NoClearValueAttribute` [#noclearvalueattribute]

Suppresses the REACTOR0050 analyzer for a descriptor field or property whose `OneWay` entries intentionally use [`Optional<T>`](/microsoft-ui-reactor/main/reference/components/Optional) without a dependency-property backing value to clear.

```csharp
public sealed class NoClearValueAttribute : Attribute
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol.Descriptor`. Inherits from [`Attribute`](https://learn.microsoft.com/dotnet/api/system.attribute).

### Remarks [#remarks_1]

Prefer providing the `dp:` argument to `ControlDescriptor.OneWay` whenever the target WinUI property is backed by a dependency property. Use this attribute only for descriptor entries whose unset value should skip the write because there is no dependency property, or because clearing the local value would be semantically wrong for that control.

### Constructors [#constructors]

#### `NoClearValueAttribute` [#noclearvalueattribute_1]

```csharp
public NoClearValueAttribute()
```

## `OneWayBridgedSetter<TControl, TValue>` [#onewaybridgedsettertcontrol-tvalue]

Spec 047 §14 Phase 3-final — set-lambda signature for `OneWayBridgedPropEntry`. Named delegate type (not `Action<...>`) so the [`MountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#mountcontext) / [`UpdateContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#updatecontext) doesn't need to be passed — the entry projects the two pieces a bridge typically needs (the [`Reconciler`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler) and the rerender callback).

```csharp
public delegate void OneWayBridgedSetter<TControl, TValue>(TControl ctrl, TValue value, Reconciler reconciler, Action requestRerender) where TControl : UIElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol.Descriptor`.
