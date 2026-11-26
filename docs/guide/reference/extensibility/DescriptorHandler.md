# DescriptorHandler\<TElement, TControl\>

Spec 047 §6 / §14 Phase 2 (Q1 spike) — interpreter that drives a [`ControlDescriptor<TElement, TControl>`](ControlDescriptor.md) against the v1 protocol surface.

Implemented as a regular [`IElementHandler<TElement, TControl>`](IElementHandler.md) so the `V1HandlerAdapter` dispatch shell is identical between the descriptor and hand-coded handler paths — the only thing that differs between the Q1 A|B variants is the body of Mount / Update. Any measured delta is the interpreter's tax, not a different dispatch shape.

**Mount sequence** (matches the hand-coded handlers' KD-1b ordering):

1. Rent the control via [`MountContext.RentControl`](lifecycle-contexts.md#rentcontrol).
2. Iterate [`ControlDescriptor.Properties`](ControlDescriptor.md#properties_1) and invoke [`PropEntry.Mount`](PropEntry.md#mounttcontrol-telement) on each — all bare initial writes happen first.
3. Iterate again and invoke [`PropEntry.EnsureSubscribed`](PropEntry.md#ensuresubscribed) — controlled entries wire their change-event trampolines now (no echo on the just- written values because nothing is listening yet).
4. Apply setters.

```csharp
public class DescriptorHandler<TElement, TControl> : IElementHandler<TElement, TControl> where TElement : Element where TControl : FrameworkElement, new()
```

Namespace: `Microsoft.UI.Reactor.Core.V1Protocol.Descriptor`.

**Learn more:** [Extending Reactor with Native Controls](../../extending-reactor-controls.md), [The Control Reconciler Protocol](../../control-reconciler-protocol.md)

## Remarks

Spec 048 §7 — **unsealed** so the built-in catalog can expose a thin, `new()`-constructible registration subclass per descriptor (e.g. `TextBlockDescriptorHandler(): DescriptorHandler<…>(TextBlockDescriptor.Descriptor)`). That subclass is what the `Reg` shim instantiates via its `static () => new THandler()` lambda, so the descriptor path registers through the exact same zero-closure mechanism as a hand-coded handler. The base interpreter has no virtual members the subclass overrides; unsealing carries no dispatch cost (calls already route through the [`IElementHandler<TElement, TControl>`](IElementHandler.md) interface via the adapter).

## Constructors

### `DescriptorHandler`

```csharp
public DescriptorHandler(ControlDescriptor<TElement, TControl> descriptor)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `descriptor` | [`ControlDescriptor<TElement, TControl>`](ControlDescriptor.md) |  |

## Properties

### `Children`

Children strategy surfaced to `V1HandlerAdapter`. Returns the descriptor's strategy except when it's an [`ItemsHost<TElement, TControl>`](children-strategies.md#itemshosttelement-tcontrol) — that one is dispatched inline by [`DescriptorHandler.Mount`](#mount) / [`DescriptorHandler.Update`](#update) before the prop loop runs so initial writes like `SelectedIndex` land against a populated collection (matches legacy mount ordering).

```csharp
public ChildrenStrategy<TElement, TControl>? Children  // get
```

### `ChildrenForUnmount`

Issue #375 — on Unmount the bind-before-props ordering constraint that motivates hiding [`ItemsHost<TElement, TControl>`](children-strategies.md#itemshosttelement-tcontrol) / `IItemsBinderStrategy` strategies from [`DescriptorHandler.Children`](#children) no longer applies, so expose the descriptor's real strategy here. Lets `V1HandlerAdapter`'s unmount-side dispatch walk descendant items (e.g. `TabView` tabs whose content holds Components) and fire their `UseEffect` cleanups.

```csharp
public ChildrenStrategy<TElement, TControl>? ChildrenForUnmount  // get
```

### `Descriptor`

The descriptor this handler interprets. Exposed for tests and bench harnesses — not part of the steady-state author API.

```csharp
public ControlDescriptor<TElement, TControl> Descriptor  // get
```

## Methods

### `AfterChildrenMount`

§14 Phase 3 prelude (Engine A1) — forwards to the descriptor's optional [`ControlDescriptor.AfterChildrenMount`](ControlDescriptor.md#afterchildrenmount) callback. The adapter invokes this after every child has mounted.

```csharp
public void AfterChildrenMount(MountContext ctx, TElement element, TControl control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`MountContext`](lifecycle-contexts.md#mountcontext) |  |
| `element` | `TElement` |  |
| `control` | `TControl` |  |

### `Mount`

```csharp
public TControl Mount(MountContext ctx, TElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`MountContext`](lifecycle-contexts.md#mountcontext) |  |
| `el` | `TElement` |  |

### `Unmount`

Forwards to the descriptor's optional [`ControlDescriptor.OnUnmount`](ControlDescriptor.md#onunmount) hook. Without this the descriptor path had no teardown seam at all, so control-scoped state a descriptor set up during mount — most notably a one-shot `Loaded` subscription carrying a deferred write — could outlive the mount that created it.

```csharp
public void Unmount(UnmountContext ctx, TControl control)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UnmountContext`](lifecycle-contexts.md#unmountcontext) |  |
| `control` | `TControl` |  |

### `Update`

```csharp
public void Update(UpdateContext ctx, TElement oldEl, TElement newEl, TControl ctrl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UpdateContext`](lifecycle-contexts.md#updatecontext) |  |
| `oldEl` | `TElement` |  |
| `newEl` | `TElement` |  |
| `ctrl` | `TControl` |  |
