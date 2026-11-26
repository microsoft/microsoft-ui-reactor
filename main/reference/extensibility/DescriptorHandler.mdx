---
title: "DescriptorHandler<TElement, TControl>"
seo:
  description: "Spec 047 §6 / §14 Phase 2 (Q1 spike) — interpreter that drives a ControlDescriptor<TElement, TControl> against the v1 protocol surface."
sidebar:
  label: "DescriptorHandler"
---

Spec 047 §6 / §14 Phase 2 (Q1 spike) — interpreter that drives a [`ControlDescriptor<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/ControlDescriptor) against the v1 protocol surface.

Implemented as a regular [`IElementHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler) so the `V1HandlerAdapter` dispatch shell is identical between the descriptor and hand-coded handler paths — the only thing that differs between the Q1 A|B variants is the body of Mount / Update. Any measured delta is the interpreter's tax, not a different dispatch shape.

**Mount sequence** (matches the hand-coded handlers' KD-1b ordering):

1. Rent the control via [`MountContext.RentControl`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#rentcontrol).
2. Iterate [`ControlDescriptor.Properties`](/microsoft-ui-reactor/main/reference/extensibility/ControlDescriptor#properties_1) and invoke [`PropEntry.Mount`](/microsoft-ui-reactor/main/reference/extensibility/PropEntry#mounttcontrol-telement) on each — all bare initial writes happen first.
3. Iterate again and invoke [`PropEntry.EnsureSubscribed`](/microsoft-ui-reactor/main/reference/extensibility/PropEntry#ensuresubscribed) — controlled entries wire their change-event trampolines now (no echo on the just- written values because nothing is listening yet).
4. Apply setters.

```csharp
public class DescriptorHandler<TElement, TControl> : IElementHandler<TElement, TControl> where TElement : Element where TControl : FrameworkElement, new()
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol.Descriptor`.

**Learn more:** [Extending Reactor with Native Controls](/microsoft-ui-reactor/main/how-it-works/extending-reactor-controls), [The Control Reconciler Protocol](/microsoft-ui-reactor/main/how-it-works/control-reconciler-protocol)

## Remarks [#remarks]

Spec 048 §7 — **unsealed** so the built-in catalog can expose a thin, `new()`-constructible registration subclass per descriptor (e.g. `TextBlockDescriptorHandler(): DescriptorHandler<…>(TextBlockDescriptor.Descriptor)`). That subclass is what the `Reg` shim instantiates via its `static () => new THandler()` lambda, so the descriptor path registers through the exact same zero-closure mechanism as a hand-coded handler. The base interpreter has no virtual members the subclass overrides; unsealing carries no dispatch cost (calls already route through the [`IElementHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/IElementHandler) interface via the adapter).

## Constructors [#constructors]

### `DescriptorHandler` [#descriptorhandler]

```csharp
public DescriptorHandler(ControlDescriptor<TElement, TControl> descriptor)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `descriptor` | [`ControlDescriptor<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/ControlDescriptor) |  |

## Properties [#properties]

### `Children` [#children]

Children strategy surfaced to `V1HandlerAdapter`. Returns the descriptor's strategy except when it's an [`ItemsHost<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/children-strategies#itemshosttelement-tcontrol) — that one is dispatched inline by [`DescriptorHandler.Mount`](#mount) / [`DescriptorHandler.Update`](#update) before the prop loop runs so initial writes like `SelectedIndex` land against a populated collection (matches legacy mount ordering).

```csharp
public ChildrenStrategy<TElement, TControl>? Children  // get
```

### `ChildrenForUnmount` [#childrenforunmount]

Issue #375 — on Unmount the bind-before-props ordering constraint that motivates hiding [`ItemsHost<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/children-strategies#itemshosttelement-tcontrol) / `IItemsBinderStrategy` strategies from [`DescriptorHandler.Children`](#children) no longer applies, so expose the descriptor's real strategy here. Lets `V1HandlerAdapter`'s unmount-side dispatch walk descendant items (e.g. `TabView` tabs whose content holds Components) and fire their `UseEffect` cleanups.

```csharp
public ChildrenStrategy<TElement, TControl>? ChildrenForUnmount  // get
```

### `Descriptor` [#descriptor]

The descriptor this handler interprets. Exposed for tests and bench harnesses — not part of the steady-state author API.

```csharp
public ControlDescriptor<TElement, TControl> Descriptor  // get
```

## Methods [#methods]

### `AfterChildrenMount` [#afterchildrenmount]

§14 Phase 3 prelude (Engine A1) — forwards to the descriptor's optional [`ControlDescriptor.AfterChildrenMount`](/microsoft-ui-reactor/main/reference/extensibility/ControlDescriptor#afterchildrenmount) callback. The adapter invokes this after every child has mounted.

```csharp
public void AfterChildrenMount(MountContext ctx, TElement element, TControl control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`MountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#mountcontext) |  |
| `element` | `TElement` |  |
| `control` | `TControl` |  |

### `Mount` [#mount]

```csharp
public TControl Mount(MountContext ctx, TElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`MountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#mountcontext) |  |
| `el` | `TElement` |  |

### `Unmount` [#unmount]

Forwards to the descriptor's optional [`ControlDescriptor.OnUnmount`](/microsoft-ui-reactor/main/reference/extensibility/ControlDescriptor#onunmount) hook. Without this the descriptor path had no teardown seam at all, so control-scoped state a descriptor set up during mount — most notably a one-shot `Loaded` subscription carrying a deferred write — could outlive the mount that created it.

```csharp
public void Unmount(UnmountContext ctx, TControl control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UnmountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#unmountcontext) |  |
| `control` | `TControl` |  |

### `Update` [#update]

```csharp
public void Update(UpdateContext ctx, TElement oldEl, TElement newEl, TControl ctrl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UpdateContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#updatecontext) |  |
| `oldEl` | `TElement` |  |
| `newEl` | `TElement` |  |
| `ctrl` | `TControl` |  |
