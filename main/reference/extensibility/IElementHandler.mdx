---
title: "IElementHandler<TElement, TControl>"
description: "Spec 047 §4 / §14 Phase 1 (1.6) — author-facing handler contract for porting a control onto the v1 protocol."
sidebar:
  label: "IElementHandler"
---

Implementations register themselves via [`Reconciler.RegisterHandler`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler#registerhandler); dispatch is exact-type by `element.GetType()` (§13 Q17). Update returns `void` — substitution is forbidden (§13 Q12); a type change flows through the unmount-and-remount path on the wrapping container.

**Hot path:** dispatch is dictionary lookup + interface call (both already paid by `V1HandlerRegistry`). Implementations should avoid allocations in [`IElementHandler.Mount`](#mount) / [`IElementHandler.Update`](#update) bodies; the `ref struct` context types are allocation-free by construction.

**Echo suppression for value-bearing controls (issue #206).** If the control raises the same change event for a programmatic property write as it does for a user edit, a write from [`IElementHandler.Update`](#update) would echo back through the trampoline into the owning component's `OnChanged` state setter — indistinguishable from real input. Guard every programmatic controlled-value write with the supported, RCW-safe public primitive [`ReactorBinding<TElement>`](/microsoft-ui-reactor/main/reference/extensibility/ReactorBinding-1)'s `WriteSuppressed` method (or the static [`ReactorBinding`](/microsoft-ui-reactor/main/reference/extensibility/ReactorBinding)): `ctx.BindFor(ctrl, newEl).WriteSuppressed(() => ctrl.Value = newEl.Value)`. Pair it 1:1 with the write and gate it on an equality check so the token is always consumed by the engine-provoked echo. Descriptor authors get this for free through `.Controlled` / `.HandCodedControlled`. Never reach into the internal `ChangeEchoSuppressor`. See `docs/guide/extending-reactor-controls.md`.

```csharp
public interface IElementHandler<TElement, TControl> where TElement : Element where TControl : UIElement
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol`.

**Learn more:** [Extending Reactor with Native Controls](/microsoft-ui-reactor/main/how-it-works/extending-reactor-controls), [The Control Reconciler Protocol](/microsoft-ui-reactor/main/how-it-works/control-reconciler-protocol)

## Properties [#properties]

### `Children` [#children]

Optional children strategy. Engine dispatches through it when non-null (and not [`None<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/children-strategies#nonetelement-tcontrol)); otherwise the handler is a leaf for the purposes of child reconciliation.

```csharp
public virtual ChildrenStrategy<TElement, TControl>? Children  // get
```

### `ChildrenForUnmount` [#childrenforunmount]

Issue #375 — children strategy the engine uses for the unmount-side child walk. Defaults to [`IElementHandler.Children`](#children); some implementations (notably [`DescriptorHandler<TElement, TControl>`](/microsoft-ui-reactor/main/reference/extensibility/DescriptorHandler)) suppress [`IElementHandler.Children`](#children) when they dispatch a strategy inline during Mount/Update for ordering reasons (e.g. items-binder strategies whose collection must be populated before `SelectedIndex` initial writes land). On Unmount the ordering constraint no longer applies, so they expose the real strategy here so descendant Component `UseEffect` cleanups still fire.

```csharp
public virtual ChildrenStrategy<TElement, TControl>? ChildrenForUnmount  // get
```

## Methods [#methods]

### `AfterChildrenMount` [#afterchildrenmount]

Spec 047 §14 Phase 3 prelude (Engine A1) — optional hook the engine invokes from `V1HandlerAdapter` after the [`IElementHandler.Children`](#children) strategy has mounted/bound every child (and after any items-binder strategy the handler dispatched inline). Default no-op.

Override this to wire events whose subscription must happen strictly after children-add — e.g. `TabView.SelectionChanged`, which WinUI fires spuriously while the first tab is being added if the handler subscribes during the prop-apply phase. Subscribing here side- steps that first-add echo.

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

Create or rent the WinUI control, apply initial writes, and subscribe event trampolines. Returns the control the engine places into the parent's children collection.

```csharp
public TControl Mount(MountContext ctx, TElement element)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`MountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#mountcontext) |  |
| `element` | `TElement` |  |

### `ReconcileChildren` [#reconcilechildren]

Optional override for handlers that want to drive child reconciliation manually instead of through [`IElementHandler.Children`](#children). Phase 1 strategy dispatch routes through [`IElementHandler.Children`](#children); this overload is retained for parity with the spec §4 surface and Phase 3 use.

```csharp
public void ReconcileChildren(MountContext ctx, TElement oldEl, TElement newEl, TControl control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`MountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#mountcontext) |  |
| `oldEl` | `TElement` |  |
| `newEl` | `TElement` |  |
| `control` | `TControl` |  |

### `Unmount` [#unmount]

Optional override for handlers with explicit teardown (e.g. control-side IDisposable resources). Default no-op — the engine still runs the pool reset contract.

```csharp
public void Unmount(UnmountContext ctx, TControl control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UnmountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#unmountcontext) |  |
| `control` | `TControl` |  |

### `Update` [#update]

Diff `oldEl` vs `newEl` and apply minimal writes to `control`. Returns void: the control identity is preserved (§13 Q12).

```csharp
public void Update(UpdateContext ctx, TElement oldEl, TElement newEl, TControl control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UpdateContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#updatecontext) |  |
| `oldEl` | `TElement` |  |
| `newEl` | `TElement` |  |
| `control` | `TControl` |  |
